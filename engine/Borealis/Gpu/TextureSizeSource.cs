namespace Borealis;

/// <summary>
/// Texture sizes for draws that don't give their own (aggregates the GPU culls): for each material, the size in texels its
/// draws need (<see cref="TextureSizes"/>). A device that streams textures takes them once a frame
/// (<see cref="GpuDevice.AddTextureSizes"/>). The CPU's comes from <see cref="Features.AggregateRenderFeature"/>; a GPU
/// cull's could stand in for it.
/// </summary>
internal interface ITextureSizeSource
{
	/// <summary>
	/// Ask for the sizes found since the last call, each material once, through <paramref name="ask"/>; nothing when there's
	/// nothing new. Called on the thread that begins the device's frames.
	/// </summary>
	void Take( Action<GpuMaterial, int> ask );
}
