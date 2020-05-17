using System;

namespace Ryujinx.Memory.Tracking
{
    public interface IMultiRegionHandle : IDisposable
    {
        public bool Dirty { get; }

        public void QueryModified(Action<ulong, ulong> modifiedAction);
        public void QueryModified(ulong address, ulong size, Action<ulong, ulong> modifiedAction);
        public void InitMinimumGranularity(ulong granularity);
    }
}
