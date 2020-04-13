using System;
using System.Threading;

namespace ARMeilleure.Memory.Tracking
{
    public class RegionHandle : IDisposable
    {
        public bool Dirty { get; private set; } = true;
        public MultiRegionHandle Parent { get; internal set; }

        public ulong Address => _region.Address;
        public ulong Size => _region.Size;
        public ulong EndAddress => _region.EndAddress;

        private Action _preAction; // Action to perform before a read or write. This will block the memory access.
        private VirtualRegion _region;

        public MemoryProtection RequiredPermission => _preAction != null ? MemoryProtection.None : (Dirty ? MemoryProtection.ReadAndWrite : MemoryProtection.Read);

        internal RegionHandle(VirtualRegion region)
        {
            _region = region;
        }

        public void Signal(bool write)
        {
            Action action = Interlocked.Exchange(ref _preAction, null);
            action?.Invoke();

            if (write)
            {
                Dirty = true;
                Parent?.SignalWrite();
            }
        }

        public void Reprotect()
        {
            Dirty = false;
            _region.UpdateProtection();
        }

        public void RegisterAction(Action action)
        {
            Action lastAction = Interlocked.Exchange(ref _preAction, action);
            if (lastAction == null)
            {
                _region.UpdateProtection();
            }
        }

        public void Dispose()
        {
            _region.RemoveHandle(this);
        }
    }
}
