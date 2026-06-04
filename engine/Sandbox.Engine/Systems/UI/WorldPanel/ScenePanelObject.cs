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

	internal static Matrix BuildPanelToObjectMatrix()
	{
		Matrix mat = Matrix.CreateRotation( Rotation.From( 0, 90, 90 ) );
		mat *= Matrix.CreateScale( ScreenToWorldScale );
		return mat;
	}

	internal static Matrix BuildPanelToWorldMatrix( Transform transform )
	{
		return BuildPanelToObjectMatrix() * Matrix.FromTransform( transform );
	}

	/// <summary>
	/// Called on the main thread to snapshot the world matrix before render.
	/// </summary>
	internal void BuildCommandList()
	{
		//
		// This converts it to front left up (instead of right, down, whatever)
		// and we apply a sensible enough default scale.
		//
		_commandList.Reset();

		_commandList.Attributes.SetCombo( "D_WORLDPANEL", 1 );
		_commandList.Attributes.Set( "WorldMat", BuildPanelToWorldMatrix( Transform ) );
	}

	public override void RenderSceneObject()
	{
		_commandList.ExecuteOnRenderThread();
		Panel?.Render();
	}
}
