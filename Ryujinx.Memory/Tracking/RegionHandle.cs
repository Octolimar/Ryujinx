using Ryujinx.Memory.Range;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Ryujinx.Memory.Tracking
{
    public class RegionHandle : IRegionHandle, IRange
    {
        public bool Dirty { get; private set; } = true;
        public MultiRegionHandle Parent { get; internal set; }

        public ulong Address { get; }
        public ulong Size { get; }
        public ulong EndAddress => Address + Size;

        private Action _preAction; // Action to perform before a read or write. This will block the memory access.
        private List<VirtualRegion> _regions;
        private MemoryTracking _tracking;

        internal MemoryPermission RequiredPermission => _preAction != null ? MemoryPermission.None : (Dirty ? MemoryPermission.ReadAndWrite : MemoryPermission.Read);

        internal RegionHandle(MemoryTracking tracking, ulong address, ulong size)
        {
            Address = address;
            Size = size;
            _tracking = tracking;
            _regions = tracking.GetVirtualRegionsForHandle(address, size);
            foreach (var region in _regions)
            {
                region.Handles.Add(this);
            }
        }

        internal void Signal(bool write)
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
            lock (_tracking.TrackingLock)
            {
                foreach (VirtualRegion region in _regions)
                {
                    region.UpdateProtection();
                }
            }
        }

        public void RegisterAction(Action action)
        {
            Action lastAction = Interlocked.Exchange(ref _preAction, action);
            if (lastAction == null && action != lastAction)
            {
                lock (_tracking.TrackingLock)
                {
                    foreach (VirtualRegion region in _regions)
                    {
                        region.UpdateProtection();
                    }
                }
            }
        }

        internal void AddChild(VirtualRegion region)
        {
            _regions.Add(region);
        }

        public bool OverlapsWith(ulong address, ulong size)
        {
            return Address < address + size && address < EndAddress;
        }

        public void Dispose()
        {
            lock (_tracking.TrackingLock)
            {
                foreach (VirtualRegion region in _regions)
                {
                    region.RemoveHandle(this);
                }
            }
        }
    }
}
