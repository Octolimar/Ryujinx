namespace ARMeilleure.Memory.Range
{
    /// <summary>
    /// Range of memory.
    /// </summary>
    public interface IRange
    {
        ulong Address { get; }
        ulong Size    { get; }

        bool OverlapsWith(ulong address, ulong size);
    }
}