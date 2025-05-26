using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XI5.Types
{
    public readonly struct TicketVersion
    {
        public byte Major { get; }
        public byte Minor { get; }

        internal TicketVersion(byte major, byte minor)
        {
            Major = major;
            Minor = minor;
        }

        public override string ToString()
        {
            return Major + "." + Minor;
        }
    }
}
