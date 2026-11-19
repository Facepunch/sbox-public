namespace EngineTests;

/// <summary>
/// Settings read from native convars every frame are kept until a convar changes, and native's own changes count: a convar
/// set inside native (a cfg exec, its console) moves <c>ConVarSystem.Changes</c> through its global change listener.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ConVarChangeTests
{
	[TestMethod]
	public void NativeChangesReachTheUpscalerAtOnce()
	{
		var upscaling = ConVarSystem.Find( "r_upscaling" );
		var renderScale = ConVarSystem.Find( "r_render_scale" );
		Assert.IsNotNull( upscaling, "r_upscaling" );
		Assert.IsNotNull( renderScale, "r_render_scale" );

		var (previousUpscaling, previousScale) = (upscaling.Value, renderScale.Value);
		try
		{
			ConVarSystem.SetValue( "r_upscaling", "0", true );
			ConVarSystem.SetValue( "r_render_scale", "1", true );
			Assert.AreEqual( 1.0f, BorealisGpuUpscaler.Settings().Scale );

			// Set straight on the native convar, past ConVarSystem.SetValue: only native's listener sees it
			var before = ConVarSystem.Changes;
			renderScale.Value = "0.5";
			Assert.AreNotEqual( before, ConVarSystem.Changes, "native's change is counted" );

			var (mode, scale) = BorealisGpuUpscaler.Settings();
			Assert.AreEqual( 0.5f, scale, "read again the next time" );
			Assert.AreEqual( BorealisGpuUpscaler.Mode.Stretch, mode );
		}
		finally
		{
			ConVarSystem.SetValue( "r_upscaling", previousUpscaling, true );
			ConVarSystem.SetValue( "r_render_scale", previousScale, true );
		}
	}
}
