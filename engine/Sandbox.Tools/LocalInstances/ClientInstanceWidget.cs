using System;

namespace Editor;

/// <summary>
/// A dockable "Client N" tab hosting an in-process client session - a genuine multiplayer
/// client (own scene, own connection, real handshake) living inside the editor process,
/// see <see cref="Sandbox.InProcessClientSession"/>. It shares loaded assemblies and
/// resources with the host, so it joins near-instantly. If the editor wasn't hosting when
/// the tab was added, it waits for the host to come up and then connects itself.
/// </summary>
public class ClientInstanceWidget : Widget
{
	internal static readonly List<ClientInstanceWidget> All = new();

	Sandbox.InProcessClientSession _session;
	readonly SceneRenderingWidget _renderer;

	RealTimeSince _waitingForHost;

	internal ClientInstanceWidget() : base( null )
	{
		Layout = Layout.Row();

		// A slim frame around the render view - painted green while this client has input focus.
		Layout.Margin = 2;

		_renderer = new SceneRenderingWidget( this );
		_renderer.Visible = false;

		Layout.Add( _renderer, 1 );

		All.Add( this );

		// Claim on the focus event - polling IsFocused once a frame misses transitions.
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

		// Kicked, disconnected, or the host went away.
		if ( _session.IsDefunct )
		{
			_session = null;
			Destroy();
			return;
		}

		// The host's game code changed (hotload) - dispose and reconnect in-process.
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

		// The host stopped (editor Stop button).
		if ( !Game.IsPlaying || Sandbox.Networking.System is null || !Sandbox.Networking.System.IsHost )
		{
			Destroy();
			return;
		}

		UpdateInputFocus();

		_session.ViewSize = _renderer.Size * _renderer.DpiScale;
		_session.Tick();

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

		// A connected client without an active camera renders a black tab - say so once, it just looks broken from outside.
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

	nint _registeredWinId;

	/// <summary>
	/// Click the client's view to drive that client with your keyboard/mouse; click the host's
	/// game view (or another client) to give input back. The claim is latched - transient Qt
	/// focus changes must not silently return input to the host.
	/// </summary>
	void ClaimInputFocus()
	{
		if ( _session is null || !_renderer.IsValid() )
			return;

		// The previous owner's cleanup must happen BEFORE our claim, or its next-frame release clobbers our registration.
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

		// Engine window state ties SDL input routing to a window - without it clicks land against the play widget's window.
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

		// Hand engine window state back to the host's game view - unless another docked client is claiming right behind us.
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

		// Escape always hands input back - clicking into the game view is a fight when the client's game captures the mouse.
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

		// Undocking reparents to a new native window - keep the SDL registration pointed at the actual window.
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

				// The tab text lives on the containing dock widget.
				if ( EditorWindow.DockManager.FindDockWidget( this ) is { } dock )
					dock.WindowTitle = WindowTitle;

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

		// Connected somewhere as a client, or hosting never came up.
		if ( (host is not null && !host.IsHost) || _waitingForHost > 30f )
		{
			Log.Warning( "Docked client tab closed - the editor never became a host" );
			Destroy();
		}
	}

	protected override void OnPaint()
	{
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
