using System.Runtime.InteropServices;

namespace LanPlayServer.LdnServer.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x22)]
    internal struct Ssid
    {
        public byte Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x21)]
        public byte[] Name;
    }
}