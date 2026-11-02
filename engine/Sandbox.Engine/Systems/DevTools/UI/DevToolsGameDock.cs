using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The dev tools along the bottom of the game window: the dev tools themselves, under a grip that drags their height.
/// The rest of the window is left to the game - the surface's root takes no input, only this does.
/// </summary>
internal sealed class DevToolsGameDock : Panel
{
	/// <summary>
	/// Room left for the game above the dock, at the least.
	/// </summary>
	const float GameMinHeight = 80;

	readonly Panel _grip;
	bool _dragging;

	public DevToolsGameDock( DevToolsWindow window )
	{
		AddClass( "game-dock" );

		_grip = AddChild( new Panel( null, "dock-grip" ) );
		_grip.AddEventListener( "onmousedown", e =>
		{
			if ( e is not MousePanelEvent { Button: "mouseleft" } ) return;
			e.StopPropagation();
			_dragging = true;
		} );
		_grip.AddEventListener( "onmouseup", () => _dragging = false );

		AddChild( window );
	}

	public override void Tick()
	{
		base.Tick();

		if ( Parent is not { } root ) return;

		var rootHeight = root.Box.Rect.Height * ScaleFromScreen;
		if ( rootHeight <= 0 ) return;

		if ( _dragging )
			DevTools.DockHeight = rootHeight - root.MousePosition.y * ScaleFromScreen;

		_grip.SetClass( "dragging", _dragging );

		var max = MathF.Max( 120, rootHeight - GameMinHeight );
		Style.Height = Math.Clamp( DevTools.DockHeight, 120, max );
	}
}
