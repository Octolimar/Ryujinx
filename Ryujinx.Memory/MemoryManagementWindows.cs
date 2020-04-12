using System;
using System.Runtime.InteropServices;

namespace Ryujinx.Memory
{
    static class MemoryManagementWindows
    {
        [Flags]
        private enum AllocationType : uint
        {
            Commit = 0x1000,
            Reserve = 0x2000,
            Decommit = 0x4000,
            Release = 0x8000,
            Reset = 0x80000,
            Physical = 0x400000,
            TopDown = 0x100000,
            WriteWatch = 0x200000,
            LargePages = 0x20000000
        }

        [Flags]
        private enum MemoryProtection : uint
        {
            NoAccess = 0x01,
            ReadOnly = 0x02,
            ReadWrite = 0x04,
            WriteCopy = 0x08,
            Execute = 0x10,
            ExecuteRead = 0x20,
            ExecuteReadWrite = 0x40,
            ExecuteWriteCopy = 0x80,
            GuardModifierflag = 0x100,
            NoCacheModifierflag = 0x200,
            WriteCombineModifierflag = 0x400,

            Reserve = 0x4000000,
            Commit = 0x8000000
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualAlloc(
            IntPtr lpAddress,
            IntPtr dwSize,
            AllocationType flAllocationType,
            MemoryProtection flProtect);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(
            IntPtr lpAddress,
            IntPtr dwSize,
            MemoryProtection flNewProtect,
            out MemoryProtection lpflOldProtect);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualFree(IntPtr lpAddress, IntPtr dwSize, AllocationType dwFreeType);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateFileMappingW(IntPtr hFile, IntPtr lpFileMappingAttributes, MemoryProtection flProtect, uint dwMaximumSizeHigh, uint dwMaximumSizeLow, IntPtr lpName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr MapViewOfFile(IntPtr handle, int dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, IntPtr dwSize);

        public static IntPtr Allocate(IntPtr size)
        {
            return AllocateInternal(size, AllocationType.Reserve | AllocationType.Commit);
        }

        public static IntPtr[] AllocateViews(IntPtr size, int views)
        {
            ulong sizeInt = (ulong)size;
            IntPtr file = CreateFileMappingW(new IntPtr(-1), IntPtr.Zero, MemoryProtection.ReadWrite, (uint)(sizeInt >> 32), (uint)sizeInt, IntPtr.Zero);

            IntPtr[] results = new IntPtr[views];
            for (int i = 0; i < views; i++)
            {
                results[i] = MapViewOfFile(file, 0xf001f, 0, 0, size);
            }
            return results;
        }

        public static IntPtr Reserve(IntPtr size)
        {
            return AllocateInternal(size, AllocationType.Reserve);
        }

        private static IntPtr AllocateInternal(IntPtr size, AllocationType flags = 0)
        {
            IntPtr ptr = VirtualAlloc(IntPtr.Zero, size, flags, MemoryProtection.ReadWrite);

            if (ptr == IntPtr.Zero)
            {
                throw new OutOfMemoryException();
            }

            return ptr;
        }

        public static bool Commit(IntPtr location, IntPtr size)
        {
            return VirtualAlloc(location, size, AllocationType.Commit, MemoryProtection.ReadWrite) != IntPtr.Zero;
        }

        public static bool Reprotect(IntPtr address, IntPtr size, MemoryPermission permission)
        {
            return VirtualProtect(address, size, GetProtection(permission), out _);
        }

        private static MemoryProtection GetProtection(MemoryPermission permission)
        {
            return permission switch
            {
                MemoryPermission.None => MemoryProtection.NoAccess,
                MemoryPermission.Read => MemoryProtection.ReadOnly,
                MemoryPermission.ReadAndWrite => MemoryProtection.ReadWrite,
                MemoryPermission.ReadAndExecute => MemoryProtection.ExecuteRead,
                MemoryPermission.ReadWriteExecute => MemoryProtection.ExecuteReadWrite,
                MemoryPermission.Execute => MemoryProtection.Execute,
                _ => throw new MemoryProtectionException(permission)
            };
        }

        public static bool Free(IntPtr address)
        {
            return VirtualFree(address, IntPtr.Zero, AllocationType.Release);
        }
    }
}