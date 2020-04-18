namespace Ryujinx.Memory.Range
{
    /// <summary>
    /// Range of memory.
    /// </summary>
    interface IRange
    {
        ulong Address { get; }
        ulong Size    { get; }
        ulong EndAddress { get; }

        bool OverlapsWith(ulong address, ulong size);
    }
}