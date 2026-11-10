using Sandbox.Diagnostics;

namespace Editor;

/// <summary>
/// Dev tools (<c>devtools</c>, F11) in an editor dock. In the editor they live here rather than in a window of their
/// own: the dock draws the same panels on a <see cref="PanelSurfaceWidget"/>, and opening dev tools opens this dock.
/// They count as open while the dock is on screen, so the data behind them is only collected while someone can see it.
/// </summary>
[Dock( "Editor", Title, "speed" )]
public class DevToolsDock : Widget
{
	const string Title = "Dev Tools";

	/// <summary>
	/// Shorter than this, a dock shows nothing but the dev tools' tab strip.
	/// </summary>
	const float UsableHeight = 160;

	readonly PanelSurfaceWidget _surface;
	bool _built;
	bool _showing;

	public DevToolsDock( Widget parent ) : base( parent )
	{
		MinimumSize = new Vector2( 320, UsableHeight );
		Layout = Layout.Column();

		_surface = Layout.Add( new PanelSurfaceWidget( this ), 1 );
		_surface.MaxFramesPerSecond = 30;
		_surface.FrameTiming = DevTools.Timing;
		_surface.BackgroundColor = new Color( 0.086f, 0.086f, 0.086f );
		_surface.OnPreFrame = PreFrame;
	}

	/// <summary>
	/// Make opening dev tools open this dock. Called once the editor's docks are registered.
	/// </summary>
	internal static void Install( EditorMainWindow window )
	{
		DevTools.ShowDock = show =>
		{
			if ( !window.IsValid() ) return;

			var docks = window.DockManager;
			docks.SetDockState( Title, show );
			if ( !show ) return;

			// It goes in with the console - wide and short, the shape the docked layouts are made for - unless it's
			// floating or somewhere it has room. A dock squeezed to a strip shows nothing but its tabs.
			if ( docks.FindDockWidget( Title ) is { } dock && !dock.IsFloating && dock.Size.y < UsableHeight
				&& docks.FindDockWidget( "Console" ) is { } console && console != dock )
				docks.AddDock( dock, console );

			docks.RaiseDock( Title );
		};
	}

	/// <summary>
	/// Just before the surface draws: it's on screen, so dev tools are open. They're built the first time.
	/// </summary>
	void PreFrame()
	{
		SetShowing( true );

		if ( !_built )
		{
			_built = true;
			DevTools.BuildRoot( _surface.Surface.Root, DevTools.Host.EditorDock );
		}

		_surface.Surface.Scale = DevTools.WindowScale;
	}

	/// <summary>
	/// The surface only draws while it's visible, so notice here when it stops - the dock closed, or another tab in its
	/// group came to the front - and let dev tools stop collecting.
	/// </summary>
	[EditorEvent.Frame]
	public void Frame()
	{
		if ( _showing && !_surface.Visible ) SetShowing( false );
	}

	void SetShowing( bool showing )
	{
		if ( _showing == showing ) return;
		_showing = showing;
		DevTools.SetDocked( showing );
	}

	public override void OnDestroyed()
	{
		SetShowing( false );
		base.OnDestroyed();
	}
}
