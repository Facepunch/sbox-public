using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Shadows;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// A cached shadow map's depth drawn into a cascade (<c>borealis_shadow_cache_copy</c>, <see cref="ShadowSystem.DrawCache"/>):
/// every texel as the cache holds it, unwrapped by the cache's offset, as the transfer copies it replaces left it.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ShadowCacheCopyTests : RenderGpuTest
{
	[TestMethod]
	[DataRow( 0, 0 )]
	[DataRow( 37, 11 )]
	[DataRow( -5, 70 )]
	public void WritesTheCacheExactly( int offsetX, int offsetY )
	{
		const int Width = 96, Height = 64;
		var random = new Random( 1 + offsetX * 31 + offsetY );
		var depth = new float[Width * Height];
		for ( int i = 0; i < depth.Length; i++ ) depth[i] = random.NextSingle();
		depth[0] = 0;
		depth[1] = 1;

		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var bytes = new byte[depth.Length * 4];
			Buffer.BlockCopy( depth, 0, bytes, 0, bytes.Length );
			using var cache = backend.CreateTexture( new TextureDesc { Width = Width, Height = Height, Depth = 1, Format = TextureFormat.R32F, Samples = 1, Mips = 1, Data = bytes }, "Shadow cache" );
			using var map = backend.CreateTexture( TextureDesc.Target( Width, Height, TextureFormat.D32 ), "Shadow map" );

			// Drawn once its pipeline is made: the first try may skip, as a frame's would (which copies instead)
			ShadowSystem.WarmUp();
			var drew = false;
			using var instanceIds = new GpuBuffer<uint>( 16, GpuBuffer.UsageFlags.Vertex, "Shadow cache copy instance ids" );
			for ( int attempt = 0; attempt < 3 && !drew; attempt++ )
			{
				// A frame's context has its instance ids (RenderSystem); the full-screen draw is one instance
				using var context = new BackendContext( backend, null ) { InstanceIds = instanceIds };
				context.Begin();
				drew = ShadowSystem.DrawCache( context, cache, map, new Vector2Int( offsetX, offsetY ) );
				context.BarrierToPixelShaderRead( map );
				context.Submit();
				Device.WaitIdle();
				if ( !drew ) Device.WaitForPendingPipelines( TimeSpan.FromSeconds( 30 ) );
			}

			Assert.IsTrue( drew, "the cache is drawn once its pipeline is made" );

			var (handle, layout) = BackendDevice.Unwrap( map );
			var texels = ReadBack( handle, layout );
			BackendDevice.SetLayout( map, ImageLayout.CopySource );

			int ox = ((offsetX % Width) + Width) % Width, oy = ((offsetY % Height) + Height) % Height;
			for ( int y = 0; y < Height; y++ )
			{
				for ( int x = 0; x < Width; x++ )
				{
					var expected = depth[((y + oy) % Height) * Width + (x + ox) % Width];
					var got = BitConverter.ToSingle( texels, (y * Width + x) * 4 );
					if ( BitConverter.SingleToInt32Bits( expected ) != BitConverter.SingleToInt32Bits( got ) )
						Assert.Fail( $"Texel {x},{y}: expected {expected:R}, got {got:R}" );
				}
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// The sparse copy: the cache's 32x32 tiles made (<see cref="ShadowSystem.BuildCacheTiles"/>), the map cleared, and only the
	/// tiles holding something nearer than the clear written; what the map held before is gone everywhere. A wrap of whole
	/// tiles draws a quad a tile; any other discards texel by texel.
	/// </summary>
	[TestMethod]
	[DataRow( 0, 0 )]
	[DataRow( 64, 32 )]
	[DataRow( -32, 96 )]
	[DataRow( 37, 11 )]
	public void SparseCopyWritesOnlyTilesWithCasters( int offsetX, int offsetY )
	{
		const int Width = 128, Height = 128, Tile = 32;
		var random = new Random( 7 + offsetX * 31 + offsetY );
		var depth = new float[Width * Height];
		for ( int y = 0; y < Height; y++ )
		{
			for ( int x = 0; x < Width; x++ )
			{
				// Tiles on a checkerboard hold casters (with clear texels among them); the rest are clear
				var holds = ((x / Tile) + (y / Tile)) % 2 == 0;
				depth[y * Width + x] = holds && random.Next( 4 ) != 0 ? random.NextSingle() : 0;
			}
		}

		var stale = new float[Width * Height];
		Array.Fill( stale, 0.75f );

		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var cache = Upload( backend, depth, Width, Height, "Shadow cache" );
			using var old = Upload( backend, stale, Width, Height, "Stale map" );
			using var map = backend.CreateTexture( TextureDesc.Target( Width, Height, TextureFormat.D32 ), "Shadow map" );
			using var tiles = backend.CreateTexture( TextureDesc.Target( Width / Tile, Height / Tile, TextureFormat.RG3232F ) with { Storage = true }, "Cache tiles" );

			ShadowSystem.WarmUp();
			var drew = false;
			using var instanceIds = new GpuBuffer<uint>( 16, GpuBuffer.UsageFlags.Vertex, "Shadow cache copy instance ids" );
			for ( int attempt = 0; attempt < 3 && !drew; attempt++ )
			{
				using var context = new BackendContext( backend, null ) { InstanceIds = instanceIds };
				context.Begin();

				// The map holds something else first, which the sparse copy must not leave behind
				drew = ShadowSystem.DrawCache( context, old, map, default );
				ShadowSystem.BuildCacheTiles( context, cache, tiles );
				drew &= ShadowSystem.DrawCache( context, cache, map, new Vector2Int( offsetX, offsetY ), tiles );
				context.BarrierToPixelShaderRead( map );
				context.Submit();
				Device.WaitIdle();
				if ( !drew ) Device.WaitForPendingPipelines( TimeSpan.FromSeconds( 30 ) );
			}

			Assert.IsTrue( drew, "the cache is drawn once its pipelines are made" );

			var (handle, layout) = BackendDevice.Unwrap( map );
			var texels = ReadBack( handle, layout );
			BackendDevice.SetLayout( map, ImageLayout.CopySource );

			int ox = ((offsetX % Width) + Width) % Width, oy = ((offsetY % Height) + Height) % Height;
			for ( int y = 0; y < Height; y++ )
			{
				for ( int x = 0; x < Width; x++ )
				{
					var expected = depth[((y + oy) % Height) * Width + (x + ox) % Width];
					var got = BitConverter.ToSingle( texels, (y * Width + x) * 4 );
					if ( BitConverter.SingleToInt32Bits( expected ) != BitConverter.SingleToInt32Bits( got ) )
						Assert.Fail( $"Texel {x},{y}: expected {expected:R}, got {got:R}" );
				}
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	static GpuTexture Upload( BackendDevice backend, float[] depth, int width, int height, string name )
	{
		var bytes = new byte[depth.Length * 4];
		Buffer.BlockCopy( depth, 0, bytes, 0, bytes.Length );
		return backend.CreateTexture( new TextureDesc { Width = width, Height = height, Depth = 1, Format = TextureFormat.R32F, Samples = 1, Mips = 1, Data = bytes }, name );
	}
}
