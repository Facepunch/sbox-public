using Borealis.Gpu;
using Borealis.Gpu.Memory;
using Borealis.Gpu.Vulkan;

namespace BorealisGpuTests;

/// <summary>
/// Which memory type an allocation of each kind takes (<see cref="MemoryAllocator.FindType"/>), from a device's heaps and
/// types alone.
/// </summary>
[TestClass]
public class MemoryTypeTests
{
	const uint DeviceLocal = 0x1, HostVisible = 0x2, HostCoherent = 0x4, HostCached = 0x8;

	/// <summary>Heaps (size) and types (flags, heap) as a driver lists them.</summary>
	static unsafe VkPhysicalDeviceMemoryProperties Properties( ulong[] heaps, (uint Flags, uint Heap)[] types )
	{
		var properties = new VkPhysicalDeviceMemoryProperties { memoryHeapCount = (uint)heaps.Length, memoryTypeCount = (uint)types.Length };
		var heapArray = (VkMemoryHeap*)properties.memoryHeaps;
		for ( int i = 0; i < heaps.Length; i++ ) heapArray[i] = new VkMemoryHeap { size = heaps[i], flags = i == 1 ? 0u : 1u };
		var typeArray = (VkMemoryType*)properties.memoryTypes;
		for ( int i = 0; i < types.Length; i++ ) typeArray[i] = new VkMemoryType { propertyFlags = types[i].Flags, heapIndex = types[i].Heap };
		return properties;
	}

	/// <summary>
	/// A Ryzen 9950X3D's integrated GPU (AMD 26.11): its device-local memory is a 51 GiB heap the host can't map, system
	/// memory a 25 GiB heap, and the host sees device-local memory only through a 256 MiB window. Uploads go to system
	/// memory, as on a discrete GPU without resizable BAR, not into the window, which ran out while a game loaded.
	/// </summary>
	[TestMethod]
	public void IntegratedUploadsKeepOutOfASmallDeviceLocalWindow()
	{
		var properties = Properties( [51ul << 30, 25ul << 30, 256ul << 20],
		[
			(DeviceLocal, 0),
			(HostVisible | HostCoherent, 1),
			(DeviceLocal | HostVisible | HostCoherent, 2),
			(HostVisible | HostCoherent | HostCached, 1),
		] );

		using var integrated = new MemoryAllocator( null, properties, true );
		Assert.AreEqual( 1, integrated.FindType( ~0u, MemoryLocation.Upload ), "system memory" );
		Assert.AreEqual( 0, integrated.FindType( ~0u, MemoryLocation.Device ) );
		Assert.AreEqual( 3, integrated.FindType( ~0u, MemoryLocation.Readback ) );

		using var discrete = new MemoryAllocator( null, properties, false );
		Assert.AreEqual( 1, discrete.FindType( ~0u, MemoryLocation.Upload ) );
	}

	/// <summary>An integrated GPU whose device-local memory the host maps whole (a unified memory APU) uploads into it.</summary>
	[TestMethod]
	public void IntegratedUploadsGoToMappableDeviceLocalMemory()
	{
		var properties = Properties( [4ul << 30, 8ul << 30],
		[
			(DeviceLocal, 0),
			(HostVisible | HostCoherent, 1),
			(DeviceLocal | HostVisible | HostCoherent, 0),
			(HostVisible | HostCoherent | HostCached, 1),
		] );

		using var integrated = new MemoryAllocator( null, properties, true );
		Assert.AreEqual( 2, integrated.FindType( ~0u, MemoryLocation.Upload ) );
	}
}
