namespace Ryujinx.Memory.Range
{
    interface INonOverlappingRange : IRange
    {
        public INonOverlappingRange Split(ulong splitAddress);
    }
}
