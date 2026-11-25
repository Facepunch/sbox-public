using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nav.Build;

namespace Nav.Lib.Tests;

/// <summary>
/// Tests of stages and types only the C# library has (edge points, held spans, the apron field, volume marking,
/// thinning).
/// </summary>
public partial class GeneratedLinkTests
{
	// C8: what a tile keeps.
	[TestMethod]
	public void EdgePointIsEightBytesAndComesBackWithinAQuarterCell()
	{
		Assert.AreEqual( 8, System.Runtime.CompilerServices.Unsafe.SizeOf<EdgePoint>() );
		Assert.IsTrue( BuildConfig.TryCreate( Settings, out var config, out _ ) );
		var frame = config.Frame( -3, 2 ) with { MinZ = -400 };
		var random = new Random( 5 );
		for ( int i = 0; i < 200; i++ )
		{
			var edge = new Vector3( -3 * 512 + random.NextSingle() * 512, 2 * 512 + random.NextSingle() * 512, -400 + random.NextSingle() * 3000 );
			float angle = random.NextSingle() * MathF.Tau;
			var outward = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 );
			var point = EdgePoint.Pack( edge, outward, frame );
			point.Unpack( frame, out var back, out var normal );
			Assert.IsTrue( MathF.Abs( back.X - edge.X ) <= 0.5f && MathF.Abs( back.Y - edge.Y ) <= 0.5f && MathF.Abs( back.Z - edge.Z ) <= 2, $"{edge} came back as {back}" );
			Assert.IsTrue( Vector3.Dot( outward, normal ) > 0.99999f );
		}
	}

	[TestMethod]
	public void HeldSpansSayWhatTheSolidFieldSays()
	{
		var scene = Gap();
		var builder = new NavBuilder();
		Assert.IsTrue( BuildConfig.TryCreate( Settings, out var config, out _ ) );
		var frame = config.Frame( 0, 0 );
		Vector3[] vertices = scene.Vertices.ToArray();
		int[] indices = scene.Indices.ToArray();
		Assert.IsTrue( builder.Prepare( vertices, indices, float.NegativeInfinity, float.PositiveInfinity, config, ref frame, out _, out _ ) );
		builder.Rasterize( vertices, indices, float.NegativeInfinity, float.PositiveInfinity, config, frame );
		var held = HeldField.From( builder.Solid, frame, null );

		// Over the floor, in a side of the platform, above it, and outside the field.
		Assert.IsTrue( held.Free( new Vector3( 100, 100, 0 ), 18, 64 ) );
		Assert.IsFalse( held.Free( new Vector3( 100, 100, 0 ), -10, 64 ) );
		Assert.IsFalse( held.Free( new Vector3( 301, 240, 0 ), 18, 64 ) );
		Assert.IsTrue( held.Free( new Vector3( 400, 240, 0 ), 118, 164 ) );
		Assert.IsFalse( held.Free( new Vector3( 700, 240, 0 ), 118, 164 ) );
		Assert.IsTrue( held.Bytes < builder.Solid.Width * builder.Solid.Width * 2 + held.Spans.Length * 4 + 4096 );
	}

	[TestMethod]
	public void ApronHasTheSolidSpansAWiderFieldHasThere()
	{
		var scene = new Scene().Floor( 40, 40, 900, 470, 0 ).Box( 300, 160, 504, 320, 0, 100 ).Box( 600, 100, 610, 300, 0, 300 ).Box( 20, 20, 60, 480, 30, 90 );
		Vector3[] vertices = scene.Vertices.ToArray();
		int[] indices = scene.Indices.ToArray();
		const int cells = 11;

		// The field of tile 0 with a border wider by the apron.
		var wide = new NavBuilder();
		Assert.IsTrue( BuildConfig.TryCreate( Settings, out var config, out _ ) );
		var wideConfig = config;
		wideConfig.Border += cells;
		wideConfig.FieldSize += 2 * cells;
		var wideFrame = wideConfig.Frame( 0, 0 );
		Assert.IsTrue( wide.Prepare( vertices, indices, float.NegativeInfinity, float.PositiveInfinity, wideConfig, ref wideFrame, out _, out _ ) );
		wide.Rasterize( vertices, indices, float.NegativeInfinity, float.PositiveInfinity, wideConfig, wideFrame );

		var builder = new NavBuilder();
		builder.RasterizeApron( vertices, indices, float.NegativeInfinity, float.PositiveInfinity, config, 0, 0, wideFrame.MinZ, cells );
		int size = config.FieldSize + 2 * cells, inner = 0, ring = 0;
		Assert.AreEqual( wide.Solid.Width, size );
		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				int c = x + y * size;
				if ( x >= cells && y >= cells && x < size - cells && y < size - cells )
				{
					inner += builder.Apron.Counts[c];
					continue;
				}
				Assert.AreEqual( wide.Solid.Counts[c], builder.Apron.Counts[c], $"column {x},{y}" );
				for ( int i = 0; i < builder.Apron.Counts[c]; i++ ) Assert.AreEqual( wide.Solid.Spans[c * wide.Solid.Capacity + i], builder.Apron.Spans[c * builder.Apron.Capacity + i], $"column {x},{y}" );
				ring += builder.Apron.Counts[c];
			}
		}
		Assert.AreEqual( 0, inner );
		Assert.IsTrue( ring > 1000 );
	}

	[TestMethod]
	public void VolumeContainsWhatItMarks()
	{
		var box = new NavVolume { Shape = VolumeShape.Box, LocalToWorld = Matrix4x4.CreateRotationZ( MathF.PI / 4 ) * Matrix4x4.CreateTranslation( 100, 0, 0 ), A = new Vector3( -10, -40, 0 ), B = new Vector3( 10, 40, 20 ) };
		Assert.IsTrue( VolumeMarker.Contains( box, new Vector3( 100, 0, 10 ) ) );
		Assert.IsTrue( VolumeMarker.Contains( box, new Vector3( 80, 20, 10 ) ) );
		Assert.IsFalse( VolumeMarker.Contains( box, new Vector3( 120, 20, 10 ) ) );
		Assert.IsFalse( VolumeMarker.Contains( box, new Vector3( 100, 0, 30 ) ) );
		var ball = new NavVolume { Shape = VolumeShape.Sphere, LocalToWorld = Matrix4x4.Identity, A = new Vector3( 0, 0, 50 ), Radius = 10 };
		Assert.IsTrue( VolumeMarker.Contains( ball, new Vector3( 0, 9, 50 ) ) );
		Assert.IsFalse( VolumeMarker.Contains( ball, new Vector3( 8, 8, 50 ) ) );
	}

	[TestMethod]
	public void ThinningByCellsKeepsWhatComparingEveryPairKeeps()
	{
		var random = new Random( 7 );
		var list = new List<FoundLink>();
		for ( int i = 0; i < 2000; i++ )
		{
			var start = new Vector3( random.NextSingle() * 1500, random.NextSingle() * 1500, random.NextSingle() * 40 );
			var end = start + new Vector3( random.NextSingle() * 200 - 100, random.NextSingle() * 200 - 100, random.NextSingle() * 40 - 20 );
			var kind = (LinkKind)random.Next( 2 );
			list.Add( new FoundLink { Start = start, End = end, Kind = kind, Bidirectional = kind == LinkKind.Jump && random.Next( 2 ) == 0 } );
		}

		// The pairwise rule, on the same order: both ways first, then by length.
		var sorted = list.OrderBy( c => !c.Bidirectional ).ThenBy( c => Vector3.DistanceSquared( c.Start, c.End ) ).ToList();
		var expected = new List<FoundLink>();
		foreach ( var c in sorted )
		{
			if ( !expected.Any( o => LinkBuilder.Same( o, c, 64 ) ) ) expected.Add( c );
		}

		new LinkBuilder().Thin( list, 64 );
		Assert.IsTrue( expected.Count < 2000 );
		Assert.AreEqual( expected.Count, list.Count );
		var kept = list.Select( c => (c.Start, c.End) ).ToHashSet();
		Assert.IsTrue( expected.All( c => kept.Contains( (c.Start, c.End) ) ) );
	}
}
