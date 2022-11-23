using System.Runtime.InteropServices;

namespace LanPlayServer.Network.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x80)]
    internal struct PassphraseMessage
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x80)]
        public byte[] Passphrase;
    }
}
