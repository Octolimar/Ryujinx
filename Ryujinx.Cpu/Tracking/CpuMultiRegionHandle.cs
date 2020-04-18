using Ryujinx.Memory.Tracking;
using System;

namespace Ryujinx.Cpu.Tracking
{
    public class CpuMultiRegionHandle : IMultiRegionHandle
    {
        private MultiRegionHandle _impl;

        public bool Dirty => _impl.Dirty;

        internal CpuMultiRegionHandle(MultiRegionHandle impl)
        {
            _impl = impl;
        }

        public void Dispose() => _impl.Dispose();
        public void QueryModified(Action<ulong, ulong> modifiedAction) => _impl.QueryModified(modifiedAction);
        public void QueryModified(ulong address, ulong size, Action<ulong, ulong> modifiedAction) => _impl.QueryModified(address, size, modifiedAction);
    }
}
