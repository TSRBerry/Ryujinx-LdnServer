using System.Runtime.InteropServices;

namespace LanPlayServer.LdnServer.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x60)]
    internal struct ScanFilter
    {
        public NetworkId      NetworkId;
        public NetworkType    NetworkType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[]         MacAddress;
        public Ssid           Ssid;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10)]
        public byte[]         Reserved;
        public ScanFilterFlag Flag;
    }
}