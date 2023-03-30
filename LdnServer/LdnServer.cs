using NetCoreServer;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LanPlayServer.Network;
using LanPlayServer.Network.Types;

namespace LanPlayServer
{
    class LdnServer : UdpServer
    {
        public const int InactivityPingFrequency = 10000;

        private readonly RyuLdnProtocol _protocol = new();

        private readonly Dictionary<IPEndPoint, LdnSession> Sessions = new();

        public readonly ConcurrentDictionary<string, HostedGame> HostedGames = new();
        public MacAddressMemory MacAddresses { get; } = new();
        public bool UseProxy => true;

        private readonly CancellationTokenSource _cancel = new();

        public LdnServer(IPAddress address, int port) : base(address, port)
        {
            _protocol.Initialize += OnInitializeSession;
            _protocol.Any += (endpoint, header) => Console.WriteLine($"[LdnServer] Received '{(PacketId)header.Type}' packet from: {endpoint}");
            Task.Run(BackgroundPingTask);
        }

        protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
        {
            ReceiveAsync();

            if (Sessions.TryGetValue((IPEndPoint)endpoint, out LdnSession session))
            {
                // Console.WriteLine($"[LdnServer] Session ({endpoint}) received packet.");
                session.OnReceived(buffer, offset, size);
            }
            else
            {
                // Console.WriteLine("[LdnServer] Server received packet.");
                try
                {
                    _protocol.Read((IPEndPoint)endpoint, buffer, (int)offset, (int)size);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[LdnServer] Error decoding packet from {endpoint}: {e}");
                }
            }
        }

        protected override void OnSent(EndPoint endpoint, long sent)
        {
            Console.WriteLine($"[LdnServer] Sent packet of length '{sent}' to {endpoint}");
        }

        private void OnInitializeSession(IPEndPoint endpoint, LdnHeader header, InitializeMessage message)
        {
            Console.WriteLine($"[LdnServer] Creating Session for '{endpoint}'...");
            Sessions.Add(endpoint, new LdnSession(endpoint, this, message));
        }

        internal void DisconnectSession(LdnSession session)
        {
            Sessions.Remove(session.Endpoint);
        }

        public HostedGame CreateGame(string id, NetworkInfo info, AddressList dhcpConfig, string oldOwnerID)
        {
            id = id.ToLower();
            HostedGame game = new HostedGame(id, info, dhcpConfig);
            bool idTaken = false;

            HostedGames.AddOrUpdate(id, game, (_, oldGame) =>
            {
                if (oldGame.OwnerId == oldOwnerID)
                {
                    oldGame.Close();

                    return game;
                }
                else
                {
                    game.Close();
                    idTaken = true;

                    Console.WriteLine($"id Taken: {id}");
                    return oldGame;
                }
            });

            if (idTaken)
            {
                return null;
            }

            return game;
        }

        public HostedGame FindGame(string id)
        {
            id = id.ToLower();

            HostedGames.TryGetValue(id, out HostedGame result);

            return result;
        }

        public KeyValuePair<string, HostedGame>[] All()
        {
            return HostedGames.ToArray();
        }

        public int Scan(ref NetworkInfo[] info, ScanFilter filter, string passphrase, HostedGame exclude)
        {
            KeyValuePair<string, HostedGame>[] all = HostedGames.ToArray();

            int results = 0;

            for (int i = 0; i < all.Length; i++)
            {
                HostedGame game = all[i].Value;

                if (game.TestReadLock())
                {
                    HostedGames.Remove(game.Id, out HostedGame removed);
                    continue;
                }

                if (game.Passphrase != passphrase || game == exclude)
                {
                    continue;
                }

                NetworkInfo scanInfo = game.Info;

                if (scanInfo.Ldn.StationAcceptPolicy == 1)
                {
                    // Optimization: don't tell anyone about unjoinable networks.

                    continue;
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.LocalCommunicationId))
                {
                    if (scanInfo.NetworkId.IntentId.LocalCommunicationId != filter.NetworkId.IntentId.LocalCommunicationId)
                    {
                        continue;
                    }
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.SceneId))
                {
                    if (scanInfo.NetworkId.IntentId.SceneId != filter.NetworkId.IntentId.SceneId)
                    {
                        continue;
                    }
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.SessionId))
                {
                    if (!scanInfo.NetworkId.SessionId.AsSpan().SequenceEqual(filter.NetworkId.SessionId.AsSpan()))
                    {
                        continue;
                    }
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.Ssid))
                {
                    Span<byte> gameSsid = scanInfo.Common.Ssid.Name.AsSpan()[..scanInfo.Common.Ssid.Length];
                    Span<byte> scanSsid = filter.Ssid.Name.AsSpan()[..filter.Ssid.Length];
                    if (!gameSsid.SequenceEqual(scanSsid))
                    {
                        continue;
                    }
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.NetworkType))
                {
                    if (scanInfo.Common.NetworkType != (byte)filter.NetworkType)
                    {
                        continue;
                    }
                }

                if (game.Players == 0)
                {
                    continue;
                }

                // Mac address filter not implemented, since they are currently random.

                if (results >= info.Length)
                {
                    Array.Resize(ref info, info.Length + 1);
                }

                info[results++] = scanInfo;
            }

            return results;
        }

        public void CloseGame(string id)
        {
            HostedGames.Remove(id.ToLower(), out HostedGame removed);
            removed?.Close();
        }

        // protected override TcpSession CreateSession()
        // {
        //     return new LdnSession(this);
        // }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"LDN UDP server caught an error with code {error}");
        }

        protected override void OnStarted()
        {
            ReceiveAsync();
        }

        public override bool Stop()
        {
            _cancel.Cancel();

            return base.Stop();
        }

        private async Task BackgroundPingTask()
        {
            while (!IsDisposed)
            {
                foreach (KeyValuePair<IPEndPoint, LdnSession> session in Sessions)
                {
                    session.Value.Ping();
                }

                try
                {
                    await Task.Delay(InactivityPingFrequency, _cancel.Token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }
    }
}
