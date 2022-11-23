using System.Runtime.InteropServices;
using LanPlayServer.LdnServer.Types;

namespace LanPlayServer.Network.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x4FC, CharSet = CharSet.Ansi)]
    struct ConnectRequest
    {
        public SecurityConfig SecurityConfig;
        public UserConfig     UserConfig;
        public uint           LocalCommunicationVersion;
        public uint           OptionUnknown;
        public NetworkInfo    NetworkInfo;
    }
}
