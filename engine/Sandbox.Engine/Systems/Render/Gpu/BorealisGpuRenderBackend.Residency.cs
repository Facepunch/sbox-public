using Borealis.Gpu;

namespace Sandbox;

/// <summary>
/// What the device holds, for the editor's GPU resource viewer.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	/// <summary>A texture on the device: its name, shape, format and the memory it holds.</summary>
	internal readonly record struct ResidentTexture( string Name, ImageFormat Format, int Width, int Height, int Depth, int Layers, int Mips, bool Cube, bool Volume, bool Array, bool RenderTarget, bool Depthbuffer, bool Storage, bool Multisampled, long Bytes );

	/// <summary>Every texture on the device now.</summary>
	internal List<ResidentTexture> ResidentTextures()
	{
		var all = gpu.ListTextures();
		var list = new List<ResidentTexture>( all.Count );
		foreach ( var (info, bytes) in all )
		{
			var cube = info.Dimension is TextureDimension.TextureCube or TextureDimension.TextureCubeArray;
			list.Add( new ResidentTexture(
				info.Name ?? "",
				ImageFormatOf( info.Format ) ?? ImageFormat.None,
				info.Width,
				info.Height,
				Math.Max( info.Depth, 1 ),
				Math.Max( info.Layers, 1 ),
				Math.Max( info.Mips, 1 ),
				cube,
				info.Dimension == TextureDimension.Texture3D,
				info.Dimension is TextureDimension.Texture2DArray or TextureDimension.TextureCubeArray,
				(info.Usage & TextureUsage.ColorTarget) != 0,
				(info.Usage & TextureUsage.DepthTarget) != 0,
				(info.Usage & TextureUsage.Storage) != 0,
				info.Samples > 1,
				bytes ) );
		}

		return list;
	}
}
