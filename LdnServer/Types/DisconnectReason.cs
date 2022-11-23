namespace LanPlayServer.LdnServer.Types
{
    internal enum DisconnectReason
    {
        None,
        DisconnectedByUser,
        DisconnectedBySystem,
        DestroyedByUser,
        DestroyedBySystem,
        Rejected,
        SignalLost
    }
}