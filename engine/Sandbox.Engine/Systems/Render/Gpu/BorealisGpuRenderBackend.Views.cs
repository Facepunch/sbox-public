using Borealis.Backend;
using Borealis.Gpu;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Engine textures made at run time as Borealis textures, for Borealis's draws to read.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	sealed class BorealisView
	{
		public TextureHandle Handle;
		public BackendTexture Texture;
	}

	readonly ConditionalWeakTable<Texture, BorealisView> borealisViews = new();
	readonly object borealisViewsLock = new();

	/// <summary>
	/// <paramref name="texture"/>'s copy on this device as a Borealis texture: the same image, which the engine's drawing keeps
	/// writing (a run-time render target a game sets on a scene object's attributes, a screen's panel texture). It's made
	/// readable at the end of every block, so a Borealis frame submitted after reads what the engine last drew. The view
	/// follows the copy if the texture is made again, and never frees the image, which stays the engine texture's.
	/// </summary>
	internal Borealis.GpuTexture BorealisViewOf( Texture texture )
	{
		lock ( borealisViewsLock )
		{
			var handle = TextureOf( texture );
			var view = borealisViews.GetOrCreateValue( texture );
			if ( view.Texture is not null && view.Handle == handle ) return view.Texture;

			var info = gpu.InfoOf( handle );
			var desc = new Borealis.TextureDesc
			{
				Width = info.Width,
				Height = info.Height,
				Depth = info.Dimension == TextureDimension.Texture3D ? info.Depth : 1,
				Cube = info.Dimension is TextureDimension.TextureCube or TextureDimension.TextureCubeArray,
				Format = Borealis.Gpu.Source2.Source2Formats.From( texture.ImageFormat ),
				Samples = Math.Max( info.Samples, 1 ),
				Mips = Math.Max( info.Mips, 1 ),
				RenderTarget = texture.IsRenderTarget,
			};

			view.Handle = handle;
			// Its image's own shape (an array's, a cube array's), which a shader binding it must expect
			view.Texture = new BackendTexture( services, handle, desc, $"Engine texture {info.Width}x{info.Height}" ) { Layout = ImageLayout.ShaderRead, Dimension = info.Dimension };
			return view.Texture;
		}
	}
}
