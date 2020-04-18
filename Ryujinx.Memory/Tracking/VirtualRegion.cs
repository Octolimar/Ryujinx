using Ryujinx.Memory.Range;
using System.Collections.Generic;

namespace Ryujinx.Memory.Tracking
{
    class VirtualRegion : AbstractRegion
    {
        public List<RegionHandle> Handles = new List<RegionHandle>();
        public List<PhysicalRegion> PhysicalChildren;

        public MemoryTracking Tracking;

        public VirtualRegion(MemoryTracking tracking, ulong address, ulong size) : base(address, size)
        {
            Tracking = tracking;

            UpdatePhysicalChildren();
        }

        public void ClearPhysicalChildren()
        {
            if (PhysicalChildren != null)
            {
                foreach (PhysicalRegion child in PhysicalChildren)
                {
                    child.RemoveParent(this);
                }
            }
        }

        public void UpdatePhysicalChildren()
        {
            PhysicalChildren = Tracking.GetPhysicalRegionsForVirtual(Address, Size);

            foreach (PhysicalRegion child in PhysicalChildren)
            {
                child.VirtualParents.Add(this);
            }
        }

        public void RecalculatePhysicalChildren()
        {
            ClearPhysicalChildren();
            UpdatePhysicalChildren();
        }

        public void Signal(bool write)
        {
            // Assumes the tracking lock has already been obtained.

            Tracking.ProtectVirtualRegion(this, MemoryPermission.ReadAndWrite); // Remove our protection immedately.
            foreach (var handle in Handles)
            {
                handle.Signal(write);
            }
        }

        public MemoryPermission GetRequiredPermission()
        {
            // Start with Read/Write, each handle can strip off permissions as necessary.
            // Assumes the tracking lock has already been obtained.

            MemoryPermission result = MemoryPermission.ReadAndWrite;

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

        public void AddChild(PhysicalRegion region)
        {
            PhysicalChildren.Add(region);
        }

        public override INonOverlappingRange Split(ulong splitAddress)
        {
            ClearPhysicalChildren();
            VirtualRegion newRegion = new VirtualRegion(Tracking, splitAddress, EndAddress - splitAddress);
            Size = splitAddress - Address;
            UpdatePhysicalChildren();

            // The new region inherits all of our parents.
            newRegion.Handles = new List<RegionHandle>(Handles);
            foreach (var parent in Handles)
            {
                parent.AddChild(newRegion);
            }

            return newRegion;
        }
    }
}
