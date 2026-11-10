using Sandbox.UI;

namespace Sandbox.SceneLab;

/// <summary>
/// The see-through part of the window the world is drawn in. Left drag orbits the camera - the
/// panel captures the mouse, so the cursor stays put and <see cref="Mouse.Delta"/> drives the
/// orbit - and the wheel zooms.
/// </summary>
internal sealed class SceneViewport : Panel
{
	readonly SceneLabWindow window;

	public SceneViewport( SceneLabWindow window )
	{
		this.window = window;
		AddClass( "sceneviewport" );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.MouseButton == MouseButtons.Left ) SetMouseCapture( true );
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( e.MouseButton == MouseButtons.Left ) SetMouseCapture( false );
	}

	public override void Tick()
	{
		base.Tick();

		if ( !HasMouseCapture ) return;

		var delta = Mouse.Delta;
		window.Yaw -= delta.x * 0.3f;
		window.Pitch = Math.Clamp( window.Pitch + delta.y * 0.3f, -89, 89 );
	}

	public override void OnMouseWheel( Vector2 value )
	{
		window.Distance *= MathF.Pow( 0.9f, -value.y );
	}
}
