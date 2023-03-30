using Ryujinx.HLE.HOS.Services.Ldn.Types;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace LanPlayServer
{
    class Program
    {
        static void Main(string[] args)
        {
            int portLdn = 30456;
            int portApi = 8080;

            Console.WriteLine();
            Console.WriteLine( "__________                     __ .__                  .____         .___        ");
            Console.WriteLine(@"\______   \ ___.__. __ __     |__||__|  ____  ___  ___ |    |      __| _/  ____  ");
            Console.WriteLine(@" |       _/<   |  ||  |  \    |  ||  | /    \ \  \/  / |    |     / __ |  /    \ ");
            Console.WriteLine(@" |    |   \ \___  ||  |  /    |  ||  ||   |  \ >    <  |    |___ / /_/ | |   |  \");
            Console.WriteLine(@" |____|_  / / ____||____/ /\__|  ||__||___|  //__/\_ \ |_______ \\____ | |___|  /");
            Console.WriteLine(@"        \/  \/            \______|         \/       \/         \/     \/      \/ ");
            Console.WriteLine();
            Console.WriteLine( "_________________________________________________________________________________");
            Console.WriteLine();
            Console.WriteLine("- Information");

            LdnServer ldnServer = new LdnServer(IPAddress.Any, portLdn);
            ApiServer apiServer = new ApiServer(IPAddress.Any, portApi, ldnServer);

            Console.Write($"    LdnServer (port: {portLdn}/udp) starting...");
            ldnServer.Start();
            Console.WriteLine(" Done!");

            Console.Write($"    ApiServer (port: {portApi}) starting...");
            apiServer.Start();
            Console.WriteLine(" Done!");
#if DISABLE_CLI
            Console.WriteLine("CLI disabled (docker mode)");
            Console.WriteLine("----- UDP TEST -----");

            for (;;)
            {
                Console.WriteLine("Hourly LDN server analytics:");
                List(ldnServer);

                Thread.Sleep(3600 * 1000);
            }

#else
            Console.WriteLine();
            Console.WriteLine("- Commands");
            Console.WriteLine("    !restart-ldn -> Restart the LDN server.");
            Console.WriteLine("    !restart-api -> Restart the API server.");
            Console.WriteLine("    !close       -> Close all servers.");
            Console.WriteLine("    !list        -> Get LDN server analytics.");
            Console.WriteLine("_________________________________________________________________________________");
            Console.WriteLine();
            Console.WriteLine("Type a command:");

            for (;;)
            {
                string line = Console.ReadLine();

                if (line == "!close")
                {
                    break;
                }

                bool commandValid = line switch {
                    "!restart-ldn" => RestartLdnServer(ldnServer),
                    "!restart-api" => RestartApiServer(apiServer),
                    "!list"        => List(ldnServer),
                    _ => false
                };

                if (!commandValid)
                {
                    Console.WriteLine("Invalid command.");
                    Console.WriteLine();
                    Console.WriteLine("Type a command:");
                }
            }

            Console.Write("LdnServer stopping...");
            ldnServer.Stop();
            Console.WriteLine(" Done!");

            Console.Write("ApiServer stopping...");
            apiServer.Stop();
            Console.WriteLine(" Done!");
#endif
        }

        static bool RestartLdnServer(LdnServer ldnServer)
        {
            Console.Write("    !restart-ldn: LDN Server restarting...");
            ldnServer.Restart();
            Console.WriteLine("Done!");

            return true;
        }

        static bool RestartApiServer(ApiServer apiServer)
        {
            Console.Write("    !restart-api: API Server restarting...");
            apiServer.Restart();
            Console.WriteLine("Done!");

            return true;
        }

        // TODO: Maybe handle that in the API with a password or something ?
        static bool List(LdnServer server)
        {
            KeyValuePair<string, HostedGame>[] games = server.All();

            Dictionary<string, List<HostedGame>> gamesByPassphrase = new Dictionary<string, List<HostedGame>>();

            foreach (KeyValuePair<string, HostedGame> game in games)
            {
                if (game.Value.TestReadLock())
                {
                    continue;
                }

                string passphrase = game.Value.Passphrase ?? "";

                List<HostedGame> target;
                if (!gamesByPassphrase.TryGetValue(passphrase, out target))
                {
                    target = new List<HostedGame>();

                    gamesByPassphrase.Add(passphrase, target);
                }

                target.Add(game.Value);
            }

            int gameCount = 0;
            int playerCount = 0;
            int privateGameCount = 0;
            int privatePlayerCount = 0;
            int masterProxyCount = 0;
            int inProgressCount = 0;

            Console.WriteLine("   !list:");

            foreach (KeyValuePair<string, List<HostedGame>> group in gamesByPassphrase)
            {
                bool publicGroup = group.Key == "";
                Console.WriteLine(publicGroup ? "== PUBLIC ==" : $"== Passphrase: {group.Key} ==");

                foreach (HostedGame game in group.Value)
                {
                    string id = game.Id;
                    NetworkInfo info = game.Info;

                    ulong titleId = (ulong)info.NetworkId.IntentId.LocalCommunicationId;
                    string gameName = GameList.GetGameById(titleId)?.Name ?? "Unknown";
                    string titleString = titleId.ToString("x16");

                    string mode = game.IsP2P ? "P2P" : "Master Server Proxy";
                    if (!game.IsP2P)
                    {
                        masterProxyCount++;
                    }

                    string status = "";

                    gameCount++;

                    if (!publicGroup)
                    {
                        privateGameCount++;
                        privatePlayerCount += info.Ldn.NodeCount;
                    }

                    if (info.Ldn.StationAcceptPolicy == 1)
                    {
                        inProgressCount++;
                        status = ", Not Joinable";
                    }

                    Console.WriteLine($" {id} ({info.Ldn.NodeCount}/{info.Ldn.NodeCountMax}): {gameName} ({titleString}) - {mode}{status}");

                    playerCount += info.Ldn.NodeCount;

                    for (int i = 0; i < info.Ldn.NodeCount; i++)
                    {
                        NodeInfo player = info.Ldn.Nodes[i];
                        string name = StringUtils.ReadUtf8String(player.UserName.AsSpan());

                        // Would like to print IP here, but needs a bit more work.
                        Console.WriteLine($"  - {name}");
                    }

                    Console.WriteLine();
                }
            }

            Console.WriteLine("==========");
            Console.WriteLine("Game Summary:");
            Console.WriteLine($"{gameCount} total games. ({privateGameCount} private, {gameCount - privateGameCount} public)");
            Console.WriteLine($" {inProgressCount} games in progress (not joinable).");
            Console.WriteLine($" {masterProxyCount} games using the master server as a proxy rather than P2P.");
            Console.WriteLine("Player Summary:");
            Console.WriteLine($"{playerCount} total players. ({privatePlayerCount} in private games, {playerCount - privatePlayerCount} in public)");

            return true;
        }
    }
}
