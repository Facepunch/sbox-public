using Editor;
using Sandbox.Rendering;
using System;

namespace SceneTests;

/// <summary>
/// The stand-ins a native tool view's lights get on Borealis (<see cref="ToolSceneViews.ToolLights"/>).
/// </summary>
[TestClass]
public class ToolLightsTests
{
	/// <summary>
	/// ModelDoc's default rig has two directional lights, its own "Sun" and the preview map's <c>light_environment</c> after
	/// it. Native's binner keeps only the last directional light, so the view is lit by one sun, the last one's, and none
	/// for the others.
	/// </summary>
	[TestMethod]
	public void OnlyTheLastDirectionalLightLights()
	{
		var native = new SceneWorld();
		var lights = new ToolSceneViews.ToolLights();
		try
		{
#pragma warning disable CS0618 // native's lights are plain SceneLights
			var rigSun = new SceneLight( native ) { LightColor = Color.Red };
			var mapSun = new SceneLight( native ) { LightColor = Color.Blue };
#pragma warning restore CS0618

			Frame( lights, rigSun, mapSun );
			var suns = Suns( lights );
			Assert.AreEqual( 1, suns.Length );
			Assert.AreEqual( Color.Blue, suns[0].LightColor );

			// The map's light gone: the rig's own is the last, and the sun
			Frame( lights, rigSun );
			suns = Suns( lights );
			Assert.AreEqual( 1, suns.Length );
			Assert.AreEqual( Color.Red, suns[0].LightColor );

			Frame( lights );
			Assert.AreEqual( 0, Suns( lights ).Length );
		}
		finally
		{
			lights.Dispose();
			native.Delete();
		}
	}

	static void Frame( ToolSceneViews.ToolLights lights, params SceneLight[] directionals )
	{
		lights.BeginFrame();
		foreach ( var light in directionals ) lights.MirrorLight( light, ShadowLightType.Directional );
		lights.EndFrame();
	}

	static SceneDirectionalLight[] Suns( ToolSceneViews.ToolLights lights )
	{
		lock ( lights.World.InternalSceneObjects )
			return lights.World.InternalSceneObjects.OfType<SceneDirectionalLight>().Where( x => x.IsValid() && x.RenderingEnabled ).ToArray();
	}
}
