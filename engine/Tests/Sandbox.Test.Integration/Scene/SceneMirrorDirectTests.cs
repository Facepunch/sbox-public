using Sandbox.Rendering;
using System.Reflection;

namespace SceneTests;

/// <summary>
/// Objects that read nothing from native (decals, point and spot lights, cubemaps, custom objects, models managed code made)
/// write a change straight into their render object on the main thread, with nothing left for the mirror's next sync; a
/// model or material change, and an animated object's, still wait for it.
/// </summary>
[TestClass]
public class SceneMirrorDirectTests
{
	static readonly System.Type MirrorType = System.Type.GetType( "Sandbox.Rendering.Bridge.SceneMirror, Sandbox.Rendering", true );
	const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	[TestMethod]
	public void ChangesApplyWithoutASync()
	{
		typeof( ThreadSafe ).GetMethod( "MarkMainThread", Any ).Invoke( null, null );

		var world = new SceneWorld();
		var camera = new SceneCamera { World = world };
		var mirror = (System.IDisposable)System.Activator.CreateInstance( MirrorType, [world] );
		try
		{
			var decal = new DecalSceneObject( world );
			var light = new ScenePointLight( world, Vector3.Zero, 100, Color.White );
			var custom = new SceneCustomObject( world );
			var box = new SceneObject( world, Model.Load( "models/dev/box.vmdl" ) );
			var animated = new SceneModel( world, Model.Load( "models/dev/box.vmdl" ), Transform.Zero );

			var sync = MirrorType.GetMethod( "Sync", Any );
			var find = MirrorType.GetMethod( "Find", Any );
			var pending = MirrorType.GetProperty( "PendingChanges", Any );
			sync.Invoke( mirror, [camera, false] );
			Assert.AreEqual( 0, (int)pending.GetValue( mirror ), "synced" );

			var moved = new Transform( new Vector3( 10, 20, 30 ) );
			decal.Transform = moved;
			light.Transform = moved;
			light.LightColor = Color.Red;
			custom.Bounds = new BBox( new Vector3( -5 ), new Vector3( 5 ) );
			box.Position = moved.Position;
			box.ColorTint = Color.Green;
			Assert.AreEqual( 0, (int)pending.GetValue( mirror ), "nothing waits for a sync" );

			Assert.AreEqual( moved.Position, ((Borealis.RenderObject)find.Invoke( mirror, [decal] )).Transform.Position, "decal" );
			var lightObject = (Borealis.LightObject)find.Invoke( mirror, [light] );
			Assert.AreEqual( moved.Position, lightObject.Transform.Position, "light moved" );
			Assert.AreEqual( Color.Red, lightObject.Color, "light colour" );
			var customObject = (Borealis.RenderObject)find.Invoke( mirror, [custom] );
			Assert.AreEqual( new BBox( new Vector3( -5 ), new Vector3( 5 ) ), customObject.LocalBounds, "custom bounds" );
			var boxObject = (Borealis.MeshObject)find.Invoke( mirror, [box] );
			Assert.AreEqual( moved.Position, boxObject.Transform.Position, "model moved" );
			Assert.AreEqual( Color.Green, boxObject.Tint, "model tint" );

			// Draws are worked out once per sync, and an animated object's state is read then
			box.SetMaterialOverride( Material.Load( "materials/default/white.vmat" ) );
			animated.Position = moved.Position;
			Assert.AreEqual( 2, (int)pending.GetValue( mirror ), "a material change and an animated object wait" );
			sync.Invoke( mirror, [camera, false] );
			Assert.AreEqual( moved.Position, ((Borealis.RenderObject)find.Invoke( mirror, [animated] )).Transform.Position, "animated after the sync" );
		}
		finally
		{
			mirror.Dispose();
			camera.Dispose();
			world.Delete();
		}
	}

	/// <summary>
	/// The kinds that make and write their own render object (SceneObject.OwnsRenderObject) write a change into it themselves:
	/// with nothing listening to the world, it still arrives.
	/// </summary>
	[TestMethod]
	public void ObjectsWriteTheirOwnRenderObjects()
	{
		typeof( ThreadSafe ).GetMethod( "MarkMainThread", Any ).Invoke( null, null );

		var world = new SceneWorld();
		var camera = new SceneCamera { World = world };
		var mirror = (System.IDisposable)System.Activator.CreateInstance( MirrorType, [world] );
		try
		{
			var decal = new DecalSceneObject( world );
			var light = new ScenePointLight( world, Vector3.Zero, 100, Color.White );
			var spot = new SceneSpotLight( world );
			var cubemap = new SceneCubemap( world, null, new BBox( new Vector3( -10 ), new Vector3( 10 ) ) );
			var custom = new SceneCustomObject( world );
			var dynamic = new SceneDynamicObject( world );
			var box = new SceneObject( world, Model.Load( "models/dev/box.vmdl" ) );
			MirrorType.GetMethod( "Sync", Any ).Invoke( mirror, [camera, false] );
			Assert.IsInstanceOfType<Borealis.LightObject>( light.RenderObject );
			Assert.AreEqual( Borealis.LightObject.LightKind.Spot, ((Borealis.LightObject)spot.RenderObject).Kind );
			var find = MirrorType.GetMethod( "Find", Any );
			Assert.IsInstanceOfType<Borealis.DecalObject>( decal.RenderObject );
			Assert.AreSame( decal.RenderObject, find.Invoke( mirror, [decal] ), "the decal's own" );

			var listener = world.ChangeListener;
			world.ChangeListener = null;
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
			world.ChangeListener = listener;

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
			mirror.Dispose();
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
		var mirror = (System.IDisposable)System.Activator.CreateInstance( MirrorType, [world] );
		try
		{
			var box = new SceneObject( world, Model.Load( "models/dev/box.vmdl" ) );
			MirrorType.GetMethod( "Sync", Any ).Invoke( mirror, [camera, false] );
			var mesh = (Borealis.MeshObject)box.RenderObject;
			Assert.IsTrue( mesh.CastShadows );

			box.ChangeFlags( SceneObjectFlags.MaterialSupportsShadows, SceneObjectFlags.CastShadows );
			MirrorType.GetMethod( "Sync", Any ).Invoke( mirror, [camera, false] );
			Assert.IsTrue( box.Flags.CastShadows, "the flag reads as on with either bit" );
			Assert.IsFalse( mesh.CastShadows, "casting isn't enabled" );
		}
		finally
		{
			mirror.Dispose();
			camera.Dispose();
			world.Delete();
		}
	}
}
