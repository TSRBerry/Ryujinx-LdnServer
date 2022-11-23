using System.Runtime.InteropServices;

namespace LanPlayServer.LdnServer.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x10)]
    internal struct IntentId
    {
        public ulong  LocalCommunicationId;
        public ushort Reserved1;
        public ushort SceneId;
        public uint   Reserved2;
    }
}