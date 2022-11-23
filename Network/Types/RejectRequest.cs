using System.Runtime.InteropServices;
using LanPlayServer.LdnServer.Types;

namespace LanPlayServer.Network.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x8)]
    internal struct RejectRequest
    {
        public uint NodeId;
        public DisconnectReason DisconnectReason;

        public RejectRequest(DisconnectReason disconnectReason, uint nodeId)
        {
            DisconnectReason = disconnectReason;
            NodeId = nodeId;
        }
    }
}
