namespace SceneTests;

/// <summary>
/// A scene object's draws worked out in managed code (<c>SceneObject.GetDraws</c>: body groups, material groups, whole, per
/// index and attribute material overrides, each draw's translucency and shadow fast path) are native's
/// (<c>RenderTools::GetSceneObjectDraw</c>) after every change, and so are its flags and bounds: checked against native's
/// values (<see cref="NativeGolden"/>).
/// </summary>
[TestClass]
public class SceneObjectDrawTests
{
	/// <summary>
	/// The object's draws and flags are native's, and its bounds too, give or take native's quantising to whole units. A scene
	/// model's bounds are its pose's (<c>SceneModelAnimationInstanceTests</c>).
	/// </summary>
	static void Same( SceneObject so, string after )
	{
		Assert.IsTrue( so.ResolvesDraws, "made by managed code" );
		var key = $"draws/{test}/{Index( so )}/{after}";
		NativeGolden.CheckDraws( key, so );
		NativeGolden.Check( key + "/flags", NativeGolden.Native( so, "flags", () => ((ulong)so.CurrentFlags).ToString( "X" ) ), 0 );
		if ( so is not SceneModel ) NativeGolden.Check( key + "/bounds", NativeGolden.Native( so, "nativebounds", () => NativeGolden.B( so.WorldBounds ) ), 1.8f, absolute: true );
	}

	static string test;
	static readonly System.Collections.Generic.List<SceneObject> made = new();

	// Which object of the test it is, in the order it's first checked
	static int Index( SceneObject so )
	{
		var i = made.IndexOf( so );
		if ( i >= 0 ) return i;
		made.Add( so );
		return made.Count - 1;
	}

	static void Start( string name )
	{
		test = name;
		made.Clear();
	}

	[TestMethod]
	public void DrawsAreNativesAfterEveryChange()
	{
		Start( "every change" );
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
		Start( "reload" );
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

			// What Model.OnReloaded does for its scene objects (the reload itself isn't sent: it drops the shared model's physics
			// and other cached data that later tests use)
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
		Start( "body groups" );
		var world = new SceneWorld();
		try
		{
			var human = Model.Load( "models/citizen_human/human.vmdl" );
			Assert.IsTrue( human.Parts.Count > 0, "the model has body parts" );
			var managed = new SceneModel( world, human, Transform.Zero );

			void Set( string part, int value )
			{
				managed.SetBodyGroup( part, value );
				NativeGolden.Check( $"bodygroup/{part} = {value}", NativeGolden.Native( managed, "bodygroup", () => managed.MeshGroupMask.ToString( "X" ) ), 0 );
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
			NativeGolden.Check( "translucency/mixed", NativeGolden.Native( mixed, "flags", () => ((ulong)mixed.CurrentFlags).ToString( "X" ) ), 0 );

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
	/// A map's model gets native's flags, translucency worked out from the materials (without Source 2's device native's
	/// carried the meshes' compiled translucency). Construct's darkroom doors, glass panes in a frame, drew no glass.
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
			Assert.IsTrue( door.Flags.IsOpaque && door.Flags.IsTranslucent, $"opaque and translucent meshes: {door.CurrentFlags}" );

			var pane = new SceneModel( world, Model.Builder.WithName( "maps/test/entities/pane.vmdl" ).AddMesh( Quad( glass ) ).Create(), Transform.Zero );
			Assert.IsTrue( !pane.Flags.IsOpaque && pane.Flags.IsTranslucent, $"a translucent mesh: {pane.CurrentFlags}" );
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
				var name = so.GetType().Name;
				string Bounds() => NativeGolden.Native( so, "nativebounds", () => NativeGolden.B( so.WorldBounds ) );
				NativeGolden.Check( $"bounds/{name} infinite at first", Bounds(), 0 );
				so.Transform = new Transform( new Vector3( 50, 60, 70 ) );
				NativeGolden.Check( $"bounds/{name} after a transform", Bounds(), 0 );
				so.Bounds = new BBox( new Vector3( -3.5f, 2.25f, 0 ), new Vector3( 4.1f, 9, 12.5f ) );
				NativeGolden.Check( $"bounds/{name} quantised as native's", Bounds(), 0 );
				so.Bounds = new BBox( new Vector3( float.NaN ), new Vector3( 1 ) );
				NativeGolden.Check( $"bounds/{name} invalid bounds ignored", Bounds(), 0 );
			}
		}
		finally
		{
			world.Delete();
		}
	}
}
