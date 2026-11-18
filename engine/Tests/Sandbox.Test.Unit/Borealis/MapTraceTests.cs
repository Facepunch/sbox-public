using Sandbox;
using System;
using System.Collections.Generic;
using N3 = System.Numerics.Vector3;

namespace BorealisTests;

/// <summary>
/// The render-mesh traces of maps Borealis.Source2 reads (<see cref="MapTrace"/>): a model's triangles in a hierarchy,
/// hit as native's ray tracer hits them (nearest along the segment, its cull modes and barycentrics).
/// </summary>
[TestClass]
public class MapTraceTests
{
	/// <summary>A floor quad at z = 0, two triangles wound to face up (+z), and a second quad above it at z = 10.</summary>
	static MapTrace.ModelTriangles Floors() => MapTrace.ModelTriangles.FromCorners(
	[
		new( 0, 0, 0 ), new( 100, 0, 0 ), new( 100, 100, 0 ),
		new( 0, 0, 0 ), new( 100, 100, 0 ), new( 0, 100, 0 ),
		new( 0, 0, 10 ), new( 100, 0, 10 ), new( 100, 100, 10 ),
		new( 0, 0, 10 ), new( 100, 100, 10 ), new( 0, 100, 10 ),
	], [0, 0, 1, 1], ["floor.vmat", "shelf.vmat"] );

	[TestMethod]
	public void NearestHitIsTheFirstAlongTheRay()
	{
		var model = Floors();
		var t = 1f;
		Assert.IsTrue( model.Nearest( new N3( 25, 75, 20 ), new N3( 0, 0, -40 ), 0, ref t, out var triangle, out var u, out var v ) );
		Assert.AreEqual( 0.25f, t, 1e-5f, "the shelf at z 10, a quarter of the way down" );
		Assert.AreEqual( "shelf.vmat", model.MaterialOf( triangle ) );

		// Native's barycentrics: the hit is c + (a - c) u + (b - c) v
		var (a, b, c) = model.Corners( triangle );
		var at = c + (a - c) * u + (b - c) * v;
		Assert.AreEqual( 25, at.X, 1e-3f );
		Assert.AreEqual( 75, at.Y, 1e-3f );

		// Nothing nearer than an earlier hit
		t = 0.1f;
		Assert.IsFalse( model.Nearest( new N3( 25, 75, 20 ), new N3( 0, 0, -40 ), 0, ref t, out _, out _, out _ ) );
	}

	[TestMethod]
	public void CullModesKeepFrontOrBackFaces()
	{
		var model = Floors();

		// Down onto the up-facing floors hits front faces: cull mode 2 keeps them, 1 drops them
		var t = 1f;
		Assert.IsTrue( model.Nearest( new N3( 50, 25, 20 ), new N3( 0, 0, -40 ), 2, ref t, out _, out _, out _ ) );
		t = 1f;
		Assert.IsFalse( model.Nearest( new N3( 50, 25, 20 ), new N3( 0, 0, -40 ), 1, ref t, out _, out _, out _ ) );

		// Up from below hits back faces
		t = 1f;
		Assert.IsTrue( model.Nearest( new N3( 50, 25, -20 ), new N3( 0, 0, 40 ), 1, ref t, out _, out _, out _ ) );
		Assert.AreEqual( 0.5f, t, 1e-5f );
	}

	[TestMethod]
	public void AllHitsAlongTheRay()
	{
		var hits = new List<float>();
		Floors().All( new N3( 60, 30, 20 ), new N3( 0, 0, -40 ), 0, ( _, t, _, _ ) => hits.Add( t ) );
		hits.Sort();
		CollectionAssert.AreEqual( new[] { 0.25f, 0.5f }, hits );
	}

	/// <summary>The hierarchy finds what testing every triangle finds, for rays through a cloud of small triangles.</summary>
	[TestMethod]
	public void HierarchyMatchesTestingEveryTriangle()
	{
		var random = new Random( 7 );
		float R( float scale ) => (float)random.NextDouble() * scale;
		var corners = new N3[3000 * 3];
		for ( int i = 0; i < 3000; i++ )
		{
			var centre = new N3( R( 1000 ), R( 1000 ), R( 1000 ) );
			for ( int k = 0; k < 3; k++ ) corners[i * 3 + k] = centre + new N3( R( 40 ) - 20, R( 40 ) - 20, R( 40 ) - 20 );
		}

		var model = MapTrace.ModelTriangles.FromCorners( corners, new int[3000], ["m"] );

		for ( int r = 0; r < 200; r++ )
		{
			var origin = new N3( R( 1000 ), R( 1000 ), -100 );
			var delta = new N3( R( 200 ) - 100, R( 200 ) - 100, 1200 );

			var t = 1f;
			var hit = model.Nearest( origin, delta, 0, ref t, out _, out _, out _ );

			var bestT = 1f;
			var best = -1;
			for ( int i = 0; i < model.Count; i++ )
			{
				if ( model.Intersect( i, origin, delta, 0, out var ti, out _, out _ ) && ti < bestT )
				{
					bestT = ti;
					best = i;
				}
			}

			Assert.AreEqual( best >= 0, hit, $"ray {r}" );
			if ( hit ) Assert.AreEqual( bestT, t, 1e-5f, $"ray {r}" );
		}
	}
}
