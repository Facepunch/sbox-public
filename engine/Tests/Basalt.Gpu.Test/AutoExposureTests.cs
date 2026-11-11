using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2.Shaders;
using System.Runtime.InteropServices;

namespace BasaltGpuTests;

/// <summary>
/// <see cref="AutoExposure"/>: native's tonemap system ported, counting a frame's luminance per bucket by drawing
/// <c>tonemap_query</c> inside an occlusion query, one bucket a frame. Inconclusive where the shaders aren't built.
/// </summary>
[TestClass]
public class AutoExposureTests : GpuTest
{
	const int Size = 64;

	/// <summary>
	/// A frame whose left half has luminance 0.5 and right half 0.8, counted a bucket a frame and read back three frames
	/// later: the middle 90% (57 pixels square from 3, 3) lands in two buckets, 29 columns in the one holding 0.5 and 28 in
	/// the one holding 0.8, and the rest count nothing - in log luminance (s&amp;box's sixteen buckets) and in the classic
	/// eight. Then the exposure has a target.
	/// </summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void BucketsCountPixelsInRange( bool logLuminance )
	{
		var library = new ShaderLibrary( Device, ShaderFileTests.Files );
		var defaults = new ShaderDefaults( Device );
		var drawer = new ShaderDrawer( Device, library, defaults );
		var exposure = new AutoExposure( Device, logLuminance );
		try
		{
			var shader = library.Load( "shaders/tonemap_query.shader" );
			if ( shader is null ) Assert.Inconclusive( "tonemap_query isn't built" );

			var pixels = new Half[Size * Size * 4];
			for ( int y = 0; y < Size; y++ )
			{
				for ( int x = 0; x < Size; x++ )
				{
					var v = (Half)(x < Size / 2 ? 0.5f : 0.8f);
					pixels.AsSpan( (y * Size + x) * 4, 4 ).Fill( v );
				}
			}

			var color = Device.CreateTexture( TextureInfo.Texture2D( Size, Size, Format.RGBA16Float, name: "Frame" ) );
			var upload = Device.BeginCommandList( QueueType.Graphics, "Frame" );
			upload.Barrier( TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
			upload.UpdateTexture( color, MemoryMarshal.AsBytes( pixels.AsSpan() ), 0, 0, Size, Size );
			Device.Wait( Device.Submit( upload ) );

			TextureHandle target = default;
			var context = new GraphicsContext( Device, drawer, null );
			context.Track( color, ImageLayout.CopyDestination );

			// A bucket a frame, each read three frames after it's counted
			for ( int frame = 0; frame < exposure.Buckets + 4; frame++ )
			{
				exposure.FrameUpdate( 0 );
				var list = Device.BeginCommandList( QueueType.Graphics, "Count" );
				context.Begin( list );
				if ( !target.IsValid ) target = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Target" );
				context.SetTarget( target );
				exposure.Draw( context, shader, color, 0, 0, Size, Size );
				context.Finish();
				Device.Wait( Device.Submit( list ) );
			}

			exposure.FrameUpdate( 0 );

			var counts = Enumerable.Range( 0, exposure.Buckets ).Select( i => exposure.BucketAt( i ) ).ToArray();
			var holding = ( int bucket, float luminance ) =>
			{
				var value = logLuminance ? MathF.Log2( luminance ) : luminance;
				return counts[bucket].Min <= value && value <= counts[bucket].Max;
			};
			for ( int i = 0; i < counts.Length; i++ )
			{
				var expected = holding( i, 0.5f ) ? 29 * 57 : holding( i, 0.8f ) ? 28 * 57 : 0;
				Assert.AreEqual( expected, counts[i].Pixels, $"bucket {i} ({counts[i].Min}..{counts[i].Max})" );
			}

			exposure.FrameUpdate( 0 );
			Assert.AreNotEqual( 1.0f, exposure.Target, "the histogram moves the target" );
			Assert.IsTrue( float.IsFinite( exposure.Scale ) );
			context.Release();
			Device.Destroy( color );
		}
		finally
		{
			Device.WaitIdle();
			exposure.Dispose();
			drawer.Dispose();
			library.Dispose();
			defaults.Dispose();
		}
	}
}
