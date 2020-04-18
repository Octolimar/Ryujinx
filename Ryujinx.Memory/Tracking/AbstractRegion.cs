using Ryujinx.Memory.Range;

namespace Ryujinx.Memory.Tracking
{
    abstract class AbstractRegion : INonOverlappingRange
    {
        public ulong Address { get; }
        public ulong Size { get; protected set; }
        public ulong EndAddress => Address + Size;

        protected AbstractRegion(ulong address, ulong size)
        {
            Address = address;
            Size = size;
        }

        public bool OverlapsWith(ulong address, ulong size)
        {
            return Address < address + size && address < EndAddress;
        }

        public abstract INonOverlappingRange Split(ulong splitAddress);
    }
}
