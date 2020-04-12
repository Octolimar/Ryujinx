using System.Collections.Generic;

namespace ARMeilleure.Memory.Tracking
{
    class PhysicalRegion : AbstractRegion
    {
        public List<VirtualRegion> VirtualParents = new List<VirtualRegion>();
        public MemoryTracking Tracking;

        public PhysicalRegion(MemoryTracking tracking, ulong address, ulong size) : base(address, size)
        {
            Tracking = tracking;
        }

        public void Signal(bool write)
        {
            // Assumes the tracking lock has already been obtained.

            foreach (var parent in VirtualParents)
            {
                parent.Signal(write);
            }
            UpdateProtection();
        }

        public void UpdateProtection()
        {
            // Re-evaluate protection, and commit to the block.

            lock (Tracking.TrackingLock)
            {
                MemoryProtection result = MemoryProtection.ReadAndWrite;
                foreach (var parent in VirtualParents)
                {
                    result &= parent.GetRequiredPermission();
                    if (result == 0) break;
                }

                Tracking.ProtectRegion(this, result);
            }
        }

        public PhysicalRegion Split(ulong splitAddress)
        {
            PhysicalRegion newRegion = new PhysicalRegion(Tracking, splitAddress, EndAddress - splitAddress);
            Size = splitAddress - Address;

            // The new region inherits all of our parents.
            newRegion.VirtualParents = new List<VirtualRegion>(VirtualParents);

            return newRegion;
        }

        public void RemoveParent(VirtualRegion region)
        {
            VirtualParents.Remove(region);
            if (VirtualParents.Count == 0)
            {
                Tracking.RemovePhysical(this);
            }
        }
    }
}
