namespace SceneTests;

/// <summary>
/// A scene object's draws worked out in managed code (<c>SceneObject.GetDraws</c>: body groups, material groups, whole, per
/// index and attribute material overrides, each draw's translucency and shadow fast path) are native's
/// (<c>RenderTools::GetSceneObjectDraw</c>) after every change, and so are its flags and bounds.
/// </summary>
[TestClass]
public class SceneObjectDrawTests
{
	static readonly System.Reflection.MethodInfo Compare = System.Type.GetType( "Sandbox.Rendering.Bridge.SceneNative, Sandbox.Rendering", true ).GetMethod( "CompareDraws" );

	static void Same( SceneObject so, string after )
	{
		Assert.IsTrue( so.ResolvesDraws, "made by managed code" );
		var differences = (System.Collections.Generic.List<string>)Compare.Invoke( null, [so] );
		Assert.AreEqual( 0, differences.Count, $"after {after}:\n{string.Join( "\n", differences )}" );
		Assert.AreEqual( "", so.CompareFlags(), $"flags after {after}" );
		SameBounds( so, after );
	}

	/// <summary>Bounds kept in managed code are native's, give or take native's quantising to whole units.</summary>
	static void SameBounds( SceneObject so, string after )
	{
		var mine = so.WorldBounds;
		var native = so.native.GetBounds();
		Assert.IsTrue( mine.Mins.Distance( native.Mins ) <= 1.8f && mine.Maxs.Distance( native.Maxs ) <= 1.8f, $"bounds after {after}: native {native}, managed {mine}" );
	}

	[TestMethod]
	public void DrawsAreNativesAfterEveryChange()
	{
		var world = new SceneWorld();
		try
		{
			var human = Model.Load( "models/citizen_human/human.vmdl" );
			var white = Material.Load( "materials/default/white.vmat" );
			var glass = Material.Load( "materials/dev/debug_ghost_translucent.vmat" );

			foreach ( var so in new SceneObject[] { new SceneObject( world, human ), new SceneModel( world, human, Transform.Zero ) } )
			{
				Same( so, "creation" );

				so.MeshGroupMask = 1;
				Same( so, "mesh group mask 1" );
				so.MeshGroupMask = ulong.MaxValue;
				Same( so, "every mesh group" );

				so.SetMaterialOverride( white, "skin" );
				Same( so, "a skin override" );
				so.SetMaterialOverride( glass, "skin" );
				Same( so, "a translucent skin override" );
				so.SetMaterialOverride( glass, "eyes", 1 );
				Same( so, "an eyes override" );
				so.SetMaterialOverride( null, "skin" );
				Same( so, "the skin override removed" );

				for ( int g = 0; g < human.MaterialGroupCount; g++ )
				{
					so.SetMaterialGroup( human.GetMaterialGroupName( g ) );
					Same( so, $"material group {g}" );
				}

				so.SetMaterialGroup( "no such group" );
				Same( so, "a missing material group" );

				so.SetMaterialOverride( glass );
				Same( so, "a whole translucent override" );
				so.SetMaterialGroup( human.MaterialGroupCount > 0 ? human.GetMaterialGroupName( 0 ) : "default" );
				Same( so, "a material group after a whole override" );
				so.SetMaterialOverride( white );
				so.SetMaterialOverride( null );
				Same( so, "the whole override removed" );

				so.ClearMaterialOverride();
				Same( so, "overrides cleared" );

				so.SetMaterialOverrideByIndex( 0, glass );
				Same( so, "a translucent index override" );
				so.SetMaterialOverride( white, "skin" );
				so.SetMaterialOverrideByIndex( 1, white );
				Same( so, "an index override over an attribute override" );
				so.ClearMaterialOverrideList();
				Same( so, "the override list cleared, index overrides kept" );
				so.SetMaterialOverrideByIndex( 0, null );
				so.SetMaterialOverrideByIndex( 1, null );
				Same( so, "index overrides removed" );

				so.Flags.CastShadows = false;
				so.Flags.IsTranslucent = true;
				Same( so, "flags set" );
				so.UpdateFlagsBasedOnMaterial();
				Same( so, "flags worked out from materials" );
				so.Flags.CastShadows = true;

				so.Transform = new Transform( new Vector3( 100.3f, -20.6f, 5.2f ), Rotation.From( 30, 45, 10 ), 1.5f );
				Same( so, "a transform" );
				so.MeshGroupMask = 1;
				so.Position += new Vector3( 7.7f, 0, 0 );
				Same( so, "moved with one mesh group" );
				so.MeshGroupMask = ulong.MaxValue;
				so.Bounds = new BBox( new Vector3( -10.5f ), new Vector3( 20.25f ) );
				Same( so, "bounds set" );
				so.RenderingEnabled = false;
				so.SetMaterialOverride( glass );
				Same( so, "a whole override while hidden" );
				so.SetMaterialOverride( null );
				so.RenderingEnabled = true;

				so.SetMaterialOverride( white, "skin" );
				so.Model = Model.Load( "models/dev/box.vmdl" );
				Same( so, "a new model" );
				so.Model = human;
				Same( so, "the first model again" );
			}

			// Native works a shared mesh's flags out again in place when an object drawing it without draw data of its own changes
			// material, for every object drawing it
			var a = new SceneObject( world, human );
			var b = new SceneObject( world, human );
			b.SetMaterialOverride( glass, "skin" );
			Same( a, "another object's attribute override" );
			Same( b, "its attribute override" );
			a.SetMaterialOverride( white );
			b.SetMaterialOverride( null, "skin" );
			Same( a, "its whole override, and another's override removed" );
			Same( b, "its override removed" );

			// A wholly translucent model, and a model changed to one
			var ghost = new SceneObject( world, Model.Load( "models/dev/box.vmdl" ) );
			ghost.SetMaterialOverride( glass );
			Same( ghost, "a box with a whole translucent mesh-instance override" );
			ghost.Model = human;
			Same( ghost, "a new model after a translucent override" );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// A model reloading gives every object drawing it new mesh instances natively (<c>CModelTypeManager::FinalizeResource</c>
	/// calls <c>CMeshSystem::ChangeModel</c>), which drop mesh-instance overrides and work the flags out from the model again:
	/// the objects' managed draws and flags follow (a game's resident resources reload while its first scene loads).
	/// </summary>
	[TestMethod]
	public void DrawsAreNativesAfterTheirModelReloads()
	{
		var world = new SceneWorld();
		try
		{
			var human = Model.Load( "models/citizen_human/human.vmdl" );
			var white = Material.Load( "materials/default/white.vmat" );
			var glass = Material.Load( "materials/dev/debug_ghost_translucent.vmat" );

			var objects = new SceneObject[] { new SceneObject( world, human ), new SceneModel( world, human, Transform.Zero ) };
			foreach ( var so in objects )
			{
				so.SetMaterialOverride( glass );
				so.SetMaterialOverride( white, "skin" );
				Same( so, "a whole translucent override and a skin override" );
			}

			// What native's reload does to every object drawing the model, then what Model.OnReloaded does for its scene objects
			// (the reload itself isn't sent: it drops the shared model's physics and other cached data that later tests use)
			foreach ( var so in objects ) NativeEngine.MeshSystem.ChangeModel( so, human.native );
			SceneObject.ModelReloaded( human );

			foreach ( var so in objects )
			{
				Same( so, "the model reloaded" );
				so.SetMaterialOverride( glass, "skin" );
				Same( so, "a translucent skin override after the reload" );
			}
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// Body groups set on a <see cref="SceneModel"/> give the mesh group mask native's <c>CSceneObject::SetBodyGroup</c> gives,
	/// every choice of every part, past the last choice and a missing part included.
	/// </summary>
	[TestMethod]
	public void BodyGroupsAreNatives()
	{
		var world = new SceneWorld();
		try
		{
			var human = Model.Load( "models/citizen_human/human.vmdl" );
			Assert.IsTrue( human.Parts.Count > 0, "the model has body parts" );
			var managed = new SceneModel( world, human, Transform.Zero );
			var reference = new SceneModel( world, human, Transform.Zero );

			void Set( string part, int value )
			{
				managed.SetBodyGroup( part, value );
				reference.native.SetBodyGroup( part, value );
				Assert.AreEqual( reference.native.GetCurrentMeshGroupMask(), managed.MeshGroupMask, $"{part} = {value}" );
				Same( managed, $"body group {part} = {value}" );
			}

			foreach ( var part in human.Parts.All )
			{
				for ( int value = part.Choices.Count; value >= 0; value-- ) Set( part.Name, value );
			}

			Set( "no such part", 1 );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// Without Source 2's device, native keeps the translucency a mesh was made with; a device works it out from the mesh's
	/// materials. Managed code works it out from the materials then: a model of an opaque and a translucent mesh is both
	/// opaque and translucent, a translucent one alone translucent, as with a device; native's object gets the same flags.
	/// </summary>
	[TestMethod]
	public void TranslucencyComesFromTheMaterialsWithoutADevice()
	{

		static Mesh Quad( Material material )
		{
			var mesh = new Mesh( material );
			var vertices = new System.Collections.Generic.List<Vertex>
			{
				new( new Vector3( 0, -32, -32 ), Vector3.Backward, Vector3.Up, new Vector4( 0, 0, 0, 0 ) ),
				new( new Vector3( 0, 32, -32 ), Vector3.Backward, Vector3.Up, new Vector4( 1, 0, 0, 0 ) ),
				new( new Vector3( 0, 0, 32 ), Vector3.Backward, Vector3.Up, new Vector4( 0.5f, 1, 0, 0 ) ),
			};
			mesh.CreateVertexBuffer( vertices.Count, vertices );
			mesh.CreateIndexBuffer( 3, new System.Collections.Generic.List<int> { 0, 2, 1 } );
			mesh.Bounds = new BBox( new Vector3( -1, -32, -32 ), new Vector3( 1, 32, 32 ) );
			return mesh;
		}

		var world = new SceneWorld();
		try
		{
			var white = Material.Load( "materials/default/white.vmat" );
			// The human's eyelashes: its translucent material
			var glass = Material.Load( "models/citizen_human/heads/citizen_human_femalehead01/eyelashes_01_opacity.vmat" );
			Assert.IsTrue( glass.Flags.IsTranslucent && !white.Flags.IsTranslucent, $"glass {glass.Flags.IsTranslucent}, white {white.Flags.IsTranslucent}" );

			var mixed = new SceneObject( world, Model.Builder.AddMesh( Quad( white ) ).AddMesh( Quad( glass ) ).Create() );
			Assert.IsTrue( mixed.Flags.IsOpaque && mixed.Flags.IsTranslucent, $"opaque and translucent meshes: {mixed.CurrentFlags}" );
			Assert.AreEqual( "", mixed.CompareFlags(), "native's twin" );

			var ghost = new SceneObject( world, Model.Builder.AddMesh( Quad( glass ) ).Create() );
			Assert.IsTrue( !ghost.Flags.IsOpaque && ghost.Flags.IsTranslucent, $"a translucent mesh: {ghost.CurrentFlags}" );

			var solid = new SceneObject( world, Model.Builder.AddMesh( Quad( white ) ).Create() );
			Assert.IsTrue( solid.Flags.IsOpaque && !solid.Flags.IsTranslucent, $"an opaque mesh: {solid.CurrentFlags}" );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// A map's model (whose draws native's stand for) keeps native's flags, which without Source 2's device carry the meshes'
	/// compiled translucency: they're worked out from the materials too. Construct's darkroom doors, glass panes in a frame,
	/// drew no glass.
	/// </summary>
	[TestMethod]
	public void MapModelsTranslucencyComesFromTheMaterialsWithoutADevice()
	{

		static Mesh Quad( Material material )
		{
			var mesh = new Mesh( material );
			var vertices = new System.Collections.Generic.List<Vertex>
			{
				new( new Vector3( 0, -32, -32 ), Vector3.Backward, Vector3.Up, new Vector4( 0, 0, 0, 0 ) ),
				new( new Vector3( 0, 32, -32 ), Vector3.Backward, Vector3.Up, new Vector4( 1, 0, 0, 0 ) ),
				new( new Vector3( 0, 0, 32 ), Vector3.Backward, Vector3.Up, new Vector4( 0.5f, 1, 0, 0 ) ),
			};
			mesh.CreateVertexBuffer( vertices.Count, vertices );
			mesh.CreateIndexBuffer( 3, new System.Collections.Generic.List<int> { 0, 2, 1 } );
			mesh.Bounds = new BBox( new Vector3( -1, -32, -32 ), new Vector3( 1, 32, 32 ) );
			return mesh;
		}

		var world = new SceneWorld();
		try
		{
			var white = Material.Load( "materials/default/white.vmat" );
			var glass = Material.Load( "models/citizen_human/heads/citizen_human_femalehead01/eyelashes_01_opacity.vmat" );

			var door = new SceneModel( world, Model.Builder.WithName( "maps/test/entities/door.vmdl" ).AddMesh( Quad( white ) ).AddMesh( Quad( glass ) ).Create(), Transform.Zero );
			Assert.IsFalse( door.ResolvesDraws, $"a map's model: {door.Model.ResourcePath}" );
			Assert.IsTrue( door.Flags.IsOpaque && door.Flags.IsTranslucent, $"opaque and translucent meshes: {door.CurrentFlags}" );
			Assert.AreEqual( "", door.CompareFlags(), "native's twin" );

			var pane = new SceneModel( world, Model.Builder.WithName( "maps/test/entities/pane.vmdl" ).AddMesh( Quad( glass ) ).Create(), Transform.Zero );
			Assert.IsTrue( !pane.Flags.IsOpaque && pane.Flags.IsTranslucent, $"a translucent mesh: {pane.CurrentFlags}" );
			Assert.AreEqual( "", pane.CompareFlags(), "native's twin" );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>Custom and dynamic objects keep their bounds in managed code: infinite until set, and not moved by their transform.</summary>
	[TestMethod]
	public void CustomBoundsAreNatives()
	{
		var world = new SceneWorld();
		try
		{
			foreach ( var so in new SceneObject[] { new SceneCustomObject( world ), new SceneDynamicObject( world ) } )
			{
				Assert.AreEqual( so.native.GetBounds(), so.WorldBounds, "infinite at first" );
				so.Transform = new Transform( new Vector3( 50, 60, 70 ) );
				Assert.AreEqual( so.native.GetBounds(), so.WorldBounds, "after a transform" );
				so.Bounds = new BBox( new Vector3( -3.5f, 2.25f, 0 ), new Vector3( 4.1f, 9, 12.5f ) );
				SameBounds( so, "bounds set" );
				Assert.AreEqual( so.native.GetBounds(), so.WorldBounds, "quantised as native's" );
				so.Bounds = new BBox( new Vector3( float.NaN ), new Vector3( 1 ) );
				Assert.AreEqual( so.native.GetBounds(), so.WorldBounds, "invalid bounds ignored" );
			}
		}
		finally
		{
			world.Delete();
		}
	}
}
