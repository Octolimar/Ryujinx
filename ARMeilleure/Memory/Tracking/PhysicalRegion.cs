using System.Collections.Generic;

namespace ARMeilleure.Memory.Tracking
{
    class PhysicalRegion : AbstractRegion
    {
        public List<VirtualRegion> VirtualParents = new List<VirtualRegion>();
        public MemoryProtection Protection { get; private set; }
        public MemoryTracking Tracking;

        public PhysicalRegion(MemoryTracking tracking, ulong address, ulong size) : base(address, size)
        {
            Tracking = tracking;
            Protection = MemoryProtection.ReadAndWrite;
        }

        public void Signal(bool write)
        {
            // Assumes the tracking lock has already been obtained.

            Protection = MemoryProtection.ReadAndWrite;
            Tracking.ProtectPhysicalRegion(this, MemoryProtection.ReadAndWrite); // Remove our protection immedately.
            foreach (var parent in VirtualParents)
            {
                parent.Signal(write);
            }
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

                if (Protection != result)
                {
                    Protection = result;
                    Tracking.ProtectPhysicalRegion(this, result);
                }
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

        public bool RemoveParent(VirtualRegion region)
        {
            VirtualParents.Remove(region);
            UpdateProtection();
            if (VirtualParents.Count == 0)
            {
                return true;
            }
            return false;
        }

        public void TryDelete()
        {
            if (VirtualParents.Count == 0)
            {
                Tracking.RemovePhysical(this);
            }
        }
    }
}
