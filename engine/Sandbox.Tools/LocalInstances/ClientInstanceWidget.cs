using System;

namespace Editor;

/// <summary>
/// A dockable "Client N" tab hosting an in-process client session. The session is a genuine
/// multiplayer client (own scene, own connection, real handshake) living inside the editor
/// process - see <see cref="Sandbox.InProcessClientSession"/> - so it shares all loaded
/// assemblies and resources with the host and joins near-instantly. The tab renders the
/// client's scene through its own camera, exactly like the game view renders the host's.
///
/// The session is created lazily: if the editor wasn't hosting when the tab was added
/// (AddDockedClient auto-starts hosting), the tab waits for the host to come up and then
/// connects itself - one click, no external process, no extra steps.
/// </summary>
public class ClientInstanceWidget : Widget
{
	/// <summary>
	/// All live docked client tabs, for focus switching (Shift+F1..F12).
	/// </summary>
	internal static readonly List<ClientInstanceWidget> All = new();

	/// <summary>
	/// Give input focus to docked client <paramref name="number"/>, raising its tab.
	/// If it already has focus, hand input back to the host instead. Returns false if
	/// no such client exists.
	/// </summary>
	internal static bool FocusInstance( int number )
	{
		var widget = All.FirstOrDefault( w => w.IsValid() && w.InstanceNumber == number );

		if ( widget?._session is null )
			return false;

		if ( Sandbox.InProcessClientSession.Focused == widget._session )
		{
			widget.ReleaseInputFocus();
		}
		else
		{
			EditorWindow.DockManager.RaiseDock( widget );
			widget.ClaimInputFocus();
		}

		widget.Update();
		return true;
	}

	Sandbox.InProcessClientSession _session;
	readonly SceneRenderingWidget _renderer;

	RealTimeSince _waitingForHost;

	/// <summary>
	/// The "Client N" number for the tab title. 0 until the session exists.
	/// </summary>
	public int InstanceNumber => _session?.Number ?? 0;

	internal ClientInstanceWidget() : base( null )
	{
		Layout = Layout.Row();

		// A slim frame around the render view - painted green while this client has
		// input focus, so you can always tell who you're driving.
		Layout.Margin = 2;

		_renderer = new SceneRenderingWidget( this );
		_renderer.Visible = false;

		// Stretch factor so the render view fills the whole tab, not its preferred size.
		Layout.Add( _renderer, 1 );

		All.Add( this );

		// Claim input the moment the render view actually gets focus (a click) - polling
		// IsFocused once a frame misses transitions and made claiming feel unreliable.
		_renderer.Focused += _ => ClaimInputFocus();

		DeleteOnClose = true;
		FocusMode = FocusMode.Click;
		MinimumSize = new Vector2( 320, 240 );

		_waitingForHost = 0;
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( !this.IsValid() )
			return;

		if ( _session is null )
		{
			WaitForHost();
			return;
		}

		// Kicked, disconnected, or the host went away - close the tab.
		if ( _session.IsDefunct )
		{
			_session = null;
			Destroy();
			return;
		}

		// The host's game code changed (hotload) - this session's private assembly copies
		// are stale. Rebuild: dispose and reconnect in-process, which is near-instant.
		if ( _session.NeedsRebuild )
		{
			_refocusOnConnect = Sandbox.InProcessClientSession.Focused == _session;
			_rebuilding = true;
			_reportedNoCamera = false;

			_session.Dispose();
			_session = null;
			_waitingForHost = 0;

			Update();
			return;
		}

		// If the host stopped (editor Stop button), shut the client down with it.
		if ( !Game.IsPlaying || Sandbox.Networking.System is null || !Sandbox.Networking.System.IsHost )
		{
			Destroy();
			return;
		}

		UpdateInputFocus();

		// The session's world reports this view's size through Screen while it ticks,
		// so its UI lays out - and its game projects - against the actual dock size.
		_session.ViewSize = _renderer.Size * _renderer.DpiScale;

		// Advance this client one frame: networking handshake/messages + scene simulation.
		_session.Tick();

		// Show the client's scene as soon as the snapshot created it.
		var scene = _session.Scene;

		if ( _renderer.Scene != scene )
		{
			_renderer.Scene = scene;
		}

		var showScene = scene.IsValid();

		if ( _renderer.Visible != showScene )
		{
			_renderer.Visible = showScene;
			Update();
		}

		// A connected client whose scene has no active camera renders a black tab -
		// say so once, because from the outside it just looks broken.
		if ( showScene && _session.IsConnected && !_reportedNoCamera )
		{
			_reportedNoCamera = true;

			if ( !scene.Camera.IsValid() )
			{
				Log.Warning( $"Docked client {_session.Number}: the scene has no active camera, so this tab will render black. " +
					"If the game creates its camera from code, check its client-side spawn path. " +
					"Compare against shared mode with 'docked_client_isolation 0' (recreate the tab after changing it)." );
			}
		}

		// Repaint the focus frame when the focused session changes.
		var focusedNow = Sandbox.InProcessClientSession.Focused == _session;
		if ( focusedNow != _paintedFocused )
		{
			_paintedFocused = focusedNow;
			Update();
		}
	}

	bool _hasInputFocus;
	bool _rebuilding;
	bool _refocusOnConnect;
	bool _reportedNoCamera;
	bool _paintedFocused;

	/// <summary>
	/// Click the client's view to drive that client with your keyboard/mouse; click the host's
	/// game view (or another client) to give input back. The claim is LATCHED: transient Qt
	/// focus changes (mouse capture, engine focus juggling) must not silently return input to
	/// the host - only an explicit click into the game view or another client releases it.
	/// Claiming registers the renderer with SDL so the engine keeps capturing game input for
	/// this window - the same hookup GameMode does for the play widget, minus the engine-state
	/// rerouting so the host's HUD stays where it belongs.
	/// </summary>
	nint _registeredWinId;

	void ClaimInputFocus()
	{
		if ( _session is null || !_renderer.IsValid() )
			return;

		// Transitions must be ordered: the previous owner's cleanup (focus-off,
		// unregister) has to happen BEFORE our claim, or its next-frame release
		// clobbers our freshly registered window and inputs go dead.
		foreach ( var other in All )
		{
			if ( other != this && other._hasInputFocus )
				other.ReleaseInputFocus( restoreHost: false );
		}

		Sandbox.InProcessClientSession.Focused = _session;

		RegisterInputWindow( _renderer._widget.winId() );
		_hasInputFocus = true;
	}

	void RegisterInputWindow( nint winId )
	{
		if ( _registeredWinId == winId )
			return;

		UnregisterInputWindow();

		NativeEngine.InputSystem.RegisterWindowWithSDL( winId );
		NativeEngine.InputSystem.OnEditorGameFocusChange( winId, true );

		// The engine's window state is what ties SDL input routing (buttons, capture)
		// to a window - the same hookup the play widget gets. Without it clicks are
		// interpreted against the play widget's window and land erratically.
		GameMode.SetEngineStateWindow( winId, _renderer.SwapChain );

		_registeredWinId = winId;
	}

	void UnregisterInputWindow( bool restoreHost = true )
	{
		if ( _registeredWinId == 0 )
			return;

		NativeEngine.InputSystem.OnEditorGameFocusChange( _registeredWinId, false );
		NativeEngine.InputSystem.UnregisterWindowFromSDL( _registeredWinId );
		_registeredWinId = 0;

		// Hand the engine window state and game focus back to the host's game view -
		// unless another docked client is claiming right behind us.
		if ( restoreHost )
		{
			GameMode.RestoreEngineState();
		}
	}

	void UpdateInputFocus()
	{
		var claimed = Sandbox.InProcessClientSession.Focused == _session;

		if ( !_hasInputFocus )
			return;

		// Another client tab claimed the input away from us.
		if ( !claimed )
		{
			ReleaseInputFocus();
			return;
		}

		// Escape always hands input back to the host - reaching the game view with a
		// click can be a fight when the focused client's game has mouse capture.
		if ( Sandbox.Input.EscapePressed )
		{
			Sandbox.Input.EscapePressed = false;
			ReleaseInputFocus();
			return;
		}

		// The player clicked back into the host's game view, or hid this tab.
		if ( GameMode.PlayWidgetFocused || !Visible )
		{
			ReleaseInputFocus();
			return;
		}

		// Undocking the tab into its own window reparents it to a new native window -
		// keep the SDL registration pointed at the window the view actually lives in.
		if ( _renderer.IsValid() && _renderer._widget.IsValid )
		{
			RegisterInputWindow( _renderer._widget.winId() );
		}
	}

	void ReleaseInputFocus( bool restoreHost = true )
	{
		if ( !_hasInputFocus )
			return;

		_hasInputFocus = false;

		if ( _session is not null && Sandbox.InProcessClientSession.Focused == _session )
			Sandbox.InProcessClientSession.Focused = null;

		UnregisterInputWindow( restoreHost );

		// If the client's game had captured/hidden the mouse, give it back.
		Mouse.Visibility = MouseVisibility.Auto;

		// Keep Qt's focus state in agreement, so the next click is a clean re-claim.
		if ( _renderer.IsValid() )
			_renderer.Blur();
	}

	/// <summary>
	/// Hosting was still starting up when this tab was added - connect as soon as it's ready.
	/// </summary>
	void WaitForHost()
	{
		var host = Sandbox.Networking.System;

		if ( host is { IsHost: true } && host.GameSystem is not null )
		{
			try
			{
				_session = Sandbox.InProcessClientSession.Create();
				WindowTitle = $"Client {_session.Number}";

				// This tab had input focus before a code-change rebuild - keep it.
				if ( _refocusOnConnect && _hasInputFocus )
				{
					Sandbox.InProcessClientSession.Focused = _session;
				}

				_rebuilding = false;
				_refocusOnConnect = false;

				Update();
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Failed to create docked client: {e.Message}" );
				Destroy();
			}

			return;
		}

		// Connected somewhere as a client, or hosting never came up - nothing to attach to.
		if ( (host is not null && !host.IsHost) || _waitingForHost > 30f )
		{
			Log.Warning( "Docked client tab closed - the editor never became a host" );
			Destroy();
		}
	}

	protected override void OnPaint()
	{
		// The frame around the render view: green while this client has input focus.
		var focused = _session is not null && Sandbox.InProcessClientSession.Focused == _session;

		Paint.ClearPen();
		Paint.SetBrush( focused ? Theme.Green : Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );

		Paint.SetPen( Theme.TextLight );

		if ( _session is null )
		{
			Paint.DrawText( LocalRect, _rebuilding ? "Reloading code…" : "Starting host…", TextFlag.Center );
		}
		else if ( !_session.IsConnected )
		{
			Paint.DrawText( LocalRect, $"Connecting {_session.PlayerName}…", TextFlag.Center );
		}
	}

	public override void OnDestroyed()
	{
		All.Remove( this );

		ReleaseInputFocus();

		_session?.Dispose();
		_session = null;

		base.OnDestroyed();
	}
}
