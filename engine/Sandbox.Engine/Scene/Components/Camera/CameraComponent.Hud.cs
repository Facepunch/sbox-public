using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class CameraComponent : Component, Component.ExecuteInEditor
{
	CommandList _hudCommandList = new CommandList( "Hud" ) { Flags = CommandList.Flag.Hud };

	/// <summary>
	/// Allows drawing on the camera after post processing.
	/// </summary>
	[Obsolete( "Use BeginHud() instead. Dispose the painter to submit its drawing." )]
	public HudPainter Hud => new HudPainter( _hudCommandList );

	/// <summary>
	/// Starts drawing on the camera after post processing. Bounds start at (0, 0) and use the camera's viewport size in pixels.
	/// Dispose the painter to append its drawing. Commands are reset at the start of each frame.
	/// Finish painting before recording other commands into the HUD command list.
	/// </summary>
	public Painter BeginHud() => Painter.Begin( _hudCommandList, new Rect( Vector2.Zero, ScreenRect.Size ) );

	CommandList _overlayCommandList = new CommandList( "Overlay" ) { Flags = CommandList.Flag.Hud };

	/// <summary>
	/// Used to draw to the screen. This is drawn on top of everything, so is good for debug overlays etc.
	/// </summary>
	[Obsolete( "Use BeginOverlay() instead. Dispose the painter to submit its drawing." )]
	public HudPainter Overlay => new HudPainter( _overlayCommandList );

	/// <summary>
	/// Starts drawing on the camera after UI. Bounds start at (0, 0) and use the camera's viewport size in pixels.
	/// Dispose the painter to append its drawing. Commands are reset at the start of each frame.
	/// Finish painting before recording other commands into the overlay command list.
	/// </summary>
	public Painter BeginOverlay() => Painter.Begin( _overlayCommandList, new Rect( Vector2.Zero, ScreenRect.Size ) );

	/// <summary>
	/// Commands drawn after post processing. Reset at the start of each frame.
	/// </summary>
	public CommandList HudCommandList => _hudCommandList;

	/// <summary>
	/// Commands drawn after UI. Reset at the start of each frame.
	/// </summary>
	public CommandList OverlayCommandList => _overlayCommandList;

	void Component.ISceneStage.Start()
	{
		// Clear the HUD at the start of every frame, so that
		// subsequent Update()'s will give it fresh data.
		_hudCommandList.Reset();
		_overlayCommandList.Reset();
	}
}
