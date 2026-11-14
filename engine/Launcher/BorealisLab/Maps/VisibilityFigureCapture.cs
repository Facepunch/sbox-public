using Borealis.Backend;
using Borealis.Gpu;
using System.IO;
using System.Numerics;
using V3 = System.Numerics.Vector3;
using System.Text;
using Capture = BorealisGpuSample.Capture;
using GpuDevice = Borealis.Gpu.Device;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// <c>-map ident -real -msaa 1 -capture-visibility dir</c>: one frame of a view, GPU-driven, drawn twice: the visibility
/// pass off (every triangle reaches forward) and on. Writes the ids target, the forward pass's fragments per pixel each
/// way on one scale, its triangles each way, the shaded frame, and the frame's counts (debug figure for the status page).
/// </summary>
internal static class VisibilityFigureCapture
{
	public static void Save( GpuDevice gpu, MapWorld map, VisibilityFigure figure, MapView view, Action<MapView> render, string folder )
	{
		folder = Path.GetFullPath( folder );
		Directory.CreateDirectory( folder );
		map.System.ParallelRecording = false;
		map.Backend.MaterialObjects = true;
		map.Backend.GpuDriven = true;
		map.Backend.GpuDrivenCulling = true;
		map.Backend.GpuDrivenCompaction = true;
		map.SetCastShadows( false );

		// A frame with the lists off, then on, each after warming up; the figure keeps the last
		(float[] Count, byte[] Colour, (long Commands, long Triangles, long Listed, long Compacted) Counts) Frame( bool visibility )
		{
			map.Backend.GpuDrivenVisibility = visibility;
			figure.DepthTestedTriangles = visibility;
			for ( int i = 0; i < 4; i++ )
			{
				figure.Reset();
				render( view );
			}

			gpu.WaitIdle();
			var counts = figure.Counts();
			var raw = Capture.Read( gpu, figure.Count, ImageLayout.ColorTarget );
			var count = new float[raw.Length / 2];
			for ( int i = 0; i < count.Length; i++ ) count[i] = (float)BitConverter.ToHalf( raw, i * 2 );
			var colour = Capture.Read( gpu, figure.Colour, ImageLayout.ColorTarget );
			return (count, colour, counts);
		}

		var info = gpu.InfoOf( figure.Count );
		var (width, height) = (info.Width, info.Height);
		var before = Frame( false );
		var after = Frame( true );

		// The ids and marked bits of the frame with the lists on
		var (ids, mask, aggregateBits, meshBits) = map.Backend.FigureVisibility;
		var idPixels = Capture.Read( gpu, ids, ImageLayout.ShaderRead );
		var maskBits = aggregateBits + meshBits;
		var marked = MarkedBits( gpu, mask, 0, aggregateBits ) + MarkedBits( gpu, mask, BackendDevice.FigureFrameBitBase, meshBits );

		// The shaded frame with the lists on
		var (captured, capturedLayout) = map.Output;
		Capture.Save( gpu, captured, capturedLayout, Path.Combine( folder, "final.png" ) );
		BackendDevice.SetLayout( map.Target.Samples > 1 ? map.Target.Resolved : map.Target.Color, ImageLayout.CopySource );

		// One scale for both heat maps: the most fragments any pixel had without the lists
		var max = Math.Max( 1, before.Count.Max() );
		Write( folder, "overdraw-before.png", Heat( before.Count, max ), width, height );
		Write( folder, "overdraw-after.png", Heat( after.Count, max ), width, height );
		Write( folder, "all-triangles.png", before.Colour, width, height );
		Write( folder, "visible-triangles.png", after.Colour, width, height );
		Write( folder, "ids.png", Ids( idPixels ), width, height );

		var distinct = new HashSet<uint>();
		for ( int i = 0; i + 3 < idPixels.Length; i += 4 )
		{
			var id = BitConverter.ToUInt32( idPixels, i );
			if ( id != 0 ) distinct.Add( id );
		}

		static double Mean( float[] values ) => values.Average( x => (double)x );
		var text = new StringBuilder();
		text.AppendLine( $"{view.Name}, {width}x{height}, 1 sample, GPU-driven forward passes of the main view and its 3D skybox (aggregates and static meshes); classic draws not counted; ids of the main view" );
		text.AppendLine( $"heat map scale: 0 to {max:0} fragments a pixel (the most without the lists), log ramp" );
		text.AppendLine();
		text.AppendLine( "lists off (every triangle the culling keeps reaches forward):" );
		text.AppendLine( $"  forward commands {before.Counts.Commands}, triangles {before.Counts.Triangles}, fragments rasterised {before.Count.Sum( x => (double)x ):0} ({Mean( before.Count ):0.00} a pixel)" );
		text.AppendLine( "lists on:" );
		text.AppendLine( $"  forward commands {after.Counts.Commands}, triangles {after.Counts.Triangles}, fragments rasterised {after.Count.Sum( x => (double)x ):0} ({Mean( after.Count ):0.00} a pixel)" );
		text.AppendLine( $"  fragments listed for compaction {after.Counts.Listed}, compacted triangles {after.Counts.Compacted}" );
		text.AppendLine( $"  ids: {distinct.Count} distinct triangle ids on screen; vertex bits marked {marked} of {maskBits}" );
		File.WriteAllText( Path.Combine( folder, "counts.txt" ), text.ToString() );
		Console.Write( text );
		Console.WriteLine( $"Visibility figure in {folder}" );
	}

	static void Write( string folder, string name, byte[] rgba, int width, int height ) => File.WriteAllBytes( Path.Combine( folder, name ), Capture.Png( rgba, width, height ) );

	/// <summary>Fragments a pixel on a log ramp from the dark background through blue, magenta and orange to pale yellow.</summary>
	static byte[] Heat( float[] counts, float max )
	{
		V3[] ramp = [new V3( 0.03f, 0.03f, 0.05f ), new( 0.10f, 0.12f, 0.45f ), new( 0.55f, 0.15f, 0.60f ), new( 0.95f, 0.40f, 0.20f ), new( 1.00f, 0.85f, 0.35f ), new( 1.00f, 1.00f, 0.90f )];
		var pixels = new byte[counts.Length * 4];
		var top = MathF.Log2( 1 + max );
		for ( int i = 0; i < counts.Length; i++ )
		{
			var t = counts[i] <= 0 ? 0 : Math.Clamp( MathF.Log2( 1 + counts[i] ) / top, 0, 1 );
			var at = t * (ramp.Length - 1);
			var k = Math.Min( (int)at, ramp.Length - 2 );
			var c = V3.Lerp( ramp[k], ramp[k + 1], at - k );
			pixels[i * 4] = (byte)(c.X * 255);
			pixels[i * 4 + 1] = (byte)(c.Y * 255);
			pixels[i * 4 + 2] = (byte)(c.Z * 255);
			pixels[i * 4 + 3] = 255;
		}

		return pixels;
	}

	/// <summary>Each id hashed to a colour; no id dark.</summary>
	static byte[] Ids( byte[] ids )
	{
		var pixels = new byte[ids.Length];
		for ( int i = 0; i + 3 < ids.Length; i += 4 )
		{
			var id = BitConverter.ToUInt32( ids, i );
			if ( id == 0 )
			{
				(pixels[i], pixels[i + 1], pixels[i + 2]) = (8, 8, 12);
			}
			else
			{
				var h = id * 0x9E3779B1u;
				h ^= h >> 15;
				h *= 0x85EBCA77u;
				h ^= h >> 13;
				pixels[i] = (byte)(60 + (h & 0xff) * 195 / 255);
				pixels[i + 1] = (byte)(60 + (h >> 8 & 0xff) * 195 / 255);
				pixels[i + 2] = (byte)(60 + (h >> 16 & 0xff) * 195 / 255);
			}

			pixels[i + 3] = 255;
		}

		return pixels;
	}

	/// <summary>Bits set in <paramref name="bits"/> of the mask from bit <paramref name="first"/> (a multiple of 32).</summary>
	static long MarkedBits( GpuDevice gpu, BufferHandle mask, long first, long bits )
	{
		if ( bits <= 0 ) return 0;
		var words = (bits + 31) / 32;
		var readback = gpu.CreateBuffer( new BufferInfo { Size = words * 4, Memory = MemoryLocation.Readback, Name = "Figure mask" } );
		var list = gpu.BeginCommandList( QueueType.Graphics, "Figure mask" );
		list.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead );
		list.CopyBuffer( mask, first / 8, readback, 0, words * 4 );
		list.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		gpu.Wait( gpu.Submit( list ) );
		long marked = 0;
		foreach ( var word in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( gpu.Map( readback ) ) ) marked += BitOperations.PopCount( word );
		gpu.Destroy( readback );
		return marked;
	}
}
