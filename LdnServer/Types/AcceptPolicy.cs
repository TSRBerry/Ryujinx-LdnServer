namespace LanPlayServer.LdnServer.Types
{
    internal enum AcceptPolicy : byte
    {
        AcceptAll,
        RejectAll,
        BlackList,
        WhiteList
    }
}
