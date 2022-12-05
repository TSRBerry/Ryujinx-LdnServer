using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LanPlayServer.LdnServer.Types;
using LanPlayServer.Network;
using LanPlayServer.Network.Types;
using NetCoreServer;

namespace LanPlayServer.LdnServer
{
    internal class LdnServer : UdpServer
    {
        public const int InactivityPingFrequency = 10000;

        private RyuLdnProtocol _protocol;
        private Dictionary<EndPoint, LdnSession> _sessions = new();

        public ConcurrentDictionary<string, HostedGame> HostedGames = new();
        public MacAddressMemory MacAddresses { get; } = new();
        public bool UseProxy => true;

        private CancellationTokenSource _cancel = new();

        public LdnServer(IPAddress address, int port) : base(address, port)
        {
            _protocol = new RyuLdnProtocol();

            _protocol.Initialize += OnSessionInitialize;

            Task.Run(BackgroundPingTask);
        }

        private void OnSessionInitialize(LdnHeader header, InitializeMessage msg, EndPoint endPoint)
        {
            _sessions.Add(endPoint, new LdnSession(this, endPoint, msg));
        }

        protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
        {
            if (_sessions.ContainsKey(endpoint))
            {
                Console.WriteLine($"LdnServer> Redirecting received message from {endpoint}");
                _sessions[endpoint].OnReceived(buffer, offset, size);
            }
            else
            {
                Console.WriteLine($"LdnServer> Message received from {endpoint}");
                _protocol.Read(buffer, offset, size, endpoint);
            }
        }

        public HostedGame CreateGame(string id, NetworkInfo info, AddressList dhcpConfig, string oldOwnerID)
        {
            HostedGame game = new HostedGame(id, info, dhcpConfig);
            bool idTaken = false;

            HostedGames.AddOrUpdate(id, game, (id, oldGame) =>
            {
                if (oldGame.OwnerId == oldOwnerID)
                {
                    oldGame.Close();

                    Console.WriteLine($"NEW GAME: {id}");
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
                    if (!scanInfo.NetworkId.SessionId.SequenceEqual(filter.NetworkId.SessionId))
                    {
                        continue;
                    }
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.Ssid))
                {
                    IEnumerable<byte> gameSsid = scanInfo.Common.Ssid.Name.Take(scanInfo.Common.Ssid.Length);
                    IEnumerable<byte> scanSsid = filter.Ssid.Name.Take(filter.Ssid.Length);
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
            HostedGames.Remove(id, out HostedGame removed);
            removed?.Close();
        }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"LDN TCP server caught an error with code {error}");
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
                foreach (var session in _sessions)
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
