namespace LanPlayServer.ApiServer.Types
{
    public class LdnAnalytics
    {
        public int TotalGamesCount     { get; set; }
        public int PrivateGamesCount   { get; set; }
        public int PublicGamesCount    { get; set; }
        public int InProgressCount     { get; set; }
        public int MasterProxyCount    { get; set; }
        public int TotalPlayersCount   { get; set; }
        public int PrivatePlayersCount { get; set; }
        public int PublicPlayersCount  { get; set; }
    }
}