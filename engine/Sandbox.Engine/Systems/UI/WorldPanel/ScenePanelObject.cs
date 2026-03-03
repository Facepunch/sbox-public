using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Renders a panel in a scene world. You are probably looking for <a href="https://sbox.game/api/Sandbox.UI.WorldPanel">WorldPanel</a>.
/// </summary>
internal sealed class ScenePanelObject : SceneCustomObject
{
	/// <summary>
	/// Global scale for panel rendering within a scene world.
	/// </summary>
	public const float ScreenToWorldScale = 0.05f;

	/// <summary>
	/// The panel that will be rendered.
	/// </summary>
	public RootPanel Panel { get; private set; }

	public ScenePanelObject( SceneWorld world, RootPanel Panel ) : base( world )
	{
		this.Panel = Panel;
	}

	public override void RenderSceneObject()
	{
		Graphics.Attributes.SetCombo( "D_WORLDPANEL", 1 );

		//
		// This converts it to front left up (instead of right, down, whatever)
		// and we apply a sensible enough default scale.
		//
		Matrix mat = Matrix.CreateRotation( Rotation.From( 0, 90, 90 ) );
		mat *= Matrix.CreateScale( ScreenToWorldScale );
		Graphics.Attributes.Set( "WorldMat", mat );

		Panel?.RenderManual();
	}
}
