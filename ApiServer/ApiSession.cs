using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using LanPlayServer.ApiServer.Types;
using LanPlayServer.LdnServer;
using LanPlayServer.LdnServer.Types;
using LanPlayServer.Utils;
using NetCoreServer;

namespace LanPlayServer.ApiServer
{
    class ApiSession : HttpSession
    {
        readonly LdnServer.LdnServer _ldnServer;

        public ApiSession(HttpServer server, LdnServer.LdnServer ldnServer) : base(server)
        {
            _ldnServer = ldnServer;

            RateLimiter.SetRateLimit(100, 60000); // 100 requests/min
        }

        protected override void OnReceivedRequest(HttpRequest request)
        {
            HttpResponse httpResponse = new HttpResponse();
            httpResponse.Clear();

            IPAddress address;
            try
            {
                address = ((IPEndPoint)Socket.RemoteEndPoint).Address;
            }
            catch (Exception)
            {
                return;
            }

            if (!RateLimiter.IsRateLimited(address))
            {
                string bodyResponse = "";
                string urlEndpoint  = Uri.UnescapeDataString(request.Url);

                if (request.Method == "GET")
                {
                    if (urlEndpoint == ApiEndpoints.Api || urlEndpoint == ApiEndpoints.Games)
                    {
                        bodyResponse = List(urlEndpoint);
                    }
                    else
                    {
                        urlEndpoint = urlEndpoint.Replace(ApiEndpoints.Games + "?titleid=", "", StringComparison.InvariantCultureIgnoreCase);

                        bool isTitleId = Regex.IsMatch(urlEndpoint, "^[a-zA-Z0-9/._-]{16}$");
                        if (isTitleId)
                        {
                            bodyResponse = List(ApiEndpoints.Games, urlEndpoint);
                        }
                    }
                }

                if (bodyResponse != "")
                {
                    httpResponse.SetBegin(200);
                    httpResponse.SetContentType(".json");
                    httpResponse.SetHeader("Access-Control-Allow-Origin", "*");
                    httpResponse.SetBody(bodyResponse);
                }
                else
                {
                    List<string> fileList = Directory.EnumerateFiles("www", "*", SearchOption.AllDirectories).Select(f => f.Substring(3).Replace("\\", "/")).ToList();
                    fileList.Add("/");

                    if (fileList.Contains(urlEndpoint))
                    {
                        httpResponse.SetBegin(200);
                        httpResponse.SetHeader("Cache-Control", $"max-age={TimeSpan.FromHours(1).Seconds}");

                        if ((urlEndpoint == "/") || (urlEndpoint == "/index.html"))
                        {
                            httpResponse.SetContentType(".html");
                            httpResponse.SetBody(File.ReadAllText($"www/index.html"));
                        }
                        else if (Path.GetExtension(urlEndpoint) == ".png")
                        {
                            httpResponse.SetContentType(Path.GetExtension(urlEndpoint));
                            httpResponse.SetBody(File.ReadAllBytes($"www{urlEndpoint}"));
                        }
                        else
                        {
                            httpResponse.SetContentType(Path.GetExtension(urlEndpoint));
                            httpResponse.SetBody(File.ReadAllText($"www{urlEndpoint}"));
                        }
                    }
                    else
                    {
                        httpResponse.SetBegin(404);
                        httpResponse.SetBody("");
                    }
                }
            }
            else
            {
                httpResponse.SetBegin(429);
                httpResponse.SetBody("");
            }

            SendResponseAsync(httpResponse);
        }

        private string List(string endpoint, string gameTitleId = "")
        {
            // List all hosted games.
            KeyValuePair<string, HostedGame>[] games = _ldnServer.All();

            // List all private hosted games.
            Dictionary<string, List<HostedGame>> gamesByPassphrase = new Dictionary<string, List<HostedGame>>();

            foreach (KeyValuePair<string, HostedGame> game in games)
            {
                if (game.Value.TestReadLock())
                {
                    continue;
                }

                string passphrase = game.Value.Passphrase ?? "";

                if (!gamesByPassphrase.TryGetValue(passphrase, out List<HostedGame> target))
                {
                    target = new List<HostedGame>();

                    gamesByPassphrase.Add(passphrase, target);
                }

                target.Add(game.Value);
            }

            int totalGameCount     = 0;
            int totalPlayerCount   = 0;
            int privateGameCount   = 0;
            int privatePlayerCount = 0;
            int masterProxyCount   = 0;
            int inProgressCount    = 0;

            List<GameAnalytics> gamesAnalytics = new List<GameAnalytics>();

            foreach (KeyValuePair<string, List<HostedGame>> group in gamesByPassphrase)
            {
                bool isGamePublic = group.Key == "";

                foreach (HostedGame game in group.Value)
                {
                    GameAnalytics gameAnalytics = new GameAnalytics();

                    string      id          = game.Id;
                    NetworkInfo info        = game.Info;
                    string      gameVersion = game.GameVersion;
                    ulong       titleId     = info.NetworkId.IntentId.LocalCommunicationId;
                    int         sceneId     = info.NetworkId.IntentId.SceneId;
                    string      gameName    = GameList.GetGameById(titleId)?.Name ?? "Unknown";
                    string      titleString = titleId.ToString("x16");

                    if (info.Ldn.NodeCount == 0)
                    {
                        continue;
                    }

                    gameAnalytics.Mode = game.IsP2P ? "P2P" : "Master Server Proxy";
                    if (!game.IsP2P)
                    {
                        masterProxyCount++;
                    }

                    gameAnalytics.Status = "Joinable";

                    totalGameCount++;

                    if (!isGamePublic)
                    {
                        privateGameCount++;
                        privatePlayerCount += info.Ldn.NodeCount;
                    }

                    if (info.Ldn.StationAcceptPolicy == 1)
                    {
                        inProgressCount++;
                        gameAnalytics.Status = "Not Joinable";
                    }

                    gameAnalytics.Id             = id;
                    gameAnalytics.PlayerCount    = info.Ldn.NodeCount;
                    gameAnalytics.MaxPlayerCount = info.Ldn.NodeCountMax;
                    gameAnalytics.GameName       = gameName;
                    gameAnalytics.TitleId        = titleString;
                    gameAnalytics.SceneId        = sceneId;
                    gameAnalytics.TitleVersion   = gameVersion;

                    totalPlayerCount += info.Ldn.NodeCount;

                    gameAnalytics.Players = new List<string>();

                    for (int i = 0; i < info.Ldn.NodeCount; i++)
                    {
                        NodeInfo player = info.Ldn.Nodes[i];
                        string   name   = StringUtils.ReadUtf8String(player.UserName);

                        // Would like add more players informations here, but needs a bit more work.
                        gameAnalytics.Players.Add(name);
                    }

                    if (isGamePublic)
                    {
                        gamesAnalytics.Add(gameAnalytics);
                    }
                }
            }

            if (endpoint == ApiEndpoints.Api)
            {
                LdnAnalytics ldnAnalytics = new LdnAnalytics()
                {
                    TotalGamesCount     = totalGameCount,
                    PrivateGamesCount   = privateGameCount,
                    PublicGamesCount    = totalGameCount - privateGameCount,
                    InProgressCount     = inProgressCount,
                    MasterProxyCount    = masterProxyCount,
                    TotalPlayersCount   = totalPlayerCount,
                    PrivatePlayersCount = privatePlayerCount,
                    PublicPlayersCount  = totalPlayerCount - privatePlayerCount
                };

                return JsonSerializerHelper.Serialize(ldnAnalytics);
            }
            else if (endpoint == ApiEndpoints.Games)
            {
                // return all games
                if (gameTitleId == "")
                {
                    return JsonSerializerHelper.Serialize(gamesAnalytics);
                }
                else
                {
                    return JsonSerializerHelper.Serialize(gamesAnalytics.Where(game => game.TitleId.ToLower() == gameTitleId.ToLower()).ToList());
                }
            }
            else
            {
                return "";
            }
        }

        protected override void OnReceivedRequestError(HttpRequest request, string error)
        {
            Console.WriteLine($"Request error: {error}");
        }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"HTTP session caught an error: {error}");
        }
    }
}