using Borealis.Backend.Upscalers;
using Borealis.Gpu;

namespace Sandbox;

/// <summary>
/// What a camera's upscaler records in the block (<see cref="BorealisGpuUpscaler"/>): a bilinear stretch, and the temporal
/// upscalers' dispatches into the block's own list.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	/// <summary>
	/// Stretch <paramref name="from"/> of <paramref name="source"/> over <paramref name="to"/> of
	/// <paramref name="destination"/>, filtered bilinearly, in the block being recorded, as native's stretch upscale blits.
	/// </summary>
	internal void StretchBlit( Texture source, Rect from, Texture destination, Rect to )
	{
		var list = Recording;
		var src = TextureOf( source );
		var dst = TextureOf( destination );
		context.PrepareNative( src, ImageLayout.CopySource, false );
		context.PrepareNative( dst, ImageLayout.CopyDestination, true );
		list.Blit( src, ((int)from.Left, (int)from.Top, (int)from.Width, (int)from.Height), dst, ((int)to.Left, (int)to.Top, (int)to.Width, (int)to.Height), Filter.Linear );
	}

	/// <summary>
	/// A temporal upscale of <paramref name="color"/> into <paramref name="output"/> in the block being recorded. Returns
	/// whether the upscaler recorded it.
	/// </summary>
	internal bool UpscaleTemporal( TemporalUpscaler upscaler, Texture color, Texture depth, Texture motionVectors, Texture reactive, Texture output, in TemporalUpscaler.Frame frame )
	{
		_ = Recording;
		return upscaler.Dispatch( context, TextureOf( color ), TextureOf( depth ), TextureOf( motionVectors ), reactive is null ? default : TextureOf( reactive ), TextureOf( output ), frame );
	}
}
