﻿using LanPlayServer.Network;
using LanPlayServer.Network.Types;
using LanPlayServer.Utils;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LanPlayServer
{
    enum GameLockReason
    {
        None,
        UpdateNetworkInfo,
        SetOwner,
        Connect,
        ConnectNode,
        ConnectBroadcast,
        ConnectProxy,
        ConnectFinal,
        Reject,
        SetAcceptPolicy,
        SetAdvertiseData,
        HandleExternalProxyState,
        Disconnect,
        DisconnectDHCP,
        DisconnectInfoRemove,
        DisconnectBroadcast,
        Close
    }

    public class HostedGame : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private const uint NetworkBaseAddress = 0x0a720000; // 10.114.0.0 (our "virtual network")
        private const uint NetworkSubnetMask  = 0xffff0000; // 255.255.0.0

        private readonly ReaderWriterLockSlim _lock;
        private GameLockReason _lockReason;
        private readonly List<LdnSession> _players;
        private readonly VirtualDhcp      _dhcp;

        private bool                _closed;
        private ExternalProxyConfig _externalConfig;
        private ExternalProxyConfig _privateConfig;

        private NetworkInfo _info;
        public NetworkInfo Info
        {
            get
            {
                _lock.EnterReadLock();

                NetworkInfo result = _info;

                _lock.ExitReadLock();

                return result;
            }
        }

        public string Id { get; }

        public LdnSession Owner { get; private set; }
        public string OwnerId => Owner.StringId;

        private string _passphrase;
        public string Passphrase
        {
            get
            {
                _lock.EnterReadLock();

                string result = _passphrase;

                _lock.ExitReadLock();

                return result;
            }
        }

        private string _gameVersion;
        private bool _isP2P;

        public string GameVersion
        {
            get
            {
                _lock.EnterReadLock();

                string result = _gameVersion;

                _lock.ExitReadLock();

                return result;
            }
            private set
            {
                if (_gameVersion != value)
                {
                    _gameVersion = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public int Players
        {
            get
            {
                int result;

                _lock.EnterReadLock();

                result = _players.Count;

                _lock.ExitReadLock();

                return result;
            }
        }

        public bool IsP2P
        {
            get => _isP2P;
            private set
            {
                if (_isP2P != value)
                {
                    _isP2P = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public HostedGame(string id, NetworkInfo info, AddressList dhcpConfig)
        {
            Id = id;

            _lock    = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            _players = new List<LdnSession>();
            _dhcp    = new VirtualDhcp(NetworkBaseAddress, NetworkSubnetMask, dhcpConfig);

            UpdateNetworkInfo(info);
        }

        public bool TestReadLock()
        {
            if (!_lock.TryEnterReadLock(2000))
            {
                Console.WriteLine($"Lock broken: {_lockReason} on {_info.NetworkId.IntentId.LocalCommunicationId:x16} with {_players.Count} players.");

                return true;
            }

            _lock.ExitReadLock();

            return false;
        }

        private void EnterLock(GameLockReason reason)
        {
            _lock.EnterWriteLock();
            _lockReason = reason;
        }

        private void ExitLock()
        {
            _lockReason = GameLockReason.None;
            _lock.ExitWriteLock();
        }

        public void UpdateNetworkInfo(NetworkInfo info)
        {
            EnterLock(GameLockReason.UpdateNetworkInfo);

            _info = info;

            NotifyPropertyChanged(nameof(Info));

            ExitLock();
        }

        private byte[] AddressTo16Byte(IPAddress address)
        {
            byte[] ipBytes  = new byte[16];
            byte[] srcBytes = address.GetAddressBytes();

            Array.Copy(srcBytes, 0, ipBytes, 0, srcBytes.Length);

            return ipBytes;
        }

        public void SetOwner(LdnSession session, RyuNetworkConfig request)
        {
            EnterLock(GameLockReason.SetOwner);

            try
            {
                Owner = session;
                _passphrase = session.Passphrase;

                GameVersion = Encoding.UTF8.GetString(request.GameVersion.AsSpan().ToArray(), 0, request.GameVersion.Length).Trim('\0');

                if (request.ExternalProxyPort != 0)
                {
                    IsP2P = true;

                    IPAddress address = (session.Socket.RemoteEndPoint as IPEndPoint).Address;

                    _externalConfig = new ExternalProxyConfig()
                    {
                        AddressFamily = address.AddressFamily,
                        ProxyPort = request.ExternalProxyPort,
                    };

                    AddressTo16Byte(address).CopyTo(_externalConfig.ProxyIp.AsSpan());

                    _privateConfig = new ExternalProxyConfig()
                    {
                        ProxyIp = request.PrivateIp,
                        AddressFamily = request.AddressFamily,
                        ProxyPort = request.InternalProxyPort
                    };
                }
            }
            catch
            {
                ExitLock();
                throw;
            }

            ExitLock();
        }

        private void InitExternalProxy(LdnSession session)
        {
            IPAddress  address          = (session.Socket.RemoteEndPoint as IPEndPoint).Address;
            Span<byte> addressBytes     = AddressTo16Byte(address).AsSpan();
            bool       sessionIsPrivate = address.AddressFamily == _externalConfig.AddressFamily && addressBytes.SequenceEqual(_externalConfig.ProxyIp.AsSpan());
            byte[]     token            = Convert.FromHexString(Guid.NewGuid().ToString().Replace("-", ""));

            // The proxy host needs to know about the new joiner.

            ExternalProxyToken tokenMsg = new()
            {
                VirtualIp     = session.IpAddress,
                AddressFamily = address.AddressFamily
            };

            token.CopyTo(tokenMsg.Token.AsSpan());

            if (!sessionIsPrivate)
            {
                addressBytes.CopyTo(tokenMsg.PhysicalIp.AsSpan());
            }

            Owner.SendAsync(RyuLdnProtocol.Encode(PacketId.ExternalProxyToken, tokenMsg));

            // Tell the joiner about the new proxy host.

            ExternalProxyConfig configCopy = sessionIsPrivate ? _privateConfig : _externalConfig;

            token.CopyTo(configCopy.Token.AsSpan());

            session.SendAsync(RyuLdnProtocol.Encode(PacketId.ExternalProxy, configCopy));
        }

        public bool Connect(LdnSession session, NodeInfo node)
        {
            EnterLock(GameLockReason.Connect);

            if (_closed || _info.Ldn.NodeCount == _info.Ldn.NodeCountMax)
            {
                ExitLock();

                return false;
            }

            uint ip = _dhcp.RequestIpV4(session.MacAddress.AsSpan());
            if (!session.SetIpV4(ip, NetworkSubnetMask, !IsP2P))
            {
                _dhcp.ReturnIpV4(ip);
            }

            node.Ipv4Address = session.IpAddress;

            _lockReason = GameLockReason.ConnectNode;

            // Add the client to the network info.
            int nodeId = LocateEmptyNode();
            _info.Ldn.NodeCount++;

            node.NodeId = (byte)nodeId;
            session.NodeId = nodeId;

            _info.Ldn.Nodes[nodeId] = node;

            _lockReason = GameLockReason.ConnectProxy;
            if (IsP2P)
            {
                InitExternalProxy(session);
            }

            _lockReason = GameLockReason.ConnectBroadcast;
            BroadcastNetworkInfoInLock();

            session.CurrentGame = this;

            _players.Add(session);
            NotifyPropertyChanged(nameof(Players));

            _lockReason = GameLockReason.ConnectFinal;
            session.SendAsync(RyuLdnProtocol.Encode(PacketId.Connected, _info));

            ExitLock();

            return true;
        }

        public void RemoveFromInfo(uint ip)
        {
            ref LdnNetworkInfo ldn       = ref _info.Ldn;
            bool               removed   = false;
            int                nodeCount = ldn.NodeCount;

            for (int i = 0; i < nodeCount; i++)
            {
                if (ldn.Nodes[i].Ipv4Address == ip)
                {
                    ldn.NodeCount--;
                    ldn.Nodes[i].IsConnected = 0;
                    removed = true;
                }

                /*
                if (removed)
                {
                    // Move the next client into this space. Keep doing this repeatedly.
                    if (i + 1 < ldn.NodeCountMax)
                    {
                        ldn.Nodes[i] = ldn.Nodes[i + 1];
                    }
                    else
                    {
                        ldn.Nodes[i] = new NodeInfo();
                    }
                }
                */
            }
        }

        private int LocateEmptyNode()
        {
            Array8<NodeInfo> nodes = Info.Ldn.Nodes;

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].IsConnected == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        // Proxy handlers

        private void RouteMessage(LdnSession sender, ref ProxyInfo info, Action<LdnSession> action)
        {
            if (info.SourceIpV4 == 0)
            {
                // If they sent from a connection bound on 0.0.0.0, make others see it as them.
                info.SourceIpV4 = sender.IpAddress;
            }
            else if (info.SourceIpV4 != sender.IpAddress)
            {
                // Can't pretend to be somebody else.
                return;
            }

            uint destIp = info.DestIpV4;

            if (destIp == 0xc0a800ff)
            {
                destIp = _dhcp.BroadcastAddress;
            }

            bool isBroadcast = destIp == _dhcp.BroadcastAddress;

            _lock.EnterReadLock();

            if (isBroadcast)
            {
                _players.ForEach(player =>
                {
                    action(player);
                });
            }
            else
            {
                LdnSession target = _players.FirstOrDefault(player => player.IpAddress == destIp);

                if (target != null)
                {
                    action(target);
                }
            }

            _lock.ExitReadLock();
        }

        public void HandleReject(LdnSession sender, LdnHeader header, RejectRequest reject)
        {
            if (sender == Owner)
            {
                EnterLock(GameLockReason.Reject);

                if (reject.NodeId >= _players.Count)
                {
                    ExitLock();

                    sender.SendAsync(RyuLdnProtocol.Encode(PacketId.NetworkError, new NetworkErrorMessage() { Error = NetworkError.RejectFailed }));
                }
                else
                {
                    Disconnect(_players.FirstOrDefault(player => player.NodeId == reject.NodeId), false);

                    ExitLock();
                }
            }
            else
            {
                sender.SendAsync(RyuLdnProtocol.Encode(PacketId.NetworkError, new NetworkErrorMessage() { Error = NetworkError.RejectFailed }));
            }

            sender.SendAsync(RyuLdnProtocol.Encode(PacketId.RejectReply));
        }

        public void HandleSetAcceptPolicy(LdnSession sender, LdnHeader header, SetAcceptPolicyRequest policy)
        {
            if (sender == Owner)
            {
                EnterLock(GameLockReason.SetAcceptPolicy);

                _info.Ldn.StationAcceptPolicy = (byte)policy.StationAcceptPolicy;

                BroadcastNetworkInfoInLock();

                ExitLock();
            }
        }

        public void HandleSetAdvertiseData(LdnSession sender, LdnHeader header, byte[] data)
        {
            if (sender == Owner)
            {
                EnterLock(GameLockReason.SetAdvertiseData);

                _info.Ldn.AdvertiseDataSize = (ushort)data.Length;

                Array.Resize(ref data, 0x180);

                data.CopyTo(_info.Ldn.AdvertiseData.AsSpan());

                BroadcastNetworkInfoInLock();

                ExitLock();
            }
        }

        public void HandleExternalProxyState(LdnSession sender, LdnHeader header, ExternalProxyConnectionState state)
        {
            EnterLock(GameLockReason.HandleExternalProxyState);

            if (sender != Owner)
            {
                // Only the owner can update external proxy state.
                ExitLock();

                return;
            }

            LdnSession player = _players.FirstOrDefault(player => player.IpAddress == state.IpAddress);

            if (player != null)
            {
                if (!state.Connected)
                {
                    // Indicate that the player is no longer connected to the game.

                    player.SendAsync(RyuLdnProtocol.Encode(PacketId.Disconnect, new DisconnectMessage()));

                    Disconnect(player, true);
                }
            }

            ExitLock();
        }

        public void HandleProxyDisconnect(LdnSession sender, LdnHeader header, ProxyDisconnectMessage message)
        {
            RouteMessage(sender, ref message.Info, (target) =>
            {
                target.SendAsync(RyuLdnProtocol.Encode(PacketId.ProxyDisconnect, message));
            });
        }

        public void HandleProxyData(LdnSession sender, LdnHeader header, ProxyDataHeader message, byte[] data)
        {
            RouteMessage(sender, ref message.Info, (target) =>
            {
                target.SendAsync(RyuLdnProtocol.Encode(PacketId.ProxyData, message, data));
            });
        }

        public void HandleProxyConnectReply(LdnSession sender, LdnHeader header, ProxyConnectResponse message)
        {
            RouteMessage(sender, ref message.Info, (target) =>
            {
                target.SendAsync(RyuLdnProtocol.Encode(PacketId.ProxyConnectReply, message));
            });
        }

        public void HandleProxyConnect(LdnSession sender, LdnHeader header, ProxyConnectRequest message)
        {
            RouteMessage(sender, ref message.Info, (target) =>
            {
                target.SendAsync(RyuLdnProtocol.Encode(PacketId.ProxyConnect, message));
            });
        }

        private void DisconnectInternal(LdnSession session, bool expected)
        {
            EnterLock(GameLockReason.Disconnect);

            try
            {
                _players.Remove(session);
                NotifyPropertyChanged(nameof(Players));

                session.CurrentGame = null;

                if (IsP2P && !expected)
                {
                    Owner?.SendAsync(RyuLdnProtocol.Encode(PacketId.ExternalProxyState, new ExternalProxyConnectionState
                    {
                        IpAddress = session.IpAddress,
                        Connected = false
                    }));
                }

                _lockReason = GameLockReason.DisconnectDHCP;

                _dhcp.ReturnIpV4(session.IpAddress);

                _lockReason = GameLockReason.DisconnectInfoRemove;

                // Remove the client from the network info.
                RemoveFromInfo(session.IpAddress);

                _lockReason = GameLockReason.DisconnectBroadcast;

                BroadcastNetworkInfoInLock();
            }
            catch (Exception e)
            {
                Console.WriteLine("SUPER FATAL ERROR: " + e);
            }

            ExitLock();
        }

        public void Disconnect(LdnSession session, bool expected)
        {
            if (session == null)
            {
                return;
            }

            if (_lock.IsReadLockHeld && !_lock.IsWriteLockHeld)
            {
                // Can't upgrade the lock, try disconnect in the background.
                session.CurrentGame = null;

                Task.Run(() => DisconnectInternal(session, expected));
            }
            else
            {
                DisconnectInternal(session, expected);
            }
        }

        // NOTE: Unused.
        private void BroadcastNetworkInfo()
        {
            _lock.EnterReadLock();

            BroadcastNetworkInfoInLock();

            _lock.ExitReadLock();
        }

        private void BroadcastNetworkInfoInLock()
        {
            BroadcastInLock(RyuLdnProtocol.Encode(PacketId.SyncNetwork, _info));
        }

        private void BroadcastInLock(byte[] buffer)
        {
            foreach (LdnSession player in _players)
            {
                player.SendAsync(buffer);
            }
        }

        public void Close()
        {
            if (_lock.IsReadLockHeld && !_lock.IsWriteLockHeld)
            {
                // Can't upgrade the lock, try close in the background.

                Task.Run(Close);
            }
            else
            {
                EnterLock(GameLockReason.Close);

                Console.WriteLine($"CLOSING: {Id}");

                _closed = true;

                BroadcastInLock(RyuLdnProtocol.Encode(PacketId.Disconnect, new DisconnectMessage()));

                ExitLock();
            }
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}