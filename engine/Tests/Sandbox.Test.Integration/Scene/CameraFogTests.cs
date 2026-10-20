using Sandbox.Rendering;

namespace SceneTests;

/// <summary>
/// Checks camera and capture fog precedence without component or volume resolution.
/// </summary>
[TestClass]
public class CameraFogTests
{
	/// <summary>
	/// Reflection and refraction overrides win without mutating the shared camera, even when post processing is off.
	/// </summary>
	[TestMethod]
	public void CaptureOverridesTakePrecedenceWithoutMutatingCamera()
	{
		using var camera = new SceneCamera();
		camera.Fog = new FogSetup { Enabled = true, Color = Color.Blue, Weight = 1 };

		Assert.AreEqual( Color.Blue, camera.ResolveFog( default ).Value.Color );
		Assert.AreEqual( Color.Blue, camera.ResolveFog( new ViewSetup { EnablePostprocessing = false } ).Value.Color,
			"Reflections disabling fullscreen effects retain material fog" );
		Assert.IsNull( camera.ResolveFog( new ViewSetup { GradientFog = new GradientFogSetup { Enabled = false } } ),
			"Existing captures explicitly disabling legacy fog must not acquire new camera fog" );

		var capture = new ViewSetup
		{
			EnablePostprocessing = false,
			GradientFog = new GradientFogSetup { Enabled = true },
			Fog = new FogSetup { Enabled = false, Weight = 1 }
		};
		var resolved = camera.ResolveFog( capture );

		Assert.IsNotNull( resolved );
		Assert.IsFalse( resolved.Value.Enabled );
		Assert.AreEqual( 1.0f, resolved.Value.Weight );
		Assert.AreEqual( Color.Blue, camera.Fog.Value.Color );
		Assert.IsTrue( camera.Fog.Value.Enabled );

		camera.EnablePostProcessing = false;
		Assert.IsNull( camera.ResolveFog( default ) );
		Assert.IsNotNull( camera.ResolveFog( capture ), "An explicit capture override wins even when its camera has post processing disabled" );
	}
}
