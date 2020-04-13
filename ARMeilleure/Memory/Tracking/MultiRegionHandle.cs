using System;
using System.Collections;
using System.Collections.Generic;

namespace ARMeilleure.Memory.Tracking
{
    public class MultiRegionHandle : IDisposable
    {
        private RegionHandle[] _handles;
        private ulong Address;
        private ulong Granularity;
        private ulong Size;
        public bool Dirty { get; private set; } = true;

        internal MultiRegionHandle(List<RegionHandle> handles, ulong granularity)
        {
            _handles = handles.ToArray();
            Granularity = granularity;

            Address = handles[0].Address;
            Size = 0;

            foreach (RegionHandle handle in handles)
            {
                handle.Parent = this;
                Size += handle.Size;
            }
        }

        public void SignalWrite()
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

            ulong rgStart = _handles[0].Address;
            ulong rgSize = 0;

            foreach (RegionHandle handle in _handles)
            {
                if (handle.Dirty)
                {
                    rgSize += handle.Size;
                    handle.Reprotect();
                } 
                else
                {
                    // Submit the region scanned so far as dirty
                    if (rgSize != 0)
                    {
                        modifiedAction(rgStart, rgSize);
                        rgSize = 0;
                    }
                    rgStart = handle.Address + handle.Size;
                }
            }

            if (rgSize != 0)
            {
                modifiedAction(rgStart, rgSize);
            }
        }

        public void QueryModified(ulong address, ulong size, Action<ulong, ulong> modifiedAction)
        {
            // TODO: dirty flag over all? (without cost of refreshing it after iteration)

            int startHandle = (int)((address - Address) / Granularity);
            int lastHandle = (int)((address + (size - 1) - Address) / Granularity);

            ulong rgStart = _handles[startHandle].Address;
            ulong rgSize = 0;

            for (int i = startHandle; i <= lastHandle; i++)
            {
                RegionHandle handle = _handles[i];
                if (handle.Dirty)
                {
                    rgSize += handle.Size;
                    handle.Reprotect();
                }
                else
                {
                    // Submit the region scanned so far as dirty
                    if (rgSize != 0)
                    {
                        modifiedAction(rgStart, rgSize);
                        rgSize = 0;
                    }
                    rgStart = handle.Address + handle.Size;
                }
            }

            if (rgSize != 0)
            {
                modifiedAction(rgStart, rgSize);
            }

            /*
            if (address != Address || Size != size)
            {
                Dirty |= CalculateDirty();
            }
            */
        }

        public bool CalculateDirty()
        {
            bool dirty = false;
            foreach (RegionHandle handle in _handles)
            {
                dirty |= handle.Dirty;
            }
            return dirty;
        }

        public void Dispose()
        {
            foreach (var handle in _handles)
            {
                handle.Dispose();
            }
        }
    }
}
