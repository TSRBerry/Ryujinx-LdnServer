using System.Runtime.InteropServices;
using LanPlayServer.LdnServer.Types;

namespace LanPlayServer.Network.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0xBC, Pack = 1)]
    struct CreateAccessPointRequest
    {
        public SecurityConfig SecurityConfig;
        public UserConfig     UserConfig;
        public NetworkConfig  NetworkConfig;

        public RyuNetworkConfig RyuNetworkConfig;

        // Advertise data is appended separately. (remaining data in the buffer)
    }
}