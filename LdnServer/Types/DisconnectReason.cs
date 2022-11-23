namespace LanPlayServer.LdnServer.Types
{
    enum DisconnectReason
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