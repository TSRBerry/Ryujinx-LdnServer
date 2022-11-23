using System.Runtime.InteropServices;

namespace LanPlayServer.LdnServer.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x20)]
    struct SecurityParameter
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10)]
        public byte[] Data;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10)]
        public byte[] SessionId;
    }
}