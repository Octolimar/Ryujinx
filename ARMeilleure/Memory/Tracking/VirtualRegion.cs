using System.Collections.Generic;

namespace ARMeilleure.Memory.Tracking
{
    class VirtualRegion : AbstractRegion
    {
        public List<RegionHandle> Handles = new List<RegionHandle>();
        public List<PhysicalRegion> PhysicalChildren;

        public MemoryTracking Tracking;

        public VirtualRegion(MemoryTracking tracking, ulong address, ulong size) : base(address, size)
        {
            Tracking = tracking;

            PhysicalChildren = tracking.GetPhysicalRegionsForVirtual(address, size);

            foreach (PhysicalRegion child in PhysicalChildren)
            {
                child.VirtualParents.Add(this);
            }
        }

        public void Signal(bool write)
        {
            // Assumes the tracking lock has already been obtained.

            Tracking.ProtectVirtualRegion(this, MemoryProtection.ReadAndWrite); // Remove our protection immedately.
            foreach (var handle in Handles)
            {
                handle.Signal(write);
            }
        }

        public MemoryProtection GetRequiredPermission()
        {
            // Start with Read/Write, each handle can strip off permissions as necessary.
            // Assumes the tracking lock has already been obtained.

            MemoryProtection result = MemoryProtection.ReadAndWrite;

            foreach (var handle in Handles)
            {
                result &= handle.RequiredPermission;
                if (result == 0) return result;
            }
            return result;
        }

        public void UpdateProtection()
        {
            // Re-evaluate protection for all physical children.

            Tracking.ProtectVirtualRegion(this, GetRequiredPermission());
            lock (Tracking.TrackingLock)
            {
                foreach (var child in PhysicalChildren)
                {
                    child.UpdateProtection();
                }
            }
        }

        public RegionHandle NewHandle()
        {
            // Assumes the tracking lock has already been obtained.
            // Handles start as dirty and with no action, so protection does not need to be updated.

            RegionHandle handle = new RegionHandle(this);
            Handles.Add(handle);
            return handle;
        }

        public void RemoveHandle(RegionHandle handle)
        {
            bool removedRegions = false;
            lock (Tracking.TrackingLock)
            {
                Handles.Remove(handle);
                if (Handles.Count == 0)
                {
                    Tracking.RemoveVirtual(this);
                    foreach (var child in PhysicalChildren)
                    {
                        removedRegions |= child.RemoveParent(this);
                    }
                }
            }

            if (removedRegions)
            {
                // The first lock will unprotect any regions that have been removed. This second lock will remove them.
                lock (Tracking.TrackingLock)
                {
                    foreach (var child in PhysicalChildren)
                    {
                        child.TryDelete();
                    }
                }
            }
        }
    }
}
