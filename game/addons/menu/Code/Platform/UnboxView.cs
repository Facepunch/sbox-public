using Sandbox;
using Sandbox.UI;

namespace MenuProject.Platform;

/// <summary>
/// The reward unbox's scene, drawn in a panel - made fresh each time it's opened, gone when it's put away. Its camera
/// renders at the panel's size, and the pointer over it is handed to the crate and cards (<see cref="PointerRay"/>,
/// <see cref="Presses"/>) - the scene's in a panel, not over the screen, so they don't read the mouse themselves.
/// </summary>
public sealed class UnboxView : Panel
{
	const string ScenePath = "/scenes/reward-unbox.scene";

	/// <summary>
	/// Into the scene from under the pointer - null when it's not over it
	/// </summary>
	public static Ray? PointerRay { get; private set; }

	/// <summary>
	/// How many times it's been pressed on the scene - something that remembers the last count sees a new press
	/// </summary>
	public static int Presses { get; private set; }

	ScenePanel view;

	public Scene RenderScene => view?.RenderScene;

	public void Open()
	{
		Close();

		view = new ScenePanel( ScenePath );
		view.AddClass( "render" );
		AddChild( view );
	}

	public void Close()
	{
		view?.Delete( true );
		view = null;
		PointerRay = null;
	}

	public override void Tick()
	{
		base.Tick();

		if ( RenderScene is not { } scene || !scene.IsValid() || scene.Camera is not { } camera ) return;

		var size = Box.Rect.Size;
		if ( size.x <= 0 || size.y <= 0 ) return;

		camera.CustomSize = size;
		PointerRay = HasHovered ? camera.ScreenPixelToRay( MousePosition ) : null;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton == MouseButtons.Left )
			Presses++;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();
		PointerRay = null;
	}
}
