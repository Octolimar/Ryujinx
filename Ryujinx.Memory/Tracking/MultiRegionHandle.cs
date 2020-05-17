using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ryujinx.Memory.Tracking
{
    public class MultiRegionHandle : IMultiRegionHandle
    {
        private const int CheckCountToMakeDecision = 400;
        private const int AlwaysDirtyThreshold = 4;

        /// <summary>
        /// A list of region handles starting at each granularity size increment.
        /// </summary>
        private RegionHandle[] _handles;
        private ulong Address;
        private ulong Granularity;
        private ulong Size;
        private MemoryTracking Tracking;

        public bool Dirty { get; private set; } = true;

        internal MultiRegionHandle(MemoryTracking tracking, ulong address, ulong size, ulong granularity)
        {
            Tracking = tracking;
            _handles = new RegionHandle[size / granularity]; 
            Granularity = granularity;

            Address = address;
            Size = size;
        }

        public void InitMinimumGranularity(ulong granularity)
        {
            for (ulong i = 0; i < Size; i += Granularity)
            {
                RegionHandle handle = Tracking.BeginTracking(Address + i, granularity);
                handle.Parent = this;
                _handles[i / Granularity] = handle;
            }
        }

        internal void SignalWrite()
        {
            Dirty = true;
        }

        public void QueryModified(Action<ulong, ulong> modifiedAction)
        {
            if (!Dirty)
            {
                return;
            }

            Dirty = false;

            QueryModified(Address, Size, modifiedAction);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong HandlesToBytes(int handles)
        {
            return (ulong)handles * Granularity;
        }

        private void SplitHandle(int handleIndex, int splitIndex)
        {
            RegionHandle handle = _handles[handleIndex];
            ulong address = Address + HandlesToBytes(handleIndex);
            ulong size = HandlesToBytes(splitIndex - handleIndex);

            // First, the target handle must be removed. Its data can still be used to determine the new handles.
            handle.Dispose();

            RegionHandle splitLow = Tracking.BeginTracking(address, size);
            splitLow.Parent = this;
            _handles[handleIndex] = splitLow;

            RegionHandle splitHigh = Tracking.BeginTracking(address + size, handle.Size - size);
            splitHigh.Parent = this;
            _handles[splitIndex] = splitHigh;
        }

        private void CreateHandle(int startHandle, int lastHandle)
        {
            ulong startAddress = Address + HandlesToBytes(startHandle);

            // Scan for the first handle before us. If it's overlapping us, it must be split.
            for (int i = startHandle - 1; i >= 0; i--)
            {
                RegionHandle handle = _handles[i];
                if (handle != null)
                {
                    if (handle.EndAddress > startAddress)
                    {
                        SplitHandle(i, startHandle);
                        return; // The remainer of this handle should be filled in later on.
                    }
                    break;
                }
            }

            // Scan for handles after us. We should create a handle that goes up to this handle's start point, if present.

            for (int i = startHandle + 1; i <= lastHandle; i++)
            {
                RegionHandle handle = _handles[i];
                if (handle != null)
                {
                    // Fill up to the found handle.
                    handle = Tracking.BeginTracking(startAddress, HandlesToBytes(i - startHandle));
                    handle.Parent = this;
                    _handles[startHandle] = handle;
                    return;
                }
            }

            // Can fill the whole range.
            _handles[startHandle] = Tracking.BeginTracking(startAddress, HandlesToBytes(1 + lastHandle - startHandle));
            _handles[startHandle].Parent = this;
        }

        public void QueryModified(ulong address, ulong size, Action<ulong, ulong> modifiedAction)
        {
            int startHandle = (int)((address - Address) / Granularity);
            int lastHandle = (int)((address + (size - 1) - Address) / Granularity);

            ulong rgStart = Address + (ulong)startHandle * Granularity;
            ulong rgSize = 0;

            ulong endAddress = Address + ((ulong)lastHandle + 1) * Granularity;

            int i = startHandle;

            while (i <= lastHandle)
            {
                RegionHandle handle = _handles[i];
                if (handle == null)
                {
                    // Missing handle. A new handle must be created.
                    CreateHandle(i, lastHandle);
                    handle = _handles[i];
                } 
                if (handle.EndAddress > endAddress)
                {
                    // End address of handle is beyond the end of the search. Force a split.
                    SplitHandle(i, lastHandle + 1);
                    handle = _handles[i];
                }

                handle.CheckCount++;

                if (handle.Dirty)
                {
                    rgSize += handle.Size;
                    if (!handle.AlwaysDirty) {
                        if (handle.CheckCount > CheckCountToMakeDecision)
                        {
                            if (handle.ReprotectCount > handle.CheckCount / AlwaysDirtyThreshold)
                            {
                                handle.AlwaysDirty = true;
                            }
                        }

                        handle.ReprotectCount++;
                        handle.Reprotect();
                    }
                }
                else
                {
                    // Submit the region scanned so far as dirty
                    if (rgSize != 0)
                    {
                        modifiedAction(rgStart, rgSize);
                        rgSize = 0;
                    }
                    rgStart = handle.EndAddress;
                }

                i += (int)(handle.Size / Granularity);
            }

            if (rgSize != 0)
            {
                modifiedAction(rgStart, rgSize);
            }
        }

        private bool CalculateDirty()
        {
            bool dirty = false;
            foreach (RegionHandle handle in _handles)
            {
                if (handle != null)
                {
                    dirty |= handle.Dirty;
                }
            }
            return dirty;
        }

        public void Dispose()
        {
            foreach (var handle in _handles)
            {
                handle?.Dispose();
            }
        }
    }
}
