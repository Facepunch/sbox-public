using System;

namespace SceneTests;

/// <summary>
/// Lighting the managed renderer's mirror takes from a world and its extra worlds, as native's light binner takes it.
/// </summary>
[TestClass]
public class SceneMirrorLightingTests
{
	static readonly Type MirrorType = Type.GetType( "Sandbox.Rendering.Bridge.SceneMirror, Sandbox.Rendering", true );

	static (IDisposable Mirror, Borealis.RenderWorld World) Mirror( SceneWorld world ) => Mirror( [world] );

	static (IDisposable Mirror, Borealis.RenderWorld World) Mirror( object[] arguments )
	{
		var mirror = (IDisposable)Activator.CreateInstance( MirrorType, arguments );
		return (mirror, (Borealis.RenderWorld)MirrorType.GetProperty( "World" ).GetValue( mirror ));
	}

	static void Sync( IDisposable mirror, SceneCamera camera ) => MirrorType.GetMethod( "Sync" ).Invoke( mirror, [camera, false] );

	/// <summary>
	/// A world with several directional lights has one sun, the last enabled one added, as native's binner keeps the last
	/// directional light in its list (the others light as extra suns, <see cref="OtherDirectionalLightsAreExtraSuns"/>).
	/// </summary>
	[TestMethod]
	public void TheLastEnabledDirectionalLightIsTheSun()
	{
		var world = new SceneWorld();
		try
		{
			var first = new SceneDirectionalLight( world, Rotation.From( 45, 0, 0 ), Color.Red );
			var last = new SceneDirectionalLight( world, Rotation.From( 60, 90, 0 ), Color.Blue );
			using var camera = new SceneCamera { World = world };
			var (mirror, renderWorld) = Mirror( world );
			using var disposeMirror = mirror;

			Sync( mirror, camera );
			Assert.AreEqual( Color.Blue, renderWorld.Lighting.SunColor );

			last.RenderingEnabled = false;
			Sync( mirror, camera );
			Assert.AreEqual( Color.Red, renderWorld.Lighting.SunColor );

			last.RenderingEnabled = true;
			first.Delete();
			Sync( mirror, camera );
			Assert.AreEqual( Color.Blue, renderWorld.Lighting.SunColor );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// Native lights with the sun alone; here every other enabled directional light lights too, as an extra sun, in the order
	/// they were added.
	/// </summary>
	[TestMethod]
	public void OtherDirectionalLightsAreExtraSuns()
	{
		var world = new SceneWorld();
		try
		{
			var first = new SceneDirectionalLight( world, Rotation.From( 45, 0, 0 ), Color.Red );
			var second = new SceneDirectionalLight( world, Rotation.From( 30, 45, 0 ), Color.Green ) { ShadowsEnabled = false };
			var last = new SceneDirectionalLight( world, Rotation.From( 60, 90, 0 ), Color.Blue );
			using var camera = new SceneCamera { World = world };
			var (mirror, renderWorld) = Mirror( world );
			using var disposeMirror = mirror;
			var lighting = renderWorld.Lighting;

			Sync( mirror, camera );
			Assert.AreEqual( Color.Blue, lighting.SunColor );
			Assert.AreEqual( 2, lighting.ExtraSuns.Count );
			Assert.AreEqual( Color.Red, lighting.ExtraSuns[0].Color );
			Assert.AreEqual( Color.Green, lighting.ExtraSuns[1].Color );
			Assert.IsTrue( lighting.ExtraSuns[0].Direction.Distance( -first.WorldDirection ) < 1e-5f, "the way its light travels, as the sun's" );
			Assert.IsTrue( lighting.ExtraSuns[0].Shadows );
			Assert.IsFalse( lighting.ExtraSuns[1].Shadows );

			second.RenderingEnabled = false;
			Sync( mirror, camera );
			Assert.AreEqual( 1, lighting.ExtraSuns.Count );
			Assert.AreEqual( Color.Red, lighting.ExtraSuns[0].Color );

			// The last one turned off: the one before it is the sun, and no other is left
			last.RenderingEnabled = false;
			Sync( mirror, camera );
			Assert.AreEqual( Color.Red, lighting.SunColor );
			Assert.AreEqual( 0, lighting.ExtraSuns.Count );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// An extra world's light probe volume goes in the main world's list with the extra world's sub-world, so it lights the
	/// views that draw that world (a native tool's preview map lights the model it shows).
	/// </summary>
	[TestMethod]
	public void ExtraWorldProbeVolumesLightViewsDrawingThem()
	{
		var world = new SceneWorld();
		var extra = new SceneWorld();
		try
		{
			using var texture = Texture.CreateVolume( 2, 2, 12 ).Finish();
			var probe = new SceneLightProbe( extra, texture, null, null, new BBox( new Vector3( -100 ), new Vector3( 100 ) ), Transform.Zero, 0, 0 );
			using var camera = new SceneCamera { World = world };
			var (host, hostWorld) = Mirror( world );
			using var disposeHost = host;
			var (mirror, sameWorld) = Mirror( [extra, hostWorld, 3] );
			using var disposeMirror = mirror;
			Assert.AreSame( hostWorld, sameWorld );

			Sync( host, camera );
			Sync( mirror, camera );
			Assert.AreEqual( 1, hostWorld.LightProbeVolumes.Count );

			var volume = hostWorld.LightProbeVolumes[0];
			Assert.AreEqual( 3, volume.SubWorld );
			Assert.IsTrue( volume.ShownIn( 1UL << 2 ), "a view drawing the extra world" );
			Assert.IsFalse( volume.ShownIn( 0 ), "a view drawing only the main world" );

			probe.Delete();
			Sync( mirror, camera );
			Assert.AreEqual( 0, hostWorld.LightProbeVolumes.Count );
		}
		finally
		{
			extra.Delete();
			world.Delete();
		}
	}
}
