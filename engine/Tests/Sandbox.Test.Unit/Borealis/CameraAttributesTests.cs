using Sandbox.Rendering.Bridge;

namespace BorealisTests;

/// <summary>
/// A Borealis view takes its camera's attributes (<see cref="MirrorContent.CopyChanged"/>), as native's camera renderer gives
/// its view them: what <c>Scene.RenderAttributes</c> set reaches the shaders (gamma's zone values, Rust Legacy's time of day),
/// kept current by copying only what changed, and everything again once something went.
/// </summary>
[TestClass]
public class CameraAttributesTests
{
	static readonly System.Type ParametersType = typeof( Borealis.Backend.ShaderDrawer ).Assembly.GetType( "Borealis.Backend.BackendParameters" );

	static object Get( Borealis.ShaderParameters parameters, string name ) => ParametersType.GetMethod( "Get" ).Invoke( parameters, [new StringToken( name )] );
	static int? GetCombo( Borealis.ShaderParameters parameters, string name ) => (int?)ParametersType.GetMethod( "GetCombo" ).Invoke( parameters, [new StringToken( name )] );

	[TestMethod]
	public void ViewsKeepTheirCamerasAttributes()
	{
		var camera = RenderAttributes.CreateManaged();
		camera.Set( "TodSunDir", new Vector3( 0, 0, 1 ) );
		camera.Set( "ZoneCutZ", 128.0f );
		camera.SetCombo( "D_ZONE_PLAIN", 1 );

		var view = (Borealis.ShaderParameters)System.Activator.CreateInstance( ParametersType, nonPublic: true );
		var version = MirrorContent.NeverCopied;
		MirrorContent.CopyChanged( camera, view, ref version );

		Assert.AreEqual( new Vector3( 0, 0, 1 ), Get( view, "TodSunDir" ) );
		Assert.AreEqual( 128.0f, Get( view, "ZoneCutZ" ) );
		Assert.AreEqual( 1, GetCombo( view, "D_ZONE_PLAIN" ) );

		// What changed since, alone
		camera.Set( "ZoneCutZ", 64.0f );
		MirrorContent.CopyChanged( camera, view, ref version );
		Assert.AreEqual( 64.0f, Get( view, "ZoneCutZ" ) );
		Assert.AreEqual( new Vector3( 0, 0, 1 ), Get( view, "TodSunDir" ) );

		// A cleared set leaves nothing behind in the view
		camera.Clear();
		camera.Set( "ZoneCutZ", 32.0f );
		MirrorContent.CopyChanged( camera, view, ref version );
		Assert.AreEqual( 32.0f, Get( view, "ZoneCutZ" ) );
		Assert.IsNull( Get( view, "TodSunDir" ), "a value the camera no longer has" );
		Assert.IsNull( GetCombo( view, "D_ZONE_PLAIN" ) );
	}
}
