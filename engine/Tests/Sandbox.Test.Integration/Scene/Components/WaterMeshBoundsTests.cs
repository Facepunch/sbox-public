namespace SceneTests.Components;

/// <summary>
/// A mesh on the water's object sets its extent: the top is the surface, the footprint the size and the height the depth.
/// </summary>
[TestClass]
public class WaterMeshBoundsTests
{
	Surface _previousSurface;
	bool _installedSurface;

	[TestInitialize]
	public void Initialize()
	{
		if ( Surface.FindByName( "default" ) is not null ) return;

		Surface.All.TryGetValue( 0, out _previousSurface );
		var surface = new Surface();
		surface.RegisterWeakResourceId( "surfaces/default.surface" );
		Surface.All[0] = surface;
		_installedSurface = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( !_installedSurface ) return;
		if ( _previousSurface is not null )
			Surface.All[0] = _previousSurface;
		else
			Surface.All.Remove( 0 );
	}

	static PolygonMesh CreateBox( Vector3 mins, Vector3 maxs )
	{
		var mesh = new PolygonMesh();
		var v = new[]
		{
			mesh.AddVertex( new Vector3( mins.x, mins.y, mins.z ) ),
			mesh.AddVertex( new Vector3( maxs.x, mins.y, mins.z ) ),
			mesh.AddVertex( new Vector3( maxs.x, maxs.y, mins.z ) ),
			mesh.AddVertex( new Vector3( mins.x, maxs.y, mins.z ) ),
			mesh.AddVertex( new Vector3( mins.x, mins.y, maxs.z ) ),
			mesh.AddVertex( new Vector3( maxs.x, mins.y, maxs.z ) ),
			mesh.AddVertex( new Vector3( maxs.x, maxs.y, maxs.z ) ),
			mesh.AddVertex( new Vector3( mins.x, maxs.y, maxs.z ) )
		};
		mesh.AddFace( v[0], v[3], v[2], v[1] );
		mesh.AddFace( v[4], v[5], v[6], v[7] );
		if ( maxs.z > mins.z )
		{
			mesh.AddFace( v[0], v[1], v[5], v[4] );
			mesh.AddFace( v[1], v[2], v[6], v[5] );
			mesh.AddFace( v[2], v[3], v[7], v[6] );
			mesh.AddFace( v[3], v[0], v[4], v[7] );
		}
		return mesh;
	}

	/// <summary>
	/// An editable mesh drives the surface, volume and footprint, follows edits, and leaves depth to a flat mesh.
	/// </summary>
	[TestMethod]
	public void EditableMeshSetsTheExtent()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		try
		{
			var go = scene.CreateObject();
			go.WorldPosition = new Vector3( 100, 0, 50 );
			var mesh = go.Components.Create<MeshComponent>();
			mesh.Mesh = CreateBox( new Vector3( 40, -64, -96 ), new Vector3( 296, 64, -32 ) );
			var water = go.Components.Create<WaterBody>();
			scene.EditorTick( 1, 1f / 60 );

			Assert.IsTrue( water.UsesMeshBounds );
			Assert.IsTrue( water.UsesMeshDepth );
			Assert.AreEqual( new Vector2( 256, 128 ), water.Size );
			Assert.AreEqual( 64f, water.Depth );
			Assert.AreEqual( new Vector3( 268, 0, 18 ), water.SurfaceOrigin );
			Assert.IsTrue( water.Contains( new Vector3( 268, 0, 10 ) ), "Just below the mesh's top is water." );
			Assert.IsFalse( water.Contains( new Vector3( 268, 0, 26 ) ), "Above the mesh's top is dry." );
			Assert.IsTrue( water.Contains( new Vector3( 150, 60, -40 ) ), "The mesh's corner is water." );
			Assert.IsFalse( water.Contains( new Vector3( 120, 0, 10 ) ), "Past the mesh's footprint is dry." );
			Assert.IsFalse( water.Contains( new Vector3( 268, 0, -50 ) ), "Below the mesh's bottom is dry." );

			// Editing the mesh moves the water with it.
			mesh.Mesh = CreateBox( new Vector3( -32, -32, -16 ), new Vector3( 32, 32, 0 ) );
			scene.EditorTick( 2, 1f / 60 );
			scene.EditorTick( 3, 1f / 60 );
			Assert.AreEqual( new Vector2( 64, 64 ), water.Size );
			Assert.AreEqual( 16f, water.Depth );
			Assert.AreEqual( new Vector3( 100, 0, 50 ), water.SurfaceOrigin );

			// A flat mesh only outlines the surface; the depth stays authored.
			water.Depth = 300;
			mesh.Mesh = CreateBox( new Vector3( -100, -50, 20 ), new Vector3( 100, 50, 20 ) );
			scene.EditorTick( 4, 1f / 60 );
			scene.EditorTick( 5, 1f / 60 );
			Assert.IsTrue( water.UsesMeshBounds );
			Assert.IsFalse( water.UsesMeshDepth );
			Assert.AreEqual( 300f, water.Depth );
			Assert.AreEqual( new Vector3( 100, 0, 70 ), water.SurfaceOrigin );

			// Without the mesh the object's origin is the surface again.
			mesh.Destroy();
			scene.EditorTick( 6, 1f / 60 );
			Assert.IsFalse( water.UsesMeshBounds );
			Assert.AreEqual( go.WorldPosition, water.SurfaceOrigin );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
