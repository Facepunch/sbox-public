namespace BorealisTests;

/// <summary>
/// Custom objects drawn in a Borealis.Gpu frame (a terrain, a dynamic object) read the view's settings the frame's own draws
/// read from its attributes (<see cref="BorealisGpuFrame.CopyViewSettings"/>), as native's layer gives its custom objects the
/// view's: Mow the Lawn's terrain sampled its shadows with no filter (ShadowFilterQuality 0) while the models around it did.
/// </summary>
[TestClass]
public class CustomDrawViewSettingsTests
{
	[TestMethod]
	public void CustomDrawsTakeTheFramesViewSettings()
	{
		// The frame's attributes, as Borealis's render system and shadow system set them
		var type = typeof( Borealis.Backend.ShaderDrawer ).Assembly.GetType( "Borealis.Backend.BackendParameters" );
		var frame = (Borealis.ShaderParameters)System.Activator.CreateInstance( type, nonPublic: true );
		frame.Set( "ShadowFilterQuality", 2 );
		frame.Set( "ToolsVisMode", 10 );
		frame.Set( "LightModulatorsDisabled", true );
		frame.Set( "PerViewConstantBuffer_t", 1.0f );

		var block = RenderAttributes.CreateManaged();
		BorealisGpuFrame.CopyViewSettings( frame, block );

		Assert.AreEqual( 2, block.GetInt( "ShadowFilterQuality" ) );
		Assert.AreEqual( 10, block.GetInt( "ToolsVisMode" ) );
		Assert.IsTrue( block.GetBool( "LightModulatorsDisabled" ) );
		Assert.AreEqual( 0f, block.GetFloat( "PerViewConstantBuffer_t" ), "only the view's settings" );
		Assert.AreEqual( 0, block.GetInt( "DDGI_Enabled" ), "what the frame doesn't set stays unset" );
	}
}
