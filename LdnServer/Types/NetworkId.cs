using System.Runtime.InteropServices;

namespace LanPlayServer.LdnServer.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x20)]
    internal struct NetworkId
    {
        public IntentId IntentId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10)]
        public byte[]   SessionId;
    }
}