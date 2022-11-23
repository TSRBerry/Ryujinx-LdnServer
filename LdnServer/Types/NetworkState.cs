namespace LanPlayServer.LdnServer.Types
{
    internal enum NetworkState
    {
        None,
        Initialized,
        AccessPoint,
        AccessPointCreated,
        Station,
        StationConnected,
        Error
    }
}