using Ryujinx.Cpu.Tracking;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ryujinx.Graphics.Gpu.Memory
{
    /// <summary>
    /// Represents physical memory, accessible from the GPU.
    /// This is actually working CPU virtual addresses, of memory mapped on the application process.
    /// </summary>
    class PhysicalMemory
    {
        public const int PageSize = Cpu.MemoryManager.PageSize;

        private readonly Cpu.MemoryManager _cpuMemory;

        /// <summary>
        /// Creates a new instance of the physical memory.
        /// </summary>
        /// <param name="cpuMemory">CPU memory manager of the application process</param>
        public PhysicalMemory(Cpu.MemoryManager cpuMemory)
        {
            _cpuMemory = cpuMemory;
        }

        /// <summary>
        /// Gets a span of data from the application process.
        /// </summary>
        /// <param name="address">Start address of the range</param>
        /// <param name="size">Size in bytes to be range</param>
        /// <returns>A read only span of the data at the specified memory location</returns>
        public ReadOnlySpan<byte> GetSpan(ulong address, int size)
        {
            return _cpuMemory.GetSpan(address, size);
        }

        /// <summary>
        /// Reads data from the application process.
        /// </summary>
        /// <typeparam name="T">Type of the structure</typeparam>
        /// <param name="gpuVa">Address to read from</param>
        /// <returns>The data at the specified memory location</returns>
        public T Read<T>(ulong address) where T : unmanaged
        {
            return MemoryMarshal.Cast<byte, T>(GetSpan(address, Unsafe.SizeOf<T>()))[0];
        }

        /// <summary>
        /// Writes data to the application process.
        /// </summary>
        /// <param name="address">Address to write into</param>
        /// <param name="data">Data to be written</param>
        public void Write(ulong address, ReadOnlySpan<byte> data)
        {
            _cpuMemory.Write(address, data);
        }

        /// <summary>
        /// Obtains a memory tracking handle for the given virtual region. This should be disposed when finished with.
        /// </summary>
        /// <param name="address">CPU virtual address of the region</param>
        /// <param name="size">Size of the region</param>
        /// <returns>The memory tracking handle</returns>
        public CpuRegionHandle BeginTracking(ulong address, ulong size)
        {
            return _cpuMemory.BeginTracking(address, size);
        }

        /// <summary>
        /// Obtains memory tracking handles for the given virtual region, with a specified granularity. This should be disposed when finished with.
        /// </summary>
        /// <param name="address">CPU virtual address of the region</param>
        /// <param name="size">Size of the region</param>
        /// <param name="granularity">Desired granularity of write tracking</param>
        /// <returns>The memory tracking handles</returns>
        public CpuMultiRegionHandle BeginGranularTracking(ulong address, ulong size, ulong granularity)
        {
            return _cpuMemory.BeginGranularTracking(address, size, granularity);
        }
    }
}