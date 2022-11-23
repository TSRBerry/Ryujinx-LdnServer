using System.Runtime.InteropServices;

namespace LanPlayServer.Network.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x4)]
    internal struct DisconnectMessage
    {
        public uint DisconnectIP;
    }
}
