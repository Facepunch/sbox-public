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
}
