using System;
using System.Collections.Generic;
using static Sandbox.TerrainClipmap;

namespace SceneTests;

[TestClass]
public class TerrainClipmapCullerTest
{
	const int Levels = 7;
	const int ExtentCells = 256;
	const int BlockSize = 4;
	const float UnitsPerTexel = 48.828125f;
	const float HeightScale = 20000.0f;

	/// <summary>
	/// Culling by ring and group must keep exactly the blocks a per-block test in terrain space keeps. A wrong group
	/// bound would drop an on-screen block (a hole) or keep an off-screen one.
	/// </summary>
	[TestMethod]
	public void MatchesPerBlockCulling()
	{
		Span<Plane> planes = stackalloc Plane[6];
		var layout = BuildLayout( Levels, ExtentCells, BlockSize );

		var transforms = new[]
		{
			Transform.Zero,
			new Transform( new Vector3( 1000, -2000, 300 ), Rotation.Identity, 1.5f ),
			new Transform( new Vector3( 500, 700, -100 ), Rotation.FromYaw( 30 ), 0.75f ),
		};

		foreach ( var transform in transforms )
		{
			var culler = new TerrainClipmapCuller( layout, BlockSize );
			var visible = new Meshlet[culler.Meshlets.Length];

			Assert.IsTrue( layout.Select( Key ).ToHashSet().SetEquals( culler.Meshlets.Select( Key ) ), "The reordered layout has every meshlet" );

			foreach ( var (frustum, camera) in Views( transform ) )
			{
				TerrainClipmapCuller.ToLocalPlanes( frustum, transform, planes );
				var local = new Frustum( planes[1], planes[0], planes[2], planes[3], planes[4], planes[5] );

				int count = culler.Cull( planes, camera, UnitsPerTexel, HeightScale, visible );

				var expected = culler.Meshlets.Where( m => local.IsInside( LocalBounds( m, camera ), partially: true ) ).Select( Key ).ToHashSet();
				var actual = visible.Take( count ).Select( Key ).ToHashSet();

				Assert.AreEqual( count, actual.Count, "No meshlet is emitted twice" );
				Assert.IsTrue( expected.SetEquals( actual ), $"Culled set differs from per-block culling: {expected.Except( actual ).Count()} missing, {actual.Except( expected ).Count()} extra" );
			}
		}
	}

	static (int Level, Vector2 Offset) Key( Meshlet m ) => (m.Level, m.BlockOffset);

	static IEnumerable<(Frustum Frustum, Vector2 Camera)> Views( Transform transform )
	{
		var random = new Random( 1 );
		float Range( float min, float max ) => min + (float)random.NextDouble() * (max - min);

		for ( int i = 0; i < 64; i++ )
		{
			var local = new Vector3( Range( -200000, 200000 ), Range( -200000, 200000 ), Range( 0, 25000 ) );
			var position = transform.PointToWorld( local );
			var rotation = Rotation.From( Range( -70, 20 ), Range( 0, 360 ), 0 );

			// 90 degree horizontal fov at 16:9
			var forward = rotation.Forward;
			var left = rotation.Left;
			var up = rotation.Up * (9.0f / 16.0f);

			var frustum = Frustum.FromCorners(
				new Ray( position, (forward + left + up).Normal ),
				new Ray( position, (forward - left + up).Normal ),
				new Ray( position, (forward - left - up).Normal ),
				new Ray( position, (forward + left - up).Normal ),
				1.0f, 500000.0f );

			yield return (frustum, new Vector2( local.x, local.y ));
		}
	}

	// The bounds the scene object tested per block before this culler: the block's local box, grown by the ring's snap increment
	static BBox LocalBounds( Meshlet m, Vector2 camera )
	{
		float vertexStep = UnitsPerTexel * (1 << m.Level);
		float increment = vertexStep * 2.0f;
		var center = camera.SnapToGrid( increment );

		float ox = center.x + m.BlockOffset.x * vertexStep;
		float oy = center.y + m.BlockOffset.y * vertexStep;
		float ext = BlockSize * vertexStep;

		return new BBox(
			new Vector3( ox - increment, oy - increment, -increment ),
			new Vector3( ox + ext + increment, oy + ext + increment, HeightScale + increment ) );
	}
}
