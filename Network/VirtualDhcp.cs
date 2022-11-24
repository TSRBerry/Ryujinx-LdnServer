using System.Collections.Generic;
using System.Linq;
using LanPlayServer.LdnServer.Types;

namespace LanPlayServer.Network
{
    /// <summary>
    /// Try to give clients unique virtual IPs that haven't been used before.
    /// </summary>
    internal class VirtualDhcp
    {
        private object _lock = new();

        private uint _nextIp;

        private HashSet<uint> _takenIps = new();
        private HashSet<uint> _reservedIps = new();
        private AddressList _config;

        private uint _baseAddress;
        private uint _subnetMask;
        private uint _invSubnetMask;

        private bool _hasReservedIps;

        public uint BroadcastAddress => _baseAddress | _invSubnetMask;

        public VirtualDhcp(uint baseAddress, uint subnetMask, AddressList dhcpConfig)
        {
            _baseAddress   = baseAddress;
            _subnetMask    = subnetMask;
            _invSubnetMask = ~subnetMask;

            _nextIp = baseAddress + 1;

            PopulateWithList(dhcpConfig);
        }

        private void PopulateWithList(AddressList dhcpConfig)
        {
            _config = dhcpConfig;

            for (int i = 0; i < 8; i++)
            {
                ref AddressEntry address = ref dhcpConfig.Addresses[i];

                if (address.Ipv4Address == 0)
                {
                    break; // End of list.
                }

                _takenIps.Add(address.Ipv4Address);
                _reservedIps.Add(address.Ipv4Address);
                _hasReservedIps = true;
            }
        }

        private uint ReservedIpLookup(byte[] macAddress)
        {
            for (int i = 0; i < 8; i++)
            {
                ref AddressEntry address = ref _config.Addresses[i];

                if (address.Ipv4Address == 0)
                {
                    break; // End of list.
                }

                if (address.MacAddress.SequenceEqual(macAddress))
                {
                    return address.Ipv4Address;
                }
            }

            return 0;
        }

        private bool IsIpValid(uint ip)
        {
            return ip != _baseAddress && ip != (_baseAddress | _invSubnetMask);
        }

        private void CycleNextIp()
        {
            do
            {
                _nextIp = _baseAddress | ((_nextIp + 1) & _invSubnetMask);
            }
            while (!IsIpValid(_nextIp));
        }

        public uint RequestIpV4(byte[] macAddress)
        {
            lock (_lock)
            {
                if (_hasReservedIps)
                {
                    // Is our mac in the reserved IP list?
                    uint reservedIp = ReservedIpLookup(macAddress);

                    if (reservedIp != 0)
                    {
                        return reservedIp;
                    }
                }

                while (_takenIps.Contains(_nextIp))
                {
                    CycleNextIp();
                }

                uint result = _nextIp;
                _takenIps.Add(result);

                CycleNextIp();

                return result;
            }
        }

        public void ReturnIpV4(uint ip)
        {
            lock (_lock)
            {
                if (!_reservedIps.Contains(ip))
                {
                    _takenIps.Remove(ip);
                }
            }
        }
    }
}
