using LanPlayServer.Network;
using LanPlayServer.Network.Types;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using System;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using LanPlayServer.Utils;
using Ryujinx.Common.Memory;

namespace LanPlayServer
{
    class LdnSession
    {
        private const int ExternalProxyTimeout = 2;

        public readonly Guid Id = Guid.NewGuid();
        public readonly IPEndPoint Endpoint;

        public HostedGame   CurrentGame { get; set; }
        public Array6<byte> MacAddress  { get; }
        public uint         IpAddress   { get; private set; }
        public uint         RealIpAddress { get; private set; }
        public string       Passphrase  { get; private set; } = "";

        public string     StringId => Id.ToString().Replace("-", "");

        private readonly LdnServer      _server;
        private readonly RyuLdnProtocol _protocol;
        private NetworkInfo[] _scanBuffer = new NetworkInfo[1];

        private PacketId? _receivePacketType = null;

        private readonly AutoResetEvent _packetReceived = new(false);
        private readonly ManualResetEvent _proxyConfigReceived = new(false);
        private readonly AutoResetEvent _testPingReceived = new(false);
        private long _lastMessageTicks = Stopwatch.GetTimestamp();
        private int _waitingPingID = -1;
        private byte _pingId;

        /// <summary>
        /// Node ID when in a game. This does not change while the user is still in that game.
        /// </summary>
        public int NodeId { get; set; }

        private bool _disconnected;
        private readonly object _connectionLock = new();

        private bool _connected;

        public LdnSession(IPEndPoint endpoint, LdnServer server, InitializeMessage message)
        {
            Endpoint = endpoint;
            _server = server;

            MacAddress = _server.MacAddresses.TryFind(Convert.ToHexString(message.Id.AsSpan()), message.MacAddress.AsSpan(), StringId);

            _protocol = new RyuLdnProtocol();

            // _protocol.Initialize               += HandleInitialize;
            _protocol.Passphrase               += HandlePassphrase;
            _protocol.CreateAccessPoint        += HandleCreateAccessPoint;
            _protocol.CreateAccessPointPrivate += HandleCreateAccessPointPrivate;
            _protocol.Reject                   += HandleReject;
            _protocol.SetAcceptPolicy          += HandleSetAcceptPolicy;
            _protocol.SetAdvertiseData         += HandleSetAdvertiseData;
            _protocol.Scan                     += HandleScan;
            _protocol.Connect                  += HandleConnect;
            _protocol.ConnectPrivate           += HandleConnectPrivate;
            _protocol.Disconnected             += HandleDisconnect;

            _protocol.ProxyConfig       += HandleProxyConfig;
            _protocol.ProxyConnect      += HandleProxyConnect;
            _protocol.ProxyConnectReply += HandleProxyConnectReply;
            _protocol.ProxyData         += HandleProxyData;
            _protocol.ProxyDisconnect   += HandleProxyDisconnect;

            _protocol.ExternalProxyState += HandleExternalProxyState;
            _protocol.Ping               += HandlePing;
            _protocol.TestPing           += HandleTestPing;

            _protocol.Any += HandleAny;

            Array16<byte> id = new();
            Convert.FromHexString(StringId).CopyTo(id.AsSpan());

            SendAsync(_protocol.Encode(PacketId.Initialize, new InitializeMessage { Id = id, MacAddress = MacAddress }));

            OnConnected();
        }

        public bool SendAsync(byte[] buffer)
        {
            Logger.Instance.Debug(ToString(), $"Sending packet to: {Endpoint}");

            return _server.SendAsync(Endpoint, buffer);
        }

        public bool SendAsyncSafe(PacketId packetId, byte[] buffer)
        {
            _receivePacketType = packetId;

            int numTries = 1;
            bool success = false;
            while (numTries < 3 && !success)
            {
                SendAsync(buffer);
                success = _packetReceived.WaitOne(500);
                numTries++;
            }

            _receivePacketType = null;

            return success;
        }

        private void HandleAny(IPEndPoint endpoint, LdnHeader header)
        {
            Logger.Instance.Debug(ToString(), $"{Endpoint} -> {(PacketId)header.Type}");
            if (_receivePacketType is not null && _receivePacketType == (PacketId)header.Type)
            {
                _packetReceived.Set();
            }
        }

        private string PrintIp()
        {
            return $"{RealIpAddress >> 24}.{(RealIpAddress >> 16) & 0xFF}.{(RealIpAddress >> 8) & 0xFF}.{RealIpAddress & 0xFF}";
        }

        public void Ping()
        {
            if (_waitingPingID != -1)
            {
                // The last ping was not responded to. Force a disconnect (async).
                Logger.Instance.Info(ToString(), $"Closing session with Id {Id} due to idle.");
                Disconnect();
            }
            else
            {
                long ticks      = Stopwatch.GetTimestamp();
                long deltaTicks = ticks - _lastMessageTicks;
                long deltaMs    = deltaTicks / (Stopwatch.Frequency / 1000);

                if (deltaMs > LdnServer.InactivityPingFrequency)
                {
                    byte pingId = _pingId++;

                    _waitingPingID = pingId;

                    Logger.Instance.Debug(ToString(), $"{Endpoint}: Sending ping...");

                    SendAsync(_protocol.Encode(PacketId.Ping, new PingMessage { Id = pingId, Requester = 0 }));
                }
            }
        }

        private void DisconnectFromGame()
        {
            HostedGame game = CurrentGame;

            game?.Disconnect(this, false);

            if (game?.Owner == this)
            {
                _server.CloseGame(game.Id);
            }
        }

        private void HandlePing(LdnHeader header, PingMessage ping)
        {
            if (ping.Requester == 0 && ping.Id == _waitingPingID)
            {
                // A response from this client. Still alive, reset the _waitingPingID. (getting the message will also reset the timer)
                _waitingPingID = -1;
            }
        }

        // private void HandleInitialize(LdnHeader header, InitializeMessage message)
        // {
        //     if (_initialized)
        //     {
        //         return;
        //     }
        //
        //     MacAddress = _server.MacAddresses.TryFind(Convert.ToHexString(message.Id.AsSpan()), message.MacAddress.AsSpan(), StringId);
        //
        //     Array16<byte> id = new();
        //     Convert.FromHexString(StringId).CopyTo(id.AsSpan());
        //
        //     SendAsync(_protocol.Encode(PacketId.Initialize, new InitializeMessage() { Id = id, MacAddress = MacAddress }));
        //
        //     _initialized = true;
        // }

        private void HandlePassphrase(LdnHeader header, PassphraseMessage message)
        {
            string passphrase = StringUtils.ReadUtf8String(message.Passphrase.AsSpan());
            Regex  match      = new Regex("Ryujinx-[0-9a-f]{8}");
            bool   valid      = passphrase == "" || (passphrase.Length == 16 && match.IsMatch(passphrase));

            Passphrase = valid ? passphrase : "";
        }

        private void HandleDisconnect(LdnHeader header, DisconnectMessage message)
        {
            DisconnectFromGame();
        }

        private void HandleReject(LdnHeader header, RejectRequest reject)
        {
            CurrentGame?.HandleReject(this, header, reject);
        }

        private void HandleSetAcceptPolicy(LdnHeader header, SetAcceptPolicyRequest policy)
        {
            CurrentGame?.HandleSetAcceptPolicy(this, header, policy);
        }

        private void HandleSetAdvertiseData(LdnHeader header, byte[] data)
        {
            CurrentGame?.HandleSetAdvertiseData(this, header, data);
        }

        private void HandleProxyConfig(LdnHeader header, ProxyConfig config)
        {
            _proxyConfigReceived.Set();
        }

        private void HandleExternalProxyState(LdnHeader header, ExternalProxyConnectionState state)
        {
            CurrentGame?.HandleExternalProxyState(this, header, state);
        }

        private void HandleProxyDisconnect(LdnHeader header, ProxyDisconnectMessage message)
        {
            CurrentGame?.HandleProxyDisconnect(this, header, message);
        }

        private void HandleProxyData(LdnHeader header, ProxyDataHeader message, byte[] data)
        {
            CurrentGame?.HandleProxyData(this, header, message, data);
        }

        private void HandleProxyConnectReply(LdnHeader header, ProxyConnectResponse data)
        {
            CurrentGame?.HandleProxyConnectReply(this, header, data);
        }

        private void HandleProxyConnect(LdnHeader header, ProxyConnectRequest message)
        {
            CurrentGame?.HandleProxyConnect(this, header, message);
        }

        private void OnConnected()
        {
            if (!_connected)
            {
                try
                {
                    RealIpAddress = GetSessionIp();
                }
                catch
                {
                    Logger.Instance.Error(ToString(), "IP unavailable!");
                    // Already disconnected?
                }

                Logger.Instance.Info(ToString(), $"LDN UDP session with Id {Id} connected! ({PrintIp()})");

                _connected = true;
            }
        }

        private void Disconnect()
        {
            _server.DisconnectSession(this);

            lock (_connectionLock)
            {
                _disconnected = true;
                DisconnectFromGame();
            }

            Logger.Instance.Info(ToString(), $"LDN UDP session with Id {Id} disconnected! ({PrintIp()})");

            _protocol.Dispose();
        }

        public void OnReceived(byte[] buffer, long offset, long size)
        {
            try
            {
                OnConnected();

                _protocol.Read(Endpoint, buffer, (int)offset, (int)size);

                _lastMessageTicks = Stopwatch.GetTimestamp();
            }
            catch (Exception e)
            {
                Logger.Instance.Error(ToString(), $"Caught exception for session with Id {Id}: {e}");
            }
        }

        // protected override void OnError(SocketError error)
        // {
        //     Console.WriteLine($"LDN TCP session caught an error with code {error}");
        // }

        private uint GetSessionIp()
        {
            IPAddress remoteIp = Endpoint.Address;
            byte[]    bytes    = remoteIp.GetAddressBytes();

            Array.Reverse(bytes);

            return BitConverter.ToUInt32(bytes);
        }

        public bool SetIpV4(uint ip, uint subnet, bool internalProxy)
        {
            if (_server.UseProxy)
            {
                IpAddress = ip;

                if (internalProxy)
                {
                    ProxyConfig config = new ProxyConfig
                    {
                        ProxyIp         = ip,
                        ProxySubnetMask = subnet
                    };

                    // Tell the client about the proxy configuration.
                    int proxyConfigTry = 0;
                    bool configReceived = false;
                    while (proxyConfigTry < 3 && !configReceived)
                    {
                        SendAsync(_protocol.Encode(PacketId.ProxyConfig, config));
                        configReceived = _proxyConfigReceived.WaitOne(500);
                        proxyConfigTry++;
                    }
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        private void HandleScan(LdnHeader ldnPacket, ScanFilter filter)
        {
            int games = _server.Scan(ref _scanBuffer, filter, Passphrase, CurrentGame);

            for (int i = 0; i < games; i++)
            {
                NetworkInfo info = _scanBuffer[i];

                SendAsync(_protocol.Encode(PacketId.ScanReply, info));
            }

            SendAsync(_protocol.Encode(PacketId.ScanReplyEnd));
        }

        private void HandleCreateAccessPoint(LdnHeader ldnPacket, CreateAccessPointRequest request, byte[] advertiseData)
        {
            if (CurrentGame != null)
            {
                // Cannot create an access point while in a game.
                SendAsync(_protocol.Encode(PacketId.NetworkError, new NetworkErrorMessage { Error = NetworkError.Unknown }));

                return;
            }

            string id = Guid.NewGuid().ToString().Replace("-", "");

            AddressList dhcpConfig = new AddressList();

            AccessPointConfigToNetworkInfo(id, request.NetworkConfig, request.UserConfig, request.RyuNetworkConfig, request.SecurityConfig, dhcpConfig, advertiseData);
        }

        private void HandleCreateAccessPointPrivate(LdnHeader ldnPacket, CreateAccessPointPrivateRequest request, byte[] advertiseData)
        {
            if (CurrentGame != null)
            {
                // Cannot create an access point while in a game.
                SendAsync(_protocol.Encode(PacketId.NetworkError, new NetworkErrorMessage { Error = NetworkError.Unknown }));

                return;
            }

            string id = Convert.ToHexString(request.SecurityParameter.SessionId.AsSpan());

            AccessPointConfigToNetworkInfo(id, request.NetworkConfig, request.UserConfig, request.RyuNetworkConfig, request.SecurityConfig, request.AddressList, advertiseData);
        }

        private void AccessPointConfigToNetworkInfo(string id, NetworkConfig networkConfig, UserConfig userConfig, RyuNetworkConfig ryuNetworkConfig, SecurityConfig securityConfig, AddressList dhcpConfig, byte[] advertiseData)
        {
            string userId = StringId;

            Array16<byte> sessionID = new();
            Convert.FromHexString(id).CopyTo(sessionID.AsSpan());

            NetworkInfo networkInfo = new NetworkInfo()
            {
                NetworkId = new NetworkId()
                {
                    IntentId = new IntentId()
                    {
                        LocalCommunicationId = networkConfig.IntentId.LocalCommunicationId,
                        SceneId              = networkConfig.IntentId.SceneId
                    },
                    SessionId = sessionID
                },
                Common = new CommonNetworkInfo()
                {
                    Channel     = networkConfig.Channel,
                    LinkLevel   = 3,
                    NetworkType = 2,
                    MacAddress  = MacAddress,
                    Ssid        = new Ssid()
                    {
                        Length = 32,
                    }
                },
                Ldn = new LdnNetworkInfo()
                {
                    SecurityMode      = (ushort)securityConfig.SecurityMode,
                    NodeCountMax      = networkConfig.NodeCountMax,
                    NodeCount         = 0,
                    AdvertiseDataSize = (ushort)advertiseData.Length,
                    AuthenticationId  = 0
                }
            };

            "12345678123456781234567812345678"u8.ToArray().CopyTo(networkInfo.Common.Ssid.Name.AsSpan());
            advertiseData.CopyTo(networkInfo.Ldn.AdvertiseData.AsSpan());

            NodeInfo myInfo = new NodeInfo()
            {
                Ipv4Address               = IpAddress,
                MacAddress                = MacAddress,
                NodeId                    = 0x00,
                IsConnected               = 0x01,
                UserName                  = userConfig.UserName,
                LocalCommunicationVersion = networkConfig.LocalCommunicationVersion,
            };

            for (int i = 0; i < 8; i++)
            {
                networkInfo.Ldn.Nodes[i] = new NodeInfo();
            }

            if (ryuNetworkConfig.ExternalProxyPort != 0 && !IsProxyReachable(ryuNetworkConfig.ExternalProxyPort))
            {
                ryuNetworkConfig.ExternalProxyPort = 0;
                SendAsync(_protocol.Encode(PacketId.NetworkError, new NetworkErrorMessage { Error = NetworkError.PortUnreachable }));
            }

            /*
            if (networkInfo.NetworkId.IntentId.LocalCommunicationId == 0x0100abf008968000ul)
            {
                SendAsync(_protocol.Encode(PacketId.NetworkError, new NetworkErrorMessage { Error = NetworkError.Unknown }));
                return;
            }
            */

            HostedGame game = _server.CreateGame(id, networkInfo, dhcpConfig, userId);

            if (game == null)
            {
                SendAsync(_protocol.Encode(PacketId.NetworkError, new NetworkErrorMessage { Error = NetworkError.Unknown }));
                return;
            }

            lock (_connectionLock)
            {
                if (_disconnected)
                {
                    Logger.Instance.Warning(ToString(), $"Emergency disconnect: {id}");
                    game = null;
                }

                game?.SetOwner(this, ryuNetworkConfig);
                game?.Connect(this, myInfo);
            }

            if (game == null)
            {
                Logger.Instance.Warning(ToString(), $"Null close: {id}");
                _server.CloseGame(id);
            }
        }

        private void HandleTestPing(IPEndPoint endpoint, LdnHeader header, PingMessage message)
        {
            if (message is { Id: 255, Requester: 255 })
            {
                _testPingReceived.Set();
            }
        }

        private bool IsProxyReachable(ushort port)
        {
            // Attempt to establish a connection to the p2p server owned by the host.
            // We don't need to send anything, just establish a TCP connection.
            // If that is not possible, then their external proxy isn't reachable from the internet.

            IPEndPoint ep = new IPEndPoint(Endpoint.Address, port);

            var client = new NetCoreServer.UdpClient(ep);

            client.Send(_protocol.Encode(PacketId.Ping, new PingMessage {Id = 255, Requester = 0}));

            if (_testPingReceived.WaitOne(ExternalProxyTimeout * 1000))
            {
                client.Dispose();

                return true;
            }

            client.Dispose();

            return false;
        }

        private void ConnectImpl(string id, UserConfig userConfig, uint localCommunicationVersion)
        {
            HostedGame game = _server.FindGame(id);

            if (game != null)
            {
                NetworkInfo gameInfo = game.Info;

                // Node 0 will contain the expected version (the host). If there is no match, we cannot connect.
                uint hostVersion   = gameInfo.Ldn.Nodes[0].LocalCommunicationVersion;
                uint clientVersion = localCommunicationVersion;

                if (clientVersion > hostVersion)
                {
                    SendAsync(_protocol.Encode(PacketId.NetworkError, new NetworkErrorMessage { Error = NetworkError.VersionTooHigh }));

                    return;
                }
                else if (clientVersion < hostVersion)
                {
                    SendAsync(_protocol.Encode(PacketId.NetworkError, new NetworkErrorMessage { Error = NetworkError.VersionTooLow }));

                    return;
                }

                NodeInfo myNode = new NodeInfo
                {
                    Ipv4Address               = IpAddress,
                    MacAddress                = MacAddress,
                    NodeId                    = 0, // Will be populated on insert.
                    IsConnected               = 0x01,
                    UserName                  = userConfig.UserName,
                    LocalCommunicationVersion = (ushort)localCommunicationVersion
                };

                bool result = game.Connect(this, myNode);

                if (!result)
                {
                    // There wasn't enough room in the game.

                    SendAsync(_protocol.Encode(PacketId.NetworkError, new NetworkErrorMessage { Error = NetworkError.TooManyPlayers }));
                }
            }
            else
            {
                SendAsync(_protocol.Encode(PacketId.NetworkError, new NetworkErrorMessage { Error = NetworkError.ConnectNotFound }));
            }
        }

        private void HandleConnect(LdnHeader ldnPacket, ConnectRequest request)
        {
            SecurityConfig securityConfig            = request.SecurityConfig;
            UserConfig     userConfig                = request.UserConfig;
            uint           localCommunicationVersion = request.LocalCommunicationVersion;
            uint           optionUnknown             = request.OptionUnknown;
            NetworkInfo    networkInfo               = request.NetworkInfo;

            string id = Convert.ToHexString(networkInfo.NetworkId.SessionId.AsSpan());

            ConnectImpl(id, userConfig, localCommunicationVersion);
        }

        private void HandleConnectPrivate(LdnHeader ldnPacket, ConnectPrivateRequest request)
        {
            SecurityConfig securityConfig = request.SecurityConfig;
            UserConfig userConfig = request.UserConfig;
            uint localCommunicationVersion = request.LocalCommunicationVersion;
            uint optionUnknown = request.OptionUnknown;

            string id = Convert.ToHexString(request.SecurityParameter.SessionId.AsSpan());

            ConnectImpl(id, userConfig, localCommunicationVersion);
        }
    }
}
