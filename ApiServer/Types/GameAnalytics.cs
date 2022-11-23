using System.Collections.Generic;

namespace LanPlayServer.ApiServer.Types
{
    public class GameAnalytics
    {
        public string       Id             { get; set; }
        public int          PlayerCount    { get; set; }
        public int          MaxPlayerCount { get; set; }
        public string       GameName       { get; set; }
        public string       TitleId        { get; set; }
        public string       TitleVersion   { get; set; }
        public string       Mode           { get; set; }
        public string       Status         { get; set; }
        public int          SceneId        { get; set; }
        public List<string> Players        { get; set; }
    }
}