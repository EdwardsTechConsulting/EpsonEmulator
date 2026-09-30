using System;
using System.Net;

namespace EpsonEmulator.Core
{
    public static class BindAddressParser
    {
        public static IPAddress Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new FormatException("Bind address is required. Use a local IP address, 0.0.0.0, ::, or all.");
            }

            var trimmed = value.Trim();
            if (trimmed.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("*", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                return IPAddress.Any;
            }

            if (trimmed.Equals("all-ipv6", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("any-ipv6", StringComparison.OrdinalIgnoreCase))
            {
                return IPAddress.IPv6Any;
            }

            if (!IPAddress.TryParse(trimmed, out var address))
            {
                throw new FormatException("Bind address must be a valid IPv4/IPv6 local address, 0.0.0.0, ::, or all.");
            }

            return address;
        }

        public static string Display(IPAddress address)
        {
            if (Equals(address, IPAddress.Any)) return "all IPv4 interfaces (0.0.0.0)";
            if (Equals(address, IPAddress.IPv6Any)) return "all IPv6 interfaces (::)";
            return address.ToString();
        }
    }
}
