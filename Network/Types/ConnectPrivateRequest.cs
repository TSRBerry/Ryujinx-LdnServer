using System.Runtime.InteropServices;
using LanPlayServer.LdnServer.Types;

namespace LanPlayServer.Network.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0xBC)]
    struct ConnectPrivateRequest
    {
        public SecurityConfig SecurityConfig;
        public SecurityParameter SecurityParameter;
        public UserConfig UserConfig;
        public uint LocalCommunicationVersion;
        public uint OptionUnknown;
        public NetworkConfig NetworkConfig;
    }
}