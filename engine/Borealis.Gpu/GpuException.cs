namespace Borealis.Gpu;

/// <summary>
/// Thrown when a Vulkan call fails.
/// </summary>
public sealed class GpuException : Exception
{
	/// <summary>The VkResult the call returned, as a number.</summary>
	public int Result { get; }

	internal GpuException( string call, Vulkan.VkResult result ) : base( $"{call} failed: {result}" )
	{
		Result = (int)result;
	}

	/// <summary>A failure that isn't a Vulkan result.</summary>
	public GpuException( string message ) : base( message )
	{
	}
}
