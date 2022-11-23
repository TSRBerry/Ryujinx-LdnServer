using System.Runtime.InteropServices;

namespace LanPlayServer.Network.Types
{
    internal enum NetworkError : int
    {
        None,

        PortUnreachable,

        TooManyPlayers,
        VersionTooLow,
        VersionTooHigh,

        ConnectFailure,
        ConnectNotFound,
        ConnectTimeout,
        ConnectRejected,

        RejectFailed,

        Unknown = -1
    }

    [StructLayout(LayoutKind.Sequential, Size = 0x4)]
    internal struct NetworkErrorMessage
    {
        public NetworkError Error;
    }
}