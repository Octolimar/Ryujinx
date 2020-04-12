using System;
using System.Collections;
using System.Collections.Generic;

namespace ARMeilleure.Memory.Tracking
{
    public class MultiRegionHandle : IDisposable, IEnumerable<RegionHandle>
    {
        private List<RegionHandle> _handles;
        private ulong Address;
        private ulong Size;
        public bool Dirty { get; private set; } = true;

        internal MultiRegionHandle(List<RegionHandle> handles)
        {
            _handles = handles;
            Address = handles[0].Address;
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

            Dirty = false;
        }

        public void QueryModified(ulong address, ulong size, Action<ulong, ulong> modifiedAction)
        {
            if (!Dirty)
            {
                return;
            }

            ulong endAddress = address + size;

            ulong rgStart = _handles[0].Address;
            ulong rgSize = 0;

            foreach (RegionHandle handle in _handles)
            {
                if (handle.EndAddress < address)
                {
                    rgStart = handle.Address + handle.Size;
                    continue;
                }

                if (handle.Address > endAddress)
                {
                    break;
                }

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
                modifiedAction(rgStart, rgSize); // THIS NEEDS TO BE IN PHYSICAL SPACE!
            }

            if (address != Address || Size != size)
            {
                RecalulateDirty();
            } 
            else
            {
                Dirty = false;
            }
        }

        public void RecalulateDirty()
        {
            bool dirty = false;
            foreach (RegionHandle handle in _handles)
            {
                dirty |= handle.Dirty;
            }
            Dirty = dirty;
        }

        public void ClearDirty()
        {
            Dirty = false;
        }

        public void Dispose()
        {
            foreach (var handle in _handles)
            {
                handle.Dispose();
            }
        }

        public IEnumerator<RegionHandle> GetEnumerator()
        {
            return _handles.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _handles.GetEnumerator();
        }
    }
}
