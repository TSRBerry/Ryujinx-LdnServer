using System.Runtime.InteropServices;

namespace LanPlayServer.LdnServer.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 8)]
    internal struct NodeLatestUpdate
    {
        public byte   State;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
        public byte[] Reserved;
    }
}
