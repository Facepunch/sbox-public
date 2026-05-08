using Sandbox.Rendering;
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

	private readonly CommandList _commandList = new( "ScenePanel" );

	public ScenePanelObject( SceneWorld world, RootPanel Panel ) : base( world )
	{
		this.Panel = Panel;
	}

	/// <summary>
	/// Called on the main thread to snapshot the world matrix before render.
	/// </summary>
	internal void BuildCommandList()
	{
		//
		// This converts it to front left up (instead of right, down, whatever)
		// and we apply a sensible enough default scale.
		// Then bake in the scene object's world transform so the shader
		// doesn't need to read from the instancing transform buffer.
		//
		_commandList.Reset();

		Matrix mat = Matrix.CreateRotation( Rotation.From( 0, 90, 90 ) );
		mat *= Matrix.CreateScale( ScreenToWorldScale );
		mat *= Matrix.CreateScale( Transform.Scale );
		mat *= Matrix.CreateRotation( Transform.Rotation );
		mat *= Matrix.CreateTranslation( Transform.Position );

		_commandList.Attributes.SetCombo( "D_WORLDPANEL", 1 );
		_commandList.Attributes.Set( "WorldMat", mat );
	}

	public override void RenderSceneObject()
	{
		_commandList.ExecuteOnRenderThread();
		Panel?.Render();
	}
}
