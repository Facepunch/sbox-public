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
		_renderer = new SceneRenderingWidget( this );
		_renderer.Visible = false;
		Layout.Add( _renderer );

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

		// If the host stopped (editor Stop button), shut the client down with it.
		if ( !Game.IsPlaying || Sandbox.Networking.System is null || !Sandbox.Networking.System.IsHost )
		{
			Destroy();
			return;
		}

		UpdateInputFocus();

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
	}

	bool _hasInputFocus;

	/// <summary>
	/// Click the client's view to drive that client with your keyboard/mouse; click the host's
	/// game view (or another client) to give input back. The claim is LATCHED: transient Qt
	/// focus changes (mouse capture, engine focus juggling) must not silently return input to
	/// the host - only an explicit click into the game view or another client releases it.
	/// Claiming registers the renderer with SDL so the engine keeps capturing game input for
	/// this window - the same hookup GameMode does for the play widget, minus the engine-state
	/// rerouting so the host's HUD stays where it belongs.
	/// </summary>
	void UpdateInputFocus()
	{
		var claimed = Sandbox.InProcessClientSession.Focused == _session;

		// Claim on click into our view.
		if ( _renderer.IsFocused && !claimed )
		{
			Sandbox.InProcessClientSession.Focused = _session;
			NativeEngine.InputSystem.RegisterWindowWithSDL( _renderer._widget.winId() );
			NativeEngine.InputSystem.OnEditorGameFocusChange( _renderer._widget.winId(), true );
			_hasInputFocus = true;
			return;
		}

		if ( !_hasInputFocus )
			return;

		// Another client tab claimed the input away from us.
		if ( !claimed )
		{
			ReleaseInputFocus();
			return;
		}

		// The player clicked back into the host's game view, or hid this tab.
		if ( GameMode.PlayWidgetFocused || !Visible )
		{
			ReleaseInputFocus();
		}
	}

	void ReleaseInputFocus()
	{
		if ( !_hasInputFocus )
			return;

		_hasInputFocus = false;

		if ( _session is not null && Sandbox.InProcessClientSession.Focused == _session )
			Sandbox.InProcessClientSession.Focused = null;

		if ( _renderer.IsValid() && _renderer._widget.IsValid )
		{
			NativeEngine.InputSystem.OnEditorGameFocusChange( _renderer._widget.winId(), false );
			NativeEngine.InputSystem.UnregisterWindowFromSDL( _renderer._widget.winId() );
		}
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
		// Only visible until the client's scene covers the tab.
		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );

		Paint.SetPen( Theme.TextLight );

		if ( _session is null )
		{
			Paint.DrawText( LocalRect, "Starting host…", TextFlag.Center );
		}
		else if ( !_session.IsConnected )
		{
			Paint.DrawText( LocalRect, $"Connecting {_session.PlayerName}…", TextFlag.Center );
		}
	}

	public override void OnDestroyed()
	{
		ReleaseInputFocus();

		_session?.Dispose();
		_session = null;

		base.OnDestroyed();
	}
}
