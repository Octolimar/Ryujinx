using ARMeilleure.Memory;
using ARMeilleure.Memory.Range;
using System;
using System.Collections.Generic;

namespace ARMeilleure.Memory.Tracking
{
    public class MemoryTracking
    {
        private RangeList<VirtualRegion> _virtualRegions;
        private RangeList<PhysicalRegion> _physicalRegions;

        public Func<ulong, ulong, (ulong address, ulong size)[]> VirtualToPhysicalConverter;
        public Action<ulong, ulong, MemoryProtection> VirtualProtect;

        private IMemoryBlock _block;
        internal object TrackingLock = new object();

        // Only use these from within the lock.
        private VirtualRegion[] _virtualResults = new VirtualRegion[10];
        private PhysicalRegion[] _physicalResults = new PhysicalRegion[10];

        public MemoryTracking(IMemoryBlock block)
        {
            _block = block;
            _virtualRegions = new RangeList<VirtualRegion>();
            _physicalRegions = new RangeList<PhysicalRegion>();

            _block.RegisterTrackingAction(PhysicalMemoryEvent);
        }

        public void Map(ulong va, ulong pa, ulong size)
        {
            // A mapping may mean we need to re-evaluate each VirtualRegion's affected area.
            // Find all virtual regions that overlap with the range, we need to recalculate their physical regions

            // TODO: this
        }

        public void Unmap(ulong va, ulong size)
        {
            // An unmapping may mean we need to re-evaluate each VirtualRegion's affected area.
            // Find all virtual regions that overlap with the range, we need to recalculate their physical regions

            // TODO: this
        }

        internal List<PhysicalRegion> GetPhysicalRegionsForVirtual(ulong va, ulong size)
        {
            List<PhysicalRegion> result = new List<PhysicalRegion>();

            // Get a list of physical regions for this virtual region, from our injected virtual mapping function.
            (ulong Address, ulong Size)[] physicalRegions = VirtualToPhysicalConverter(va, size);

            foreach (var region in physicalRegions)
            {
                AddPhysicalRegionsForPhysical(result, region.Address, region.Size);
            }

            return result;
        }

        private void AddPhysicalRegionsForPhysical(List<PhysicalRegion> list, ulong pa, ulong size)
        {
            // A physical region may be split into multiple parts if multiple virtual regions have mapped to it.
            // For instance, while a virtual mapping could cover 0-2 in physical space, the space 0-1 may have already been reserved...
            // So we need to return both the split 0-1 and 1-2 ranges.

            var results = new PhysicalRegion[1];
            int count = _physicalRegions.FindOverlapsNonOverlapping(pa, size, ref results);

            if (count == 0)
            {
                // The region is fully unmapped. Create and add it to the range list.
                PhysicalRegion region = new PhysicalRegion(this, pa, size);
                list.Add(region);
                _physicalRegions.Add(region);
            } 
            else
            {
                ulong lastAddress = pa;
                ulong endAddress = pa + size;

                for (int i = 0; i < count; i++)
                {
                    PhysicalRegion region = results[i];
                    if (count == 1 && region.Address == pa && region.Size == size)
                    {
                        // Exact match, no splitting required.
                        list.Add(region);
                        return;
                    }

                    if (lastAddress < region.Address)
                    {
                        // There is a gap between this region and the last. We need to fill it.
                        PhysicalRegion fillRegion = new PhysicalRegion(this, lastAddress, region.Address - lastAddress);
                        list.Add(fillRegion);
                        _physicalRegions.Add(fillRegion);
                    }

                    if (region.Address < pa)
                    {
                        // Split the region around our base address and take the high half.

                        region = SplitPhysical(region, pa);
                    }

                    if (region.EndAddress > pa + size)
                    {
                        // Split the region around our end address and take the low half.

                        SplitPhysical(region, pa + size);
                    }

                    list.Add(region);
                    lastAddress = region.EndAddress;
                }

                if (lastAddress < endAddress)
                {
                    // There is a gap between this region and the end. We need to fill it.
                    PhysicalRegion fillRegion = new PhysicalRegion(this, lastAddress, endAddress - lastAddress);
                    list.Add(fillRegion);
                    _physicalRegions.Add(fillRegion);
                }
            }
        }

        /// <summary>
        /// Splits a physical region around a target point and updates the physical region list. 
        /// The original region's size is modified, but its address stays the same.
        /// A new region starting from the split address is added to the region list and returned.
        /// </summary>
        /// <param name="region">The region to split</param>
        /// <param name="splitAddress">The address to split with</param>
        /// <returns>The new region (high part)</returns>
        private PhysicalRegion SplitPhysical(PhysicalRegion region, ulong splitAddress)
        {
            _physicalRegions.Remove(region); // TODO: Is this necessary?

            PhysicalRegion newRegion = region.Split(splitAddress);
            _physicalRegions.Add(region);
            _physicalRegions.Add(newRegion);
            return newRegion;
        }

        internal void RemoveVirtual(VirtualRegion region)
        {
            _virtualRegions.Remove(region);
        }

        internal void RemovePhysical(PhysicalRegion region)
        {
            _physicalRegions.Remove(region);
        }

        private (ulong address, ulong size) PageAlign(ulong address, ulong size)
        {
            ulong pageMask = MemoryManager.PageSize - 1;
            ulong rA = address & (~pageMask);
            ulong rS = ((address + size + pageMask) & (~pageMask)) - rA;
            return (rA, rS);
        }

        public MultiRegionHandle BeginGranularTracking(ulong address, ulong size, ulong granularity)
        {
            (address, size) = PageAlign(address, size);

            List<RegionHandle> handles = new List<RegionHandle>();
            for (; size > 0; address += granularity, size -= granularity)
            {
                handles.Add(BeginTracking(address, Math.Min(granularity, size)));
            }

            return new MultiRegionHandle(handles, granularity);
        }

        public RegionHandle BeginTracking(ulong address, ulong size)
        {
            // Look up virtual region in existing regions.
            // If there is no exact match, create a new one.

            (address, size) = PageAlign(address, size);

            lock (TrackingLock)
            {
                var results = _virtualResults;

                int count = _virtualRegions.FindOverlaps(address, size, ref results);
                VirtualRegion region;

                for (int i = 0; i < count; i++)
                {
                    region = results[i];
                    // Are any virtual regions a perfect match?
                    if (region.Address == address && region.Size == size)
                    {
                        // Get a handle for this region and return it.
                        return region.NewHandle();
                    }
                }

                // We need to create a new region.
                region = new VirtualRegion(this, address, size);
                _virtualRegions.Add(region);
                return region.NewHandle();
            }
        }

        public bool PhysicalMemoryEvent(ulong address, bool write)
        {
            // Look up the physical region using the region list.
            // Signal up the chain to relevant handles.

            lock (TrackingLock)
            {
                var results = _physicalResults;
                int count = _physicalRegions.FindOverlapsNonOverlapping(address, 8, ref results); // TODO: get/use the actual access size?

                if (count == 0)
                {
                    return false; // We can't handle this - it's probably a real invalid access.
                }

                for (int i = 0; i < count; i++)
                {
                    PhysicalRegion region = results[i];
                    region.Signal(write);
                }
            }

            return true;
        }

        public bool VirtualMemoryEvent(ulong address, ulong size, bool write)
        {
            lock (TrackingLock)
            {
                var results = _virtualResults;
                int count = _virtualRegions.FindOverlaps(address, size, ref results); // TODO: get/use the actual access size?

                if (count == 0)
                {
                    return false; // We can't handle this - it's probably a real invalid access.
                }

                for (int i = 0; i < count; i++)
                {
                    VirtualRegion region = results[i];
                    region.Signal(write);
                }
            }

            return true;
        }

        internal void ProtectPhysicalRegion(PhysicalRegion region, MemoryProtection permission)
        {
            _block.MapWithPermission(region.Address, region.Size, permission);
        }

        internal void ProtectVirtualRegion(VirtualRegion region, MemoryProtection permission)
        {
            VirtualProtect(region.Address, region.Size, permission);
        }
    }
}
