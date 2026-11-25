using Borealis.Source2;
using Borealis.Source2.Resources;
using Borealis.Source2.World;
using System;
using System.Collections.Generic;
using System.IO;
using N3 = System.Numerics.Vector3;

namespace BorealisTests;

/// <summary>
/// A compiled map's visibility as Borealis.Source2 reads it (<see cref="WorldVisibility"/>), for network culling.
/// </summary>
[TestClass]
public class WorldVisibilityTests
{
	/// <summary>Every core map that has visibility, read from its VPK.</summary>
	static IEnumerable<(string Map, int Version, WorldVisibility Visibility, byte[] Data)> CoreMaps()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is null ) yield break;

		foreach ( var file in Directory.EnumerateFiles( Path.Combine( core, "maps" ), "*.vpk", SearchOption.AllDirectories ) )
		{
			if ( file.EndsWith( "_dir.vpk", StringComparison.OrdinalIgnoreCase ) ) continue;

			var folder = "maps/" + Path.GetRelativePath( Path.Combine( core, "maps" ), Path.ChangeExtension( file, null ) ).Replace( '\\', '/' );
			using var package = VpkFile.Open( file );
			if ( package.Read( folder + "/world_visibility.vvis_c" ) is not { } data ) continue;
			yield return (folder, ResourceFile.Read( data ).ResourceVersion, WorldVisibility.Read( data ), data);
		}
	}

	/// <summary>
	/// Versions 7 and 8 are read; native gives any other an empty visibility that sees everything, and so does a null one.
	/// </summary>
	[TestMethod]
	public void CoreMapsReadTheirVisibility()
	{
		var read = 0;
		foreach ( var (map, version, visibility, _) in CoreMaps() )
		{
			Console.WriteLine( visibility is null ? $"{map}: version {version}, not read"
				: $"{map}: version {version}, {visibility.ClusterCount} clusters, {visibility.Mins} - {visibility.Maxs}, grid {visibility.GridSize}, {visibility.Size} bytes" );
			Assert.AreEqual( version is 7 or 8, visibility is not null, map );
			if ( visibility is null ) continue;

			Assert.IsTrue( visibility.ClusterCount >= 2, map );
			read++;
		}

		if ( read == 0 ) Assert.Inconclusive( "No core maps with visibility" );
	}

	/// <summary>
	/// Version 8 (what the vis builder writes now) holds the version 7 payload transposed in 8-byte lanes and zstd
	/// compressed in a VXVZ block. The same map either way gives the same answers.
	/// </summary>
	[TestMethod]
	public void CompressedVisibilityReadsAsUncompressed()
	{
		var (map, _, plain, data) = CoreMaps().Where( x => x.Version == 7 && x.Visibility is not null ).OrderByDescending( x => x.Visibility.ClusterCount ).FirstOrDefault();
		if ( plain is null ) Assert.Inconclusive( "No core map with version 7 visibility" );

		var compressed = WorldVisibility.Read( Version8( ResourceFile.Read( data ) ) );
		Assert.IsNotNull( compressed, map );
		Assert.AreEqual( plain.ClusterCount, compressed.ClusterCount );
		Assert.AreEqual( plain.Size, compressed.Size );
		Assert.AreEqual( plain.Mins, compressed.Mins );
		Assert.AreEqual( plain.Maxs, compressed.Maxs );

		var random = new Random( 8 );
		N3 Point() => plain.Mins + (plain.Maxs - plain.Mins) * new N3( random.NextSingle(), random.NextSingle(), random.NextSingle() );
		var sources = new N3[2];
		for ( var i = 0; i < 5000; i++ )
		{
			sources[0] = Point();
			sources[1] = Point();
			var point = Point();
			var extent = new N3( random.NextSingle() * 600 );
			Assert.AreEqual( plain.ClusterAt( point ), compressed.ClusterAt( point ) );
			Assert.AreEqual( plain.IsPointVisible( sources, point, N3.Zero ), compressed.IsPointVisible( sources, point, N3.Zero ) );
			Assert.AreEqual( plain.IsBoxVisible( sources, point - extent, point + extent, N3.Zero ), compressed.IsBoxVisible( sources, point - extent, point + extent, N3.Zero ) );
		}

		Console.WriteLine( $"{map}: {plain.ClusterCount} clusters" );
	}

	/// <summary>The server asks for every networked object and connection each tick: the queries allocate nothing.</summary>
	[TestMethod]
	public void QueriesDontAllocate()
	{
		var (map, _, visibility, _) = CoreMaps().Where( x => x.Visibility is { IsEmpty: false } ).OrderByDescending( x => x.Visibility.ClusterCount ).FirstOrDefault();
		if ( visibility is null ) Assert.Inconclusive( "No core map with clusters" );

		var random = new Random( 3 );
		var points = new N3[1024];
		for ( var i = 0; i < points.Length; i++ )
			points[i] = visibility.Mins + (visibility.Maxs - visibility.Mins) * new N3( random.NextSingle(), random.NextSingle(), random.NextSingle() );

		var sources = new N3[1];
		var seen = 0;
		void Run( int count )
		{
			for ( var i = 0; i < count; i++ )
			{
				sources[0] = points[i & 1023];
				var at = points[(i * 7 + 3) & 1023];
				if ( visibility.IsBoxVisible( sources, at - new N3( 32 ), at + new N3( 32 ), N3.Zero ) ) seen++;
				if ( visibility.IsPointVisible( sources, at, N3.Zero ) ) seen++;
			}
		}

		Run( 2000 );
		var before = GC.GetAllocatedBytesForCurrentThread();
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		Run( 100000 );
		var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime( start );
		Assert.AreEqual( 0, GC.GetAllocatedBytesForCurrentThread() - before, "bytes allocated" );
		Console.WriteLine( $"{map}: {elapsed.TotalMilliseconds * 1000 / 200000:F3} us a query ({seen} of 200000 seen)" );
	}

	/// <summary>A version 7 visibility rewritten as version 8, its payload in zstd raw blocks.</summary>
	static byte[] Version8( ResourceFile v7 )
	{
		var payload = v7.Block( "VXVS" ).ToArray();
		var lanes = payload.Length / 8;
		var transposed = new byte[payload.Length];
		for ( var lane = 0; lane < lanes; lane++ )
		{
			for ( var b = 0; b < 8; b++ )
				transposed[b * lanes + lane] = payload[lane * 8 + b];
		}

		payload.AsSpan( lanes * 8 ).CopyTo( transposed.AsSpan( lanes * 8 ) );

		// A zstd frame: single segment with an 8-byte content size, then raw blocks
		var frame = new List<byte>();
		frame.AddRange( BitConverter.GetBytes( 0xFD2FB528u ) );
		frame.Add( 0xE0 );
		frame.AddRange( BitConverter.GetBytes( (ulong)transposed.Length ) );
		for ( var start = 0; start < transposed.Length || start == 0; start += 1 << 17 )
		{
			var size = Math.Min( 1 << 17, transposed.Length - start );
			var last = start + size >= transposed.Length ? 1 : 0;
			var header = last | size << 3;
			frame.Add( (byte)header );
			frame.Add( (byte)(header >> 8) );
			frame.Add( (byte)(header >> 16) );
			frame.AddRange( transposed.AsSpan( start, size ).ToArray() );
		}

		var vxvz = new List<byte>();
		vxvz.AddRange( BitConverter.GetBytes( (uint)payload.Length ) );
		vxvz.AddRange( frame );
		return Resource( 8, ("DATA", v7.Block( "DATA" ).ToArray()), ("VXVZ", vxvz.ToArray()) );
	}

	/// <summary>A resource file (header version 12) holding these blocks.</summary>
	static byte[] Resource( int version, params (string Name, byte[] Data)[] blocks )
	{
		var table = 16;
		var start = table + 12 * blocks.Length;
		var output = new byte[start + blocks.Sum( x => x.Data.Length )];
		BitConverter.TryWriteBytes( output.AsSpan( 0 ), start );
		BitConverter.TryWriteBytes( output.AsSpan( 4 ), (ushort)12 );
		BitConverter.TryWriteBytes( output.AsSpan( 6 ), (ushort)version );
		BitConverter.TryWriteBytes( output.AsSpan( 8 ), table - 8 );
		BitConverter.TryWriteBytes( output.AsSpan( 12 ), blocks.Length );

		var at = start;
		for ( var i = 0; i < blocks.Length; i++ )
		{
			var entry = table + 12 * i;
			System.Text.Encoding.ASCII.GetBytes( blocks[i].Name ).CopyTo( output, entry );
			BitConverter.TryWriteBytes( output.AsSpan( entry + 4 ), at - (entry + 4) );
			BitConverter.TryWriteBytes( output.AsSpan( entry + 8 ), blocks[i].Data.Length );
			blocks[i].Data.CopyTo( output, at );
			at += blocks[i].Data.Length;
		}

		return output;
	}
}
