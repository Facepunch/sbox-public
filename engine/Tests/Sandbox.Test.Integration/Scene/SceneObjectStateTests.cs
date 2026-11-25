using System;
using System.Collections.Generic;
using System.Linq;

namespace SceneTests;

/// <summary>
/// A scene object's state (flags, visibility, tint, layer, LOD, mesh groups, tags, bounds, draws) is kept in managed code,
/// starts as native's new object started and follows the same changes, checked against native's values
/// (<see cref="NativeGolden"/>). Objects have no native object, open no handle scope, and leave their world when deleted.
/// </summary>
[TestClass]
public class SceneObjectStateTests
{
	static readonly (string Name, Func<SceneWorld, SceneObject> Make)[] Kinds =
	[
		("box", w => new SceneObject( w, Model.Load( "models/dev/box.vmdl" ), new Transform( new Vector3( 10, 20, 30 ) ) )),
		("human object", w => new SceneObject( w, Model.Load( "models/citizen_human/human.vmdl" ) )),
		("human model", w => new SceneModel( w, Model.Load( "models/citizen_human/human.vmdl" ), new Transform( new Vector3( 5, 0, 0 ) ) )),
		("custom", w => new SceneCustomObject( w )),
		("dynamic", w => new SceneDynamicObject( w )),
		("line", w => new SceneLineObject( w )),
	];

	static void Same( SceneObject so, string what ) => NativeGolden.Check( $"object/{what}", NativeGolden.Object( so ) );

	[TestMethod]
	public void ObjectsStartAndChangeAsNatives()
	{
		var world = new SceneWorld();
		try
		{
			foreach ( var (name, make) in Kinds )
			{
				var so = make( world );
				Same( so, $"{name} made" );

				so.Transform = new Transform( new Vector3( 100, -50, 25 ), Rotation.From( 10, 20, 30 ), 2.0f );
				so.ColorTint = new Color( 0.5f, 0.25f, 1.0f, 0.75f );
				so.Tags.Add( "solid" );
				so.Tags.Add( "prop" );
				so.Tags.Remove( "solid" );
				so.RenderLayer = SceneRenderLayer.OverlayWithDepth;
				so.LodOverride = 2;
				so.Batchable = false;
				so.Flags.CastShadows = false;
				if ( so.Model is not null )
				{
					so.MeshGroupMask = 1;
					so.SetMaterialOverride( Material.Load( "materials/default/white.vmat" ) );
				}
				Same( so, $"{name} changed" );

				if ( so.Model is null ) continue;
				so.ClearMaterialOverride();
				so.RenderingEnabled = false;
				so.Model = Model.Cube;
				Same( so, $"{name} model changed" );
			}
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void TintReadsBackAsNativeKeepsIt()
	{
		var world = new SceneWorld();
		try
		{
			var so = new SceneObject( world, Model.Cube );
			var i = 0;
			foreach ( var tint in new[] { Color.White, new Color( 0.5f, 0.25f, 0.1f, 0.5f ), new Color( 2.0f, 1.5f, 0.0f, 1.5f ), new Color( -0.1f, 0.999f, 0.004f, -1 ) } )
			{
				so.ColorTint = tint;
				NativeGolden.Check( $"state/tint {i++}", NativeGolden.Native( so, "tint", () => NativeGolden.C( so.ColorTint ) ), 0 );
			}
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void LayerMatchIsKeptAsNativeKeepsIt()
	{
		var world = new SceneWorld();
		try
		{
			var so = new SceneObject( world, Model.Cube );
			NativeGolden.Check( "state/layer new", NativeGolden.Native( so, "layer", () => so.LayerMatchToken.ToString() ), 0 );
			foreach ( var layer in Enum.GetValues<SceneRenderLayer>() )
			{
				so.RenderLayer = layer;
				NativeGolden.Check( $"state/layer {layer}", NativeGolden.Native( so, "layer", () => so.LayerMatchToken.ToString() ), 0 );
			}
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void LodOverrideIsKeptAsNativeKeepsIt()
	{
		var world = new SceneWorld();
		try
		{
			var so = new SceneObject( world, Model.Cube );
			NativeGolden.Check( "state/lod new", NativeGolden.Native( so, "lod", () => so.LodOverride.ToString() ), 0 );
			foreach ( var lod in new[] { 0, 3, 7, 8, -1, 2, -5 } )
			{
				so.LodOverride = lod;
				NativeGolden.Check( $"state/lod {lod}", NativeGolden.Native( so, "lod", () => so.LodOverride.ToString() ), 0 );
			}
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void AttributesAreManaged()
	{
		var world = new SceneWorld();
		try
		{
			var so = new SceneObject( world, Model.Cube );
			so.Attributes.Set( "skin_tint", 0.5f );
			Assert.IsTrue( so.Attributes.IsManaged, "the set is managed only" );
			Assert.AreEqual( 0.5f, so.Attributes.GetFloat( "skin_tint" ) );
			Assert.AreSame( so.Attributes, so.Attributes.ManagedSet );

			so.Attributes.Set( "LightGroup", 5 );
			so.Attributes.Set( "RenderOrder", -3 );
			Assert.AreEqual( 5, so.LightGroup );
			Assert.AreEqual( -3, so.RenderOrder );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// Children move with their parent in managed code as native moved linked objects (<c>CHILD_SCENEOBJECT_INHERIT_TRANSFORM</c>),
	/// and go when it's deleted.
	/// </summary>
	[TestMethod]
	public void ChildrenMoveAsNativeMovesThem()
	{
		var world = new SceneWorld();
		try
		{
			var parent = new SceneObject( world, Model.Cube, new Transform( new Vector3( 10, 20, 30 ), Rotation.From( 10, 20, 30 ), 2 ) );
			var child = new SceneObject( world, Model.Cube, new Transform( new Vector3( 50, -20, 5 ), Rotation.From( -40, 5, 90 ), 0.5f ) );
			var grandchild = new SceneObject( world, Model.Cube, new Transform( new Vector3( 0, 64, 0 ), Rotation.From( 0, 45, 0 ), 1.5f ) );
			parent.AddChild( "child", child );
			child.AddChild( "grandchild", grandchild );
			Assert.AreSame( parent, child.Parent );

			var i = 0;
			foreach ( var to in new[] { new Transform( new Vector3( -100, 5, 0 ), Rotation.From( 0, 90, 0 ), 2 ), new Transform( new Vector3( 3, 4, 5 ), Rotation.From( 30, -60, 10 ), 1 ), new Transform( Vector3.Zero, Rotation.Identity, 0.25f ) } )
			{
				parent.Transform = to;
				NativeGolden.Check( $"state/child {i}", NativeGolden.T( child.Transform ), 0.01f );
				NativeGolden.Check( $"state/grandchild {i}", NativeGolden.T( grandchild.Transform ), 0.01f );
				i++;
			}

			parent.Delete();
			Assert.IsFalse( child.IsValid(), "children go with their parent" );
			Assert.IsFalse( grandchild.IsValid() );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// An object native code made (a tool's sky, Hammer's helpers) keeps its native object: its tags are read from it once, and
	/// tag and visibility changes still reach it.
	/// </summary>
	[TestMethod]
	public void NativeMadeObjectsKeepTheirTagsAndVisibilityInStep()
	{
		var world = new SceneWorld();
		try
		{
			var so = NativeEngine.CSceneSystem.CreateSkyBox( Material.Load( "materials/skybox/skybox_day_01.vmat" ).native, world );
			so.native.AddTag( StringToken.FindOrCreate( "world" ) );
			Assert.IsTrue( so.Tags.Has( "world" ) );

			so.Tags.Add( "a" );
			so.Tags.Add( "b" );
			so.Tags.Add( "a" );
			so.Tags.Remove( "world" );
			CollectionAssert.AreEqual( new[] { StringToken.FindOrCreate( "a" ), StringToken.FindOrCreate( "b" ) }, so.TagTokens.ToArray() );

			var native = Enumerable.Range( 0, so.native.GetTagCount() ).Select( so.native.GetTagAt ).ToArray();
			CollectionAssert.AreEqual( so.TagTokens.ToArray(), native, "native's object gets every tag change" );

			so.RenderingEnabled = false;
			Assert.IsFalse( so.RenderingEnabled );
			Assert.IsFalse( so.native.IsRenderingEnabled(), "and visibility" );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// An object managed code makes has no native object and opens no handle scope (one left open was taken by the next native
	/// object made instead, and logged), while a scope that no native object takes is still logged.
	/// </summary>
	[TestMethod]
	public void ObjectsHaveNoNativeObjectAndLeaveNoHandleScopeOpen()
	{
		var world = new SceneWorld();
		var logged = new List<string>();
		void OnEmitted( LogEvent e ) { if ( e.Message?.Contains( "NextHandleScope" ) == true ) lock ( logged ) logged.Add( e.Message ); }
		Sandbox.Diagnostics.Logging.OnEmitted += OnEmitted;
		try
		{
			var sky = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			var kinds = Kinds.Concat( new (string Name, Func<SceneWorld, SceneObject> Make)[]
			{
				("point light", w => new ScenePointLight( w, Vector3.Zero, 100, Color.White )),
				("spot light", w => new SceneSpotLight( w, Vector3.Zero, Color.White )),
				("sun", w => new SceneDirectionalLight( w, Rotation.From( 45, 30, 0 ), Color.White )),
				("cubemap", w => new SceneCubemap( w, null, BBox.FromPositionAndSize( 0, 200 ) )),
				("light probe", w => new SceneLightProbe( w, null, null, null, BBox.FromPositionAndSize( 0, 200 ), Transform.Zero, 0, 0 )),
				("sky", w => new SceneSkyBox( w, sky )),
				("decal", w => new DecalSceneObject( w )),
			} );

			foreach ( var (name, make) in kinds )
			{
				var so = make( world );
				Assert.IsTrue( so.IsValid(), $"{name}: valid" );
				Assert.IsTrue( so.native.IsNull, $"{name}: no native object" );
				Assert.IsNull( HandleIndex.nextObject, $"{name}: no scope left open" );
			}

			lock ( logged ) Assert.AreEqual( 0, logged.Count, string.Join( "; ", logged ) );

			// A scope opened around code that makes no native object is a misuse, and is still logged
			var unused = Kinds[3].Make( world );
			using ( IHandle.MakeNextHandle( unused ) ) { }
			Assert.IsNull( HandleIndex.nextObject, "closed" );
			lock ( logged ) Assert.AreEqual( 1, logged.Count, "an unused scope is logged" );
		}
		finally
		{
			Sandbox.Diagnostics.Logging.OnEmitted -= OnEmitted;
			world.Delete();
		}
	}

	[TestMethod]
	public void ObjectsLeaveTheirWorldWhenDeleted()
	{
		var world = new SceneWorld();
		try
		{
			var kept = Kinds[0].Make( world );
			var deleted = Kinds[2].Make( world );
			lock ( world.InternalSceneObjects ) Assert.IsTrue( world.InternalSceneObjects.Contains( deleted ) );

			deleted.Delete();
			Assert.IsFalse( deleted.IsValid(), "invalid at once" );
			Assert.ThrowsException<NullReferenceException>( () => deleted.Position = Vector3.One, "a deleted object throws as before" );
			lock ( world.InternalSceneObjects ) Assert.IsTrue( world.InternalSceneObjects.Contains( deleted ), "in its world until its deletes run" );

			world.DeletePendingObjects();
			lock ( world.InternalSceneObjects )
			{
				Assert.IsFalse( world.InternalSceneObjects.Contains( deleted ), "gone after its world's deletes" );
				Assert.IsTrue( world.InternalSceneObjects.Contains( kept ) );
			}

			// Every world's deletes run at the frame's end, flushed or not
			var other = Kinds[3].Make( world );
			other.Delete();
			SceneWorld.RunEndOfFrameDeletes();
			lock ( world.InternalSceneObjects ) Assert.IsFalse( world.InternalSceneObjects.Contains( other ) );
		}
		finally
		{
			world.Delete();
		}

		lock ( world.InternalSceneObjects ) Assert.AreEqual( 0, world.InternalSceneObjects.Count, "objects go with their world" );
	}
}
