using Sandbox.Rendering;
using System.Reflection;

namespace SceneTests;

/// <summary>
/// Objects that read nothing from native (decals, point and spot lights, cubemaps, custom objects, models managed code made)
/// write a change straight into their render object on the main thread, with nothing left for the render's next prepare: a
/// model, its materials and its attributes too.
/// </summary>
[TestClass]
public class WorldRenderWriteTests
{
	static readonly System.Type RenderType = System.Type.GetType( "Sandbox.Rendering.Bridge.WorldRender, Sandbox.Rendering", true );
	const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	[TestMethod]
	public void ChangesGoStraightIn()
	{
		typeof( ThreadSafe ).GetMethod( "MarkMainThread", Any ).Invoke( null, null );

		var world = new SceneWorld();
		var camera = new SceneCamera { World = world };
		var render = (System.IDisposable)System.Activator.CreateInstance( RenderType, [world] );
		try
		{
			var decal = new DecalSceneObject( world );
			var light = new ScenePointLight( world, Vector3.Zero, 100, Color.White );
			var custom = new SceneCustomObject( world );
			var box = new SceneObject( world, Model.Load( "models/dev/box.vmdl" ) );
			var animated = new SceneModel( world, Model.Load( "models/dev/box.vmdl" ), Transform.Zero );

			var prepare = RenderType.GetMethod( "Prepare", Any );
			var find = RenderType.GetMethod( "Find", Any );
			var pending = RenderType.GetProperty( "PendingChanges", Any );
			prepare.Invoke( render, [camera, false] );
			Assert.AreEqual( 0, (int)pending.GetValue( render ), "prepared" );

			var moved = new Transform( new Vector3( 10, 20, 30 ) );
			decal.Transform = moved;
			light.Transform = moved;
			light.LightColor = Color.Red;
			custom.Bounds = new BBox( new Vector3( -5 ), new Vector3( 5 ) );
			box.Position = moved.Position;
			box.ColorTint = Color.Green;
			Assert.AreEqual( 0, (int)pending.GetValue( render ), "nothing waits for a prepare" );

			Assert.AreEqual( moved.Position, ((Borealis.RenderObject)find.Invoke( render, [decal] )).Transform.Position, "decal" );
			var lightObject = (Borealis.LightObject)find.Invoke( render, [light] );
			Assert.AreEqual( moved.Position, lightObject.Transform.Position, "light moved" );
			Assert.AreEqual( Color.Red, lightObject.Color, "light colour" );
			var customObject = (Borealis.RenderObject)find.Invoke( render, [custom] );
			Assert.AreEqual( new BBox( new Vector3( -5 ), new Vector3( 5 ) ), customObject.LocalBounds, "custom bounds" );
			var boxObject = (Borealis.MeshObject)find.Invoke( render, [box] );
			Assert.AreEqual( moved.Position, boxObject.Transform.Position, "model moved" );
			Assert.AreEqual( Color.Green, boxObject.Tint, "model tint" );

			// A scene model writes its own too
			animated.Position = moved.Position;
			Assert.AreEqual( moved.Position, ((Borealis.RenderObject)find.Invoke( render, [animated] )).Transform.Position, "animated" );

			// Its draws are worked out as they change, and its attributes are read as it draws from when they're made
			box.SetMaterialOverride( Material.Load( "materials/default/white.vmat" ) );
			box.Model = Model.Load( "models/dev/plane.vmdl" );
			box.Attributes.Set( "Tint", Color.Red );
			Assert.AreEqual( 0, (int)pending.GetValue( render ), "a model, material and attribute change wait for nothing" );
			Assert.IsNotNull( boxObject.AttributesFrom, "attributes read as it draws" );
		}
		finally
		{
			render.Dispose();
			camera.Dispose();
			world.Delete();
		}
	}

	/// <summary>
	/// The kinds that make and write their own render object (SceneObject.OwnsRenderObject) write a change into it themselves,
	/// with nothing waiting for the world's next frame.
	/// </summary>
	[TestMethod]
	public void ObjectsWriteTheirOwnRenderObjects()
	{
		typeof( ThreadSafe ).GetMethod( "MarkMainThread", Any ).Invoke( null, null );

		var world = new SceneWorld();
		var camera = new SceneCamera { World = world };
		var render = (System.IDisposable)System.Activator.CreateInstance( RenderType, [world] );
		try
		{
			var decal = new DecalSceneObject( world );
			var light = new ScenePointLight( world, Vector3.Zero, 100, Color.White );
			var spot = new SceneSpotLight( world );
			var cubemap = new SceneCubemap( world, null, new BBox( new Vector3( -10 ), new Vector3( 10 ) ) );
			var custom = new SceneCustomObject( world );
			var dynamic = new SceneDynamicObject( world );
			var box = new SceneObject( world, Model.Load( "models/dev/box.vmdl" ) );
			RenderType.GetMethod( "Prepare", Any ).Invoke( render, [camera, false] );
			Assert.IsInstanceOfType<Borealis.LightObject>( light.RenderObject );
			Assert.AreEqual( Borealis.LightObject.LightKind.Spot, ((Borealis.LightObject)spot.RenderObject).Kind );
			var find = RenderType.GetMethod( "Find", Any );
			Assert.IsInstanceOfType<Borealis.DecalObject>( decal.RenderObject );
			Assert.AreSame( decal.RenderObject, find.Invoke( render, [decal] ), "the decal's own" );

			var pending = RenderType.GetProperty( "PendingChanges", Any );
			decal.SortOrder = 7;
			decal.Position = new Vector3( 1, 2, 3 );
			light.LightColor = Color.Red;
			light.Radius = 250;
			spot.ConeOuter = 40;
			spot.RenderingEnabled = false;
			cubemap.Priority = 3;
			cubemap.Position = new Vector3( 4, 5, 6 );
			custom.ColorTint = Color.Green;
			dynamic.RenderingEnabled = false;
			box.ColorTint = Color.Blue;
			box.Position = new Vector3( 7, 8, 9 );
			Assert.AreEqual( 0, (int)pending.GetValue( render ), "nothing waits" );

			var lightObject = (Borealis.LightObject)light.RenderObject;
			Assert.AreEqual( Color.Red, lightObject.Color );
			Assert.AreEqual( 250, lightObject.Radius );
			Assert.AreEqual( 40, ((Borealis.LightObject)spot.RenderObject).ConeOuter );
			Assert.IsFalse( ((Borealis.LightObject)spot.RenderObject).Visible, "a hidden light lights nothing" );
			var envMap = (Borealis.EnvMapObject)cubemap.RenderObject;
			Assert.AreEqual( 3, envMap.Priority );
			Assert.AreEqual( new Vector3( 4, 5, 6 ), envMap.Transform.Position );
			Assert.AreEqual( Color.Green, ((Borealis.CustomObject)custom.RenderObject).Tint );
			Assert.IsFalse( ((Borealis.CustomObject)dynamic.RenderObject).Visible );
			var boxObject = (Borealis.MeshObject)box.RenderObject;
			Assert.AreEqual( Color.Blue, boxObject.Tint );
			Assert.AreEqual( new Vector3( 7, 8, 9 ), boxObject.Transform.Position );

			var obj = (Borealis.DecalObject)decal.RenderObject;
			Assert.AreEqual( 7u, obj.SortOrder );
			Assert.AreEqual( new Vector3( 1, 2, 3 ), obj.Transform.Position );
		}
		finally
		{
			render.Dispose();
			camera.Dispose();
			world.Delete();
		}
	}

	/// <summary>
	/// A scene model posed on another thread (the animation passes run on workers) writes its bones into its mesh object's
	/// second pose array there, which the next prepare (the sync point) swaps in with its bounds: what's drawn until then is the
	/// pose before, so a frame being drawn isn't written under.
	/// </summary>
	[TestMethod]
	public void BonesPosedOnAWorkerAreDrawnFromTheNextPrepare()
	{
		typeof( ThreadSafe ).GetMethod( "MarkMainThread", Any ).Invoke( null, null );

		var world = new SceneWorld();
		var camera = new SceneCamera { World = world };
		var render = (System.IDisposable)System.Activator.CreateInstance( RenderType, [world] );
		try
		{
			var human = new SceneModel( world, Model.Load( "models/citizen_human/human.vmdl" ), new Transform( new Vector3( 50, 0, 0 ) ) );
			human.UseAnimGraph = false;
			human.CurrentSequence.Name = "Jump_Standing";
			human.CurrentSequence.Looping = true;
			Assert.IsTrue( human.animNative.HasInstance );

			var prepare = RenderType.GetMethod( "Prepare", Any );
			var pending = RenderType.GetProperty( "PendingChanges", Any );
			prepare.Invoke( render, [camera, false] );
			var mesh = (Borealis.MeshObject)human.RenderObject;

			// No device makes the human's mesh here: it draws a skinned stand-in with as many bones
			var bones = human.Model.BoneCount;
			var standIn = new Borealis.RenderMesh( new BBox( new Vector3( -10 ), new Vector3( 10 ) ), [[]] );
			standIn.SetSkinning( [new Borealis.RenderMesh.MeshSkin { BlendWeightCount = 1, VertexCount = 1, InverseBindPoses = new Matrix3x4[bones], MasterBones = new int[bones] }], new Matrix3x4[bones] );
			mesh.Mesh = standIn;
			Assert.IsTrue( mesh.Mesh.IsSkinned, "skinned" );

			var before = mesh.BoneMatrices.ToArray();
			System.Threading.Tasks.Task.Run( () => human.Update( 0.4f ) ).Wait();

			var expected = new Matrix3x4[mesh.Mesh.BindPose.Length];
			human.GetBoneWorldMatrices( expected );
			CollectionAssert.AreNotEqual( before, expected, "the worker posed it" );
			CollectionAssert.AreEqual( before, mesh.BoneMatrices.ToArray(), "drawn from the pose before until the next prepare" );
			Assert.AreEqual( 1, (int)pending.GetValue( render ), "the bounds and pose wait" );

			prepare.Invoke( render, [camera, false] );
			CollectionAssert.AreEqual( expected, mesh.BoneMatrices.ToArray(), "the pose written on the worker, drawn from the prepare" );
			var bounds = SceneObject.ToLocal( human.Bounds, mesh.Transform );
			Assert.IsTrue( bounds.Mins.Distance( mesh.LocalBounds.Mins ) < 1e-3f && bounds.Maxs.Distance( mesh.LocalBounds.Maxs ) < 1e-3f, "bounds after the next frame" );
		}
		finally
		{
			render.Dispose();
			camera.Dispose();
			world.Delete();
		}
	}

	/// <summary>
	/// With changes deferred to the sync point (<see cref="RenderSync.DeferWrites"/>, the game window's mode), nothing is
	/// written into a render object as it changes, a deleted object stays until then, and the next prepare writes each changed
	/// object once with everything that changed.
	/// </summary>
	[TestMethod]
	public void DeferredChangesWaitForThePrepare()
	{
		typeof( ThreadSafe ).GetMethod( "MarkMainThread", Any ).Invoke( null, null );

		var world = new SceneWorld();
		var camera = new SceneCamera { World = world };
		var render = (System.IDisposable)System.Activator.CreateInstance( RenderType, [world] );
		var deferred = RenderSync.DeferWrites;
		try
		{
			var light = new ScenePointLight( world, Vector3.Zero, 100, Color.White );
			var box = new SceneObject( world, Model.Load( "models/dev/box.vmdl" ) );
			var gone = new SceneObject( world, Model.Load( "models/dev/box.vmdl" ) );

			var prepare = RenderType.GetMethod( "Prepare", Any );
			var find = RenderType.GetMethod( "Find", Any );
			var pending = RenderType.GetProperty( "PendingChanges", Any );
			prepare.Invoke( render, [camera, false] );
			var lightObject = (Borealis.LightObject)find.Invoke( render, [light] );
			var boxObject = (Borealis.MeshObject)find.Invoke( render, [box] );
			Assert.IsNotNull( find.Invoke( render, [gone] ), "made" );

			RenderSync.DeferWrites = true;
			var moved = new Transform( new Vector3( 10, 20, 30 ) );
			light.Transform = moved;
			light.LightColor = Color.Red;
			box.Position = moved.Position;
			box.Position = moved.Position * 2;
			box.ColorTint = Color.Green;
			gone.Delete();

			Assert.AreEqual( 2, (int)pending.GetValue( render ), "a change each for the light and the box, however many they had" );
			Assert.AreEqual( Vector3.Zero, lightObject.Transform.Position, "the light waits" );
			Assert.AreNotEqual( Color.Red, lightObject.Color, "its colour waits" );
			Assert.AreNotEqual( moved.Position * 2, boxObject.Transform.Position, "the box waits" );
			Assert.IsNotNull( find.Invoke( render, [gone] ), "the deleted object stays until the prepare" );

			prepare.Invoke( render, [camera, false] );
			Assert.AreEqual( 0, (int)pending.GetValue( render ), "written" );
			Assert.AreEqual( moved.Position, lightObject.Transform.Position, "light moved" );
			Assert.AreEqual( Color.Red, lightObject.Color, "light colour" );
			Assert.AreEqual( moved.Position * 2, boxObject.Transform.Position, "box moved, to where it ended up" );
			Assert.AreEqual( Color.Green, boxObject.Tint, "box tint" );
			Assert.IsNull( find.Invoke( render, [gone] ), "the deleted object went" );
		}
		finally
		{
			RenderSync.DeferWrites = deferred;
			render.Dispose();
			camera.Dispose();
			world.Delete();
		}
	}

	/// <summary>
	/// The sync point (<see cref="RenderSync.Run"/>) writes every live world with changes waiting, one no view prepares again
	/// this frame (a skybox's or an extra world, drawn inside another's view) as much as the one the window shows; a world no view
	/// has drawn yet is left to its first, and a view's prepare after the sync point finds nothing to do.
	/// </summary>
	[TestMethod]
	public void TheSyncPointWritesEveryWorldWithChanges()
	{
		typeof( ThreadSafe ).GetMethod( "MarkMainThread", Any ).Invoke( null, null );

		var shown = new SceneWorld();
		var inside = new SceneWorld();
		var unseen = new SceneWorld();
		var camera = new SceneCamera { World = shown };
		var insideCamera = new SceneCamera { World = inside };
		var shownRender = (System.IDisposable)System.Activator.CreateInstance( RenderType, [shown] );
		var insideRender = (System.IDisposable)System.Activator.CreateInstance( RenderType, [inside] );
		var unseenRender = (System.IDisposable)System.Activator.CreateInstance( RenderType, [unseen] );
		var deferred = RenderSync.DeferWrites;
		try
		{
			var box = new SceneObject( shown, Model.Load( "models/dev/box.vmdl" ) );
			var sky = new SceneObject( inside, Model.Load( "models/dev/box.vmdl" ) );
			var never = new SceneObject( unseen, Model.Load( "models/dev/box.vmdl" ) );

			var prepare = RenderType.GetMethod( "Prepare", Any );
			var find = RenderType.GetMethod( "Find", Any );
			var pending = RenderType.GetProperty( "PendingChanges", Any );
			prepare.Invoke( shownRender, [camera, false] );
			prepare.Invoke( insideRender, [insideCamera, false] );
			var boxObject = (Borealis.MeshObject)find.Invoke( shownRender, [box] );
			var skyObject = (Borealis.MeshObject)find.Invoke( insideRender, [sky] );
			Assert.IsTrue( RenderSync.Worlds >= 3, "each world's renderer is registered" );

			RenderSync.DeferWrites = true;
			var moved = new Vector3( 10, 20, 30 );
			box.Position = moved;
			sky.Position = moved;
			never.Position = moved;
			Assert.AreNotEqual( moved, skyObject.Transform.Position, "waits for the sync point" );

			var syncs = RenderSync.Syncs;
			RenderSync.Run();
			Assert.AreEqual( syncs + 1, RenderSync.Syncs, "ran" );
			Assert.AreEqual( moved, boxObject.Transform.Position, "the shown world is written" );
			Assert.AreEqual( moved, skyObject.Transform.Position, "a world drawn inside another's view is written there too" );
			Assert.AreEqual( 0, (int)pending.GetValue( insideRender ), "nothing waits in it" );
			Assert.IsTrue( (int)pending.GetValue( unseenRender ) > 0, "a world no view has drawn waits for its first" );

			// A view after the sync point finds the world ready, and writes what changed after it
			prepare.Invoke( shownRender, [camera, false] );
			box.Position = moved * 2;
			prepare.Invoke( shownRender, [camera, false] );
			Assert.AreEqual( moved * 2, boxObject.Transform.Position, "a change after the sync point is written by the next prepare" );
		}
		finally
		{
			RenderSync.DeferWrites = deferred;
			unseenRender.Dispose();
			insideRender.Dispose();
			shownRender.Dispose();
			insideCamera.Dispose();
			camera.Dispose();
			unseen.Delete();
			inside.Delete();
			shown.Delete();
		}
	}

	/// <summary>
	/// What lights the world rather than drawing in it writes straight there too: a sun's change waits for nothing but the next
	/// view to light with it, a probe volume's goes into its volume.
	/// </summary>
	[TestMethod]
	public void SunsAndProbeVolumesWriteStraightIn()
	{
		typeof( ThreadSafe ).GetMethod( "MarkMainThread", Any ).Invoke( null, null );

		var world = new SceneWorld();
		var camera = new SceneCamera { World = world };
		var render = (System.IDisposable)System.Activator.CreateInstance( RenderType, [world] );
		try
		{
			using var texture = Texture.CreateVolume( 2, 2, 12 ).Finish();
			var sun = new SceneDirectionalLight( world, Rotation.From( 45, 0, 0 ), Color.Red );
			var probe = new SceneLightProbe( world, texture, null, null, new BBox( new Vector3( -100 ), new Vector3( 100 ) ), Transform.Zero, 0, 0 );

			var prepare = RenderType.GetMethod( "Prepare", Any );
			var pending = RenderType.GetProperty( "PendingChanges", Any );
			var renderWorld = (Borealis.RenderWorld)RenderType.GetProperty( "World" ).GetValue( render );
			prepare.Invoke( render, [camera, false] );
			Assert.AreEqual( Color.Red, renderWorld.Lighting.SunColor );

			sun.LightColor = Color.Blue;
			probe.Position = new Vector3( 10, 20, 30 );
			Assert.AreEqual( 0, (int)pending.GetValue( render ), "nothing waits for a prepare" );
			Assert.AreEqual( new Vector3( 10, 20, 30 ), renderWorld.LightProbeVolumes[0].Transform.Position, "the volume moved" );

			prepare.Invoke( render, [camera, false] );
			Assert.AreEqual( Color.Blue, renderWorld.Lighting.SunColor, "the next view lights with the new colour" );
		}
		finally
		{
			render.Dispose();
			camera.Dispose();
			world.Delete();
		}
	}

	/// <summary>
	/// An object casts shadows with both shadow flags, as native's shadow layers require them: a tool's helper model has only
	/// the material's (MaterialSupportsShadows), which <c>Flags.CastShadows</c> reads as on.
	/// </summary>
	[TestMethod]
	public void ShadowsNeedBothShadowFlags()
	{
		typeof( ThreadSafe ).GetMethod( "MarkMainThread", Any ).Invoke( null, null );

		var world = new SceneWorld();
		var camera = new SceneCamera { World = world };
		var render = (System.IDisposable)System.Activator.CreateInstance( RenderType, [world] );
		try
		{
			var box = new SceneObject( world, Model.Load( "models/dev/box.vmdl" ) );
			RenderType.GetMethod( "Prepare", Any ).Invoke( render, [camera, false] );
			var mesh = (Borealis.MeshObject)box.RenderObject;
			Assert.IsTrue( mesh.CastShadows );

			box.ChangeFlags( SceneObjectFlags.MaterialSupportsShadows, SceneObjectFlags.CastShadows );
			RenderType.GetMethod( "Prepare", Any ).Invoke( render, [camera, false] );
			Assert.IsTrue( box.Flags.CastShadows, "the flag reads as on with either bit" );
			Assert.IsFalse( mesh.CastShadows, "casting isn't enabled" );
		}
		finally
		{
			render.Dispose();
			camera.Dispose();
			world.Delete();
		}
	}

	/// <summary>
	/// An object whose draw blends is drawn with the translucent objects though its flags say opaque alone, as native's
	/// flags stay without Source 2's device for an object native code made (Hammer's meshes: its tools volumes and glass
	/// drew nowhere, the opaque pass passing over their blended draws).
	/// </summary>
	[TestMethod]
	public void BlendedDrawsDrawTranslucentWhateverTheFlags()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1: meshes are made on the device." );

		typeof( ThreadSafe ).GetMethod( "MarkMainThread", Any ).Invoke( null, null );

		var world = new SceneWorld();
		var camera = new SceneCamera { World = world };
		var render = (System.IDisposable)System.Activator.CreateInstance( RenderType, [world] );
		try
		{
			// The human's eyelashes: its translucent material
			var glass = Material.Load( "models/citizen_human/heads/citizen_human_femalehead01/eyelashes_01_opacity.vmat" );
			var pane = new SceneObject( world, Model.Load( "models/dev/box.vmdl" ) );
			pane.SetMaterialOverride( glass );
			pane.ChangeFlags( SceneObjectFlags.IsOpaque, SceneObjectFlags.IsOpaque | SceneObjectFlags.IsTranslucent );
			RenderType.GetMethod( "Prepare", Any ).Invoke( render, [camera, false] );

			var mesh = (Borealis.MeshObject)pane.RenderObject;
			Assert.IsNotNull( mesh.Mesh, "the box's mesh" );
			Assert.IsTrue( mesh.Mesh.Draws.Any( x => x.Translucent ), "its draw blends" );
			Assert.IsTrue( mesh.DrawsTranslucent, "drawn with the translucent objects" );
		}
		finally
		{
			render.Dispose();
			camera.Dispose();
			world.Delete();
		}
	}
}
