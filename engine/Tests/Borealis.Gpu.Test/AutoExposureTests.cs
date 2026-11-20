using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Runtime.InteropServices;

namespace BorealisGpuTests;

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

	/// <summary>
	/// A multisampled frame's resolved colour is metered as native's query meters the multisampled colour, whose
	/// <c>Texture2DMS&lt; float &gt;</c> reads only red: a frame of red 0.8 and green and blue 0.2 (luminance 0.33) counts
	/// every pixel in the bucket holding 0.8, and a single-sampled one in the bucket holding its luminance. Native's query
	/// counts every sample of its multisampled target, so a 4x frame's pixels count four times each.
	/// </summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void MultisampledFramesMeterRed( bool multisampled )
	{
		// The Slang module, as the game draws it: its .shader_c is only Source 2's renderer's
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = e => errors.Add( e );
		using var drawer = new ShaderDrawer( backend );
		var exposure = new AutoExposure( Device );
		try
		{
			var shader = backend.Shaders.Load( "shaders/tonemap_query.shader" );
			if ( shader is null ) Assert.Inconclusive( "tonemap_query isn't built" );

			var pixels = new Half[Size * Size * 4];
			for ( int i = 0; i < Size * Size; i++ )
			{
				pixels[i * 4] = (Half)0.8f;
				pixels[i * 4 + 1] = (Half)0.2f;
				pixels[i * 4 + 2] = (Half)0.2f;
				pixels[i * 4 + 3] = (Half)1.0f;
			}

			var color = Device.CreateTexture( TextureInfo.Texture2D( Size, Size, Format.RGBA16Float, name: "Frame" ) );
			var upload = Device.BeginCommandList( QueueType.Graphics, "Frame" );
			upload.Barrier( TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
			upload.UpdateTexture( color, MemoryMarshal.AsBytes( pixels.AsSpan() ), 0, 0, Size, Size );
			Device.Wait( Device.Submit( upload ) );

			TextureHandle target = default;
			var context = new GraphicsContext( Device, drawer, null );
			context.Track( color, ImageLayout.CopyDestination );
			for ( int frame = 0; frame < exposure.Buckets + 4; frame++ )
			{
				exposure.FrameUpdate( 0 );
				var list = Device.BeginCommandList( QueueType.Graphics, "Count" );
				context.Begin( list );
				if ( !target.IsValid ) target = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Target" );
				context.SetTarget( target );
				exposure.Draw( context, shader, color, 0, 0, Size, Size, multisampled ? 4 : 1 );
				context.Finish();
				Device.Wait( Device.Submit( list ) );
			}

			exposure.FrameUpdate( 0 );

			var metered = MathF.Log2( multisampled ? 0.8f : 0.2125f * 0.8f + 0.7154f * 0.2f + 0.0721f * 0.2f );
			for ( int i = 0; i < exposure.Buckets; i++ )
			{
				var bucket = exposure.BucketAt( i );
				var expected = bucket.Min <= metered && metered <= bucket.Max ? 57 * 57 * (multisampled ? 4 : 1) : 0;
				Assert.AreEqual( expected, bucket.Pixels, $"bucket {i} ({bucket.Min}..{bucket.Max})" );
			}

			Assert.AreEqual( 0, errors.Count, string.Join( "; ", errors ) );
			context.Release();
			Device.Destroy( color );
		}
		finally
		{
			Device.WaitIdle();
			exposure.Dispose();
		}
	}

	/// <summary>
	/// A frame picks its bucket as it begins (<see cref="AutoExposure.Reserve"/>, where native's pipeline picks it), so another
	/// count drawn while the frame draws takes the next one: a 4x frame whose luminance is in the first bucket and a
	/// single-sampled image whose luminance is in the second land each in their own, the frame's pixels four times each.
	/// Counted the other way round, both would count nothing.
	/// </summary>
	[TestMethod]
	public void FramesPickTheirBucketAsTheyBegin()
	{
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = e => errors.Add( e );
		using var drawer = new ShaderDrawer( backend );
		var exposure = new AutoExposure( Device );
		try
		{
			var shader = backend.Shaders.Load( "shaders/tonemap_query.shader" );
			if ( shader is null ) Assert.Inconclusive( "tonemap_query isn't built" );

			// Log luminance -7.6 (the first bucket takes everything below -7.19) and -6.8 (the second, -7.19 to -6.38)
			var frame = Upload( MathF.Pow( 2, -7.6f ) );
			var other = Upload( MathF.Pow( 2, -6.8f ) );

			var context = new GraphicsContext( Device, drawer, null );
			context.Track( frame, ImageLayout.CopyDestination );
			context.Track( other, ImageLayout.CopyDestination );
			exposure.FrameUpdate( 0 );
			var bucket = exposure.Reserve();
			Assert.AreEqual( 0, bucket );

			var list = Device.BeginCommandList( QueueType.Graphics, "Count" );
			context.Begin( list );
			var target = context.CreateTarget( Size, Size, Format.RGBA8Unorm, name: "Target" );
			context.SetTarget( target );
			Assert.IsTrue( exposure.Draw( context, shader, other, 0, 0, Size, Size ) );
			Assert.IsTrue( exposure.Draw( context, shader, frame, 0, 0, Size, Size, 4, bucket ) );
			Assert.IsFalse( exposure.Draw( context, shader, frame, 0, 0, Size, Size, 4, bucket ), "a reservation counts once" );
			context.Finish();
			Device.Wait( Device.Submit( list ) );

			// Read three frames on
			for ( int i = 0; i < 3; i++ ) exposure.FrameUpdate( 0 );
			Assert.AreEqual( 57 * 57 * 4, exposure.BucketAt( 0 ).Pixels, "the frame's bucket" );
			Assert.AreEqual( 57 * 57, exposure.BucketAt( 1 ).Pixels, "the other count's bucket" );

			Assert.AreEqual( 0, errors.Count, string.Join( "; ", errors ) );
			context.Release();
			Device.Destroy( frame );
			Device.Destroy( other );
		}
		finally
		{
			Device.WaitIdle();
			exposure.Dispose();
		}
	}

	/// <summary>A frame of grey <paramref name="luminance"/>, left in <see cref="ImageLayout.CopyDestination"/>.</summary>
	TextureHandle Upload( float luminance )
	{
		var pixels = new Half[Size * Size * 4];
		pixels.AsSpan().Fill( (Half)luminance );
		var color = Device.CreateTexture( TextureInfo.Texture2D( Size, Size, Format.RGBA16Float, name: "Frame" ) );
		var upload = Device.BeginCommandList( QueueType.Graphics, "Frame" );
		upload.Barrier( TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.None, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		upload.UpdateTexture( color, MemoryMarshal.AsBytes( pixels.AsSpan() ), 0, 0, Size, Size );
		Device.Wait( Device.Submit( upload ) );
		return color;
	}
}
