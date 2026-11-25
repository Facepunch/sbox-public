using Borealis.Gpu;
using Borealis.Gpu.Vulkan;

namespace BorealisGpuTests;

/// <summary>
/// A Vulkan call's check formats the call's description only when it failed: creating a texture checked with its name and
/// size, which made a string for every texture made (streaming and relocation make them every few frames).
/// </summary>
[TestClass]
public class VkCheckTests
{
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void SuccessFormatsNothing()
	{
		var name = "a texture";
		var width = 64;
		Allocations.AssertSettles( "a successful call's check", () => VkResult.Success.Check( $"vkCreateImage ({name}, {width}x{width})" ) );
	}

	[TestMethod]
	public void FailureSaysTheCall()
	{
		var name = "a texture";
		var thrown = Assert.ThrowsException<GpuException>( () => VkResult.ErrorOutOfDeviceMemory.Check( $"vkCreateImage ({name}, {64}x{32})" ) );
		Assert.AreEqual( "vkCreateImage (a texture, 64x32) failed: ErrorOutOfDeviceMemory", thrown.Message );
		Assert.AreEqual( (int)VkResult.ErrorOutOfDeviceMemory, thrown.Result );
	}
}
