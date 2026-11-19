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
	/// A scene model posed on another thread (the animation passes run on workers) writes its bones straight into its mesh
	/// object there; only its bounds, which move it in the world, wait for the next render.
	/// </summary>
	[TestMethod]
	public void BonesPosedOnAWorkerGoStraightIn()
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

			System.Threading.Tasks.Task.Run( () => human.Update( 0.4f ) ).Wait();

			var expected = new Matrix3x4[mesh.Mesh.BindPose.Length];
			human.GetBoneWorldMatrices( expected );
			var written = mesh.BoneMatrices.ToArray();
			CollectionAssert.AreEqual( expected, written, "the pose, written on the worker" );
			Assert.AreEqual( 1, (int)pending.GetValue( render ), "the bounds wait" );

			prepare.Invoke( render, [camera, false] );
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
}
