using ARMeilleure.Memory.Range;

namespace ARMeilleure.Memory.Tracking
{
    abstract class AbstractRegion : IRange
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
    }
}
