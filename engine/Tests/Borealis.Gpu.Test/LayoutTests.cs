using Borealis.Gpu;
using Borealis.Gpu.Memory;
using Borealis.Gpu.Vulkan;
using System.IO;
using System.Runtime.CompilerServices;

namespace BorealisGpuTests;

/// <summary>
/// Things that need no GPU: the hand-written Vulkan structs against the C header's sizes, the block allocator's free
/// lists, handle generations, and the shader binding layout.
/// </summary>
[TestClass]
public class LayoutTests
{
	/// <summary>
	/// Sizes of the hand-written structs, as vulkan_core.h lays them out on 64-bit targets.
	/// </summary>
	[TestMethod]
	public void StructsMatchTheHeader()
	{
		Assert.AreEqual( 504, Unsafe.SizeOf<VkPhysicalDeviceLimits>() );
		Assert.AreEqual( 824, Unsafe.SizeOf<VkPhysicalDeviceProperties>() );
		Assert.AreEqual( 220, Unsafe.SizeOf<VkPhysicalDeviceFeatures>() );
		Assert.AreEqual( 208, Unsafe.SizeOf<VkPhysicalDeviceVulkan12Features>() );
		Assert.AreEqual( 80, Unsafe.SizeOf<VkPhysicalDeviceVulkan13Features>() );
		Assert.AreEqual( 520, Unsafe.SizeOf<VkPhysicalDeviceMemoryProperties>() );
		Assert.AreEqual( 72, Unsafe.SizeOf<VkRenderingAttachmentInfo>() );
		Assert.AreEqual( 96, Unsafe.SizeOf<VkImageMemoryBarrier2>() );
		Assert.AreEqual( 80, Unsafe.SizeOf<VkBufferMemoryBarrier2>() );
		Assert.AreEqual( 104, Unsafe.SizeOf<VkSwapchainCreateInfoKHR>() );
		Assert.AreEqual( 88, Unsafe.SizeOf<VkImageCreateInfo>() );
		Assert.AreEqual( 144, Unsafe.SizeOf<VkGraphicsPipelineCreateInfo>() );
		Assert.AreEqual( 80, Unsafe.SizeOf<VkSamplerCreateInfo>() );
		Assert.AreEqual( 64, Unsafe.SizeOf<VkWriteDescriptorSet>() );
		Assert.AreEqual( 48, Unsafe.SizeOf<VkSemaphoreSubmitInfo>() );
		Assert.AreEqual( 16, Unsafe.SizeOf<VkClearValue>() );
	}

	/// <summary>
	/// The bindless set's bindings are where vfx puts s&amp;box's registers.
	/// </summary>
	[TestMethod]
	public void BindlessBindingsFollowVfx()
	{
		Assert.AreEqual( 4, ShaderAbi.BindlessSet );
		Assert.AreEqual( 29, ShaderAbi.BindlessSamplers );
		Assert.AreEqual( 49, ShaderAbi.BindlessVertexCache );
		Assert.AreEqual( 60, ShaderAbi.BindlessPipelineTextures );
		Assert.AreEqual( 62, ShaderAbi.BindlessTextures );
		Assert.AreEqual( 190, ShaderAbi.BindlessStorageTextures );
		Assert.AreEqual( 46, ShaderAbi.Texture( 0 ) );
	}

	/// <summary>
	/// The view set's bindings are native's (<c>g_ViewDescriptorSetEntries</c> in scenesystem/viewdescriptorset.h), in
	/// the same order, each at its register's binding and filled from the same attribute.
	/// </summary>
	[TestMethod]
	public void ViewSetBindingsMatchNative()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory is not null && !File.Exists( Path.Combine( directory.FullName, "src", "public", "scenesystem", "viewdescriptorset.h" ) ) ) directory = directory.Parent;
		Assert.IsNotNull( directory, "the checkout's src/public/scenesystem/viewdescriptorset.h" );

		var header = File.ReadAllText( Path.Combine( directory.FullName, "src", "public", "scenesystem", "viewdescriptorset.h" ) );
		var entries = System.Text.RegularExpressions.Regex.Matches( header, @"\{\s*VIEW_DESCRIPTOR_(CONSTANT_BUFFER|BUFFER),\s*(\d+),\s*""(\w+)""\s*\}" );

		Assert.AreEqual( 5, ShaderAbi.ViewSet );
		Assert.AreEqual( entries.Count, ShaderAbi.ViewSetBindings.Length );
		for ( int i = 0; i < entries.Count; i++ )
		{
			var constantBuffer = entries[i].Groups[1].Value == "CONSTANT_BUFFER";
			var register = int.Parse( entries[i].Groups[2].Value );
			var (binding, type, attribute) = ShaderAbi.ViewSetBindings[i];
			Assert.AreEqual( constantBuffer ? ShaderAbi.ConstantBuffer( register ) : ShaderAbi.Texture( register ), binding, attribute );
			Assert.AreEqual( constantBuffer ? BindingType.UniformBuffer : BindingType.StorageBuffer, type, attribute );
			Assert.AreEqual( entries[i].Groups[3].Value, attribute );
		}
	}

	/// <summary>
	/// First-fit allocation honours alignment, and freeing merges neighbours back into one range.
	/// </summary>
	[TestMethod]
	public void BlocksAllocateAlignedAndCoalesce()
	{
		var block = new MemoryBlock { Size = 1024 };
		block.Free.Add( (0, 1024) );

		Assert.IsTrue( block.TryAllocate( 100, 1, out var a ) );
		Assert.IsTrue( block.TryAllocate( 100, 256, out var b ) );
		Assert.IsTrue( block.TryAllocate( 300, 64, out var c ) );
		Assert.AreEqual( 0ul, a );
		Assert.AreEqual( 256ul, b );
		Assert.AreEqual( 384ul, c );
		Assert.IsFalse( block.TryAllocate( 400, 1, out _ ), "only 340 bytes are left at the end" );

		block.Release( b, 100 );
		block.Release( a, 100 );
		block.Release( c, 300 );
		Assert.AreEqual( 1, block.Free.Count );
		Assert.AreEqual( (0ul, 1024ul), block.Free[0] );
		Assert.AreEqual( 0ul, block.Used );
	}

	/// <summary>
	/// A destroyed handle stops naming anything, even once its index is reused.
	/// </summary>
	[TestMethod]
	public void StaleHandlesThrow()
	{
		var pool = new HandlePool<int>( "thing" );
		var (index, generation) = pool.Add( 7 );
		Assert.AreEqual( 7, pool.Get( index, generation ) );

		pool.Remove( index, generation );
		var (reused, newGeneration) = pool.Add( 8 );
		Assert.AreEqual( index, reused );
		Assert.AreNotEqual( generation, newGeneration );
		Assert.ThrowsException<ArgumentException>( () => pool.Get( index, generation ) );
		Assert.ThrowsException<ArgumentException>( () => pool.Get( 0, 0 ) );
	}
}
