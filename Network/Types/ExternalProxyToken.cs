using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LanPlayServer.Network.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x28)]
    internal struct ExternalProxyToken
    {
        public uint VirtualIp;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10)]
        public byte[] Token;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10)]
        public byte[] PhysicalIp;
        public AddressFamily AddressFamily;
    }
}
