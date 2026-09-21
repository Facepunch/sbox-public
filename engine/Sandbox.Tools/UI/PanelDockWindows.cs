using NativeEngine;
using Sandbox.UI;
using System;

namespace Editor;

/// <summary>
/// Drags dock panels between desktop windows with panel-drawn chrome.
/// </summary>
public sealed partial class PanelDockWindows : IDisposable
{
	readonly DockHost _main;
	readonly Action<DockHost, string, Vector2> _previousDragPressed;
	readonly Func<string, bool> _previousFloatRequested;
	readonly Dictionary<FloatingWindow, DockHost> _windows = new();
	readonly Dictionary<DockHost, Dictionary<string, DockItem>> _reservations = new();
	readonly HashSet<PanelWindow> _closing = new();
	DragSession _session;
	int _busy;
	bool _dockAllRequested;
	bool _disposeRequested;
	bool _disposed;
	bool _drainQueued;

	/// <summary>Styles each floating root before content is transferred.</summary>
	public Action<RootPanel> ConfigureWindow { get; set; }

	/// <summary>Enables desktop tab dragging for this host and its floating windows.</summary>
	public PanelDockWindows( DockHost main )
	{
		ArgumentNullException.ThrowIfNull( main );
		_main = main;
		_previousDragPressed = main.DragPressed;
		_previousFloatRequested = main.FloatRequested;
		Attach( main );
	}

	static bool IsAlive( Panel panel ) => panel is { IsValid: true, IsDeleting: false }
		&& panel.AncestorsAndSelf.All( x => x.IsValid && !x.IsDeleting );

	bool CanUse( DockHost host ) => IsAlive( host ) && host.IsVisible
		&& PanelWindow.FromPanel( host ) is { IsOpen: true } window && !_closing.Contains( window );

	void Attach( DockHost host )
	{
		_reservations.Add( host, new( StringComparer.Ordinal ) );
		host.DragPressed = Arm;
		host.FloatRequested = Float;
		host.LayoutChanged += UpdateReservations;
		host.RegistrationChanged += UpdateReservations;
	}

	void Detach( DockHost host )
	{
		host.LayoutChanged -= UpdateReservations;
		host.RegistrationChanged -= UpdateReservations;
		if ( host.DragPressed == Arm ) host.DragPressed = host == _main ? _previousDragPressed : null;
		if ( host.FloatRequested == Float ) host.FloatRequested = host == _main ? _previousFloatRequested : null;
		if ( !_reservations.Remove( host, out var reserved ) ) return;
		foreach ( var (id, item) in reserved )
			if ( host.ReservedItems.GetValueOrDefault( id ) == item ) host.ReservedItems.Remove( id );
	}

	void Reserve( DockHost host, DockItem item )
	{
		if ( !_reservations.TryGetValue( host, out var reserved ) ) return;
		if ( host.ReservedItems.TryGetValue( item.Id, out var existing ) && existing != item )
			throw new InvalidOperationException( $"Dock panel ID '{item.Id}' is already reserved." );
		host.ReservedItems[item.Id] = item;
		reserved[item.Id] = item;
	}

	void UpdateReservations()
	{
		var hosts = _reservations.Keys.ToArray();
		var items = hosts.Where( IsAlive ).SelectMany( x => x.Items ).Where( x => x.IsAlive ).ToArray();
		foreach ( var host in hosts )
		{
			var reserved = _reservations[host];
			foreach ( var (id, item) in reserved )
				if ( host.ReservedItems.GetValueOrDefault( id ) == item ) host.ReservedItems.Remove( id );
			reserved.Clear();
			if ( !IsAlive( host ) ) continue;
			foreach ( var item in items )
				if ( host.Find( item.Id ) != item ) Reserve( host, item );
		}
	}

	void Transfer( DockHost source, DockHost target, DockItem item, DockDropTarget? placement = null, bool open = true )
	{
		// Reserve before callbacks can register another panel under the outgoing ID.
		Reserve( source, item );
		if ( target != _main ) Reserve( _main, item );
		try
		{
			source.TransferTo( target, item.Id, placement?.RelativeTo,
				position: placement?.Position ?? DockPosition.Center,
				fraction: placement?.Fraction ?? 0.5f, tabIndex: placement?.TabIndex ?? -1, open: open );
		}
		catch ( Exception exception ) when ( source.Find( item.Id ) != item )
		{
			// Notifications run after commit and can even transfer the item again.
			Log.Warning( exception, "Dock layout notification failed" );
		}
		finally
		{
			UpdateReservations();
		}
	}

	void Arm( DockHost host, string id, Vector2 point )
	{
		if ( _disposed || _disposeRequested || _dockAllRequested || _busy != 0 || !CanUse( host ) || !CanUse( _main ) ) return;
		if ( PanelWindows.DragSession is not null || !_reservations.ContainsKey( host ) || !host.IsOpen( id ) ) return;
		var item = host.Find( id );
		if ( item is null || !item.IsAlive ) return;

		UpdateReservations();
		_session = new DragSession( this, host, item, point );
		PanelWindows.DragSession = _session;
	}

	FloatingWindow CreateFloatingWindow( string title, Vector2 size, Vector2 position )
	{
		var owner = PanelWindow.FromPanel( _main );
		if ( owner is not { IsOpen: true } ) throw new InvalidOperationException( "Floating panels require an editor panel window." );
		var window = new FloatingWindow( title, size, position, owner, ReturnItems );
		try
		{
			window.RoundedCorners = true;
			window.ResizeBorder = 4;
			PanelWindowDesktop.SetOwner( window, owner );
			var host = new FloatingHost( this );
			host.AddClass( "floating" );
			_windows.Add( window, host );
			Attach( host );
			host.Style.Width = Length.Percent( 100 );
			host.Style.Height = Length.Percent( 100 );
			window.Root.AddChild( host );
			ConfigureWindow?.Invoke( window.Root );
			return window;
		}
		catch
		{
			window.Dispose();
			throw;
		}
	}

	void ReturnItems( FloatingWindow window )
	{
		if ( !_windows.TryGetValue( window, out var host ) ) return;
		if ( !_closing.Add( window ) ) throw new InvalidOperationException( "Dock window is already closing." );
		_busy++;
		try
		{
			var session = _session;
			session?.OnWindowClosing( window );
			// A direct Dispose cannot wait for Frame: its surface is about to be deleted.
			if ( session is not null )
			{
				if ( _busy == 1 && session.CanFinishCancellation ) session.Finish( rollback: true );
			}

			// Registrations include closed tabs parked outside the visible layout.
			foreach ( var item in host.Items.ToArray() )
			{
				if ( host.Find( item.Id ) != item || !item.IsAlive || !IsAlive( _main ) || _closing.Contains( PanelWindow.FromPanel( _main ) ) ) continue;
				var wasOpen = host.IsOpen( item.Id );
				Transfer( host, _main, item, open: wasOpen );
				if ( _main.Find( item.Id ) == item && !wasOpen )
				{
					try { _main.HideTransferredItem( item.Id ); }
					catch ( Exception exception ) when ( !_main.IsOpen( item.Id ) )
					{
						Log.Warning( exception, "Dock layout notification failed" );
					}
				}
			}

			// Refuse deletion if an observer put live content back into a closing host.
			if ( IsAlive( _main ) && host.Items.Any( x => x.IsAlive ) )
				throw new InvalidOperationException( "Dock window still owns content that could not be returned." );

			Detach( host );
			_windows.Remove( window );
			UpdateReservations();
		}
		finally
		{
			_closing.Remove( window );
			// The caller is still inside PanelWindow.Dispose. Do not recursively dispose it.
			_busy--;
			QueueRequests();
		}
	}

	void QueueRequests()
	{
		if ( _busy != 0 || _drainQueued || (!_dockAllRequested && !_disposeRequested) ) return;
		_drainQueued = true;
		EngineLoop.DisposeAtFrameEnd( new Sandbox.Utility.DisposeAction( () =>
		{
			_drainQueued = false;
			DrainRequests();
		} ) );
	}

	void CloseEmptyWindows()
	{
		foreach ( var (window, host) in _windows.ToArray() )
			if ( !host.Items.Any( x => host.IsOpen( x.Id ) ) && !_closing.Contains( window ) ) Try( window.Dispose );
	}

	static void Try( Action action )
	{
		try { action(); }
		catch ( Exception exception ) { Log.Warning( exception, "Panel window docking failed" ); }
	}

	void DrainRequests()
	{
		if ( _busy != 0 || (!_dockAllRequested && !_disposeRequested) ) return;
		_busy++;
		try
		{
			_dockAllRequested = false;
			_session?.Finish( rollback: true );
			foreach ( var window in _windows.Keys.ToArray() ) Try( window.Dispose );
			if ( _disposeRequested && _windows.Count == 0 )
			{
				Detach( _main );
				ConfigureWindow = null;
				_disposed = true;
				_disposeRequested = false;
			}
		}
		finally
		{
			_dockAllRequested = false;
			_busy--;
		}
	}

	/// <summary>
	/// Explicitly opens a registered panel in its own floating window. Returns false when unavailable
	/// or during a drag. The same content instance is retained; a lone floating panel is simply focused.
	/// </summary>
	public bool Float( string id )
	{
		if ( string.IsNullOrEmpty( id ) ) return false;
		if ( _disposed || _disposeRequested || _dockAllRequested || _busy != 0 || _session is not null || !CanUse( _main ) ) return false;
		var source = _reservations.Keys.FirstOrDefault( x => IsAlive( x ) && x.Find( id ) is not null );
		if ( source is null || !CanUse( source ) ) return false;
		var item = source.Find( id );
		if ( !item.IsAlive ) return false;
		var sourceWindow = PanelWindow.FromPanel( source );
		if ( source != _main && source.IsOpen( id ) && source.Items.Count( x => source.IsOpen( x.Id ) ) == 1 )
		{
			sourceWindow.Focus();
			return true;
		}

		FloatingWindow window = null;
		_busy++;
		try
		{
			var size = source.GetDockBounds( id ).Size * source.ScaleFromScreen;
			if ( size.x <= 0 || size.y <= 0 ) size = new Vector2( 560, 420 );
			size.y += 30;
			window = CreateFloatingWindow( item.Title, size, sourceWindow.Position + new Vector2( 40, 40 ) );
			if ( _disposeRequested || _dockAllRequested || !window.IsOpen || !CanUse( source ) || source.Find( id ) != item ) return false;
			Transfer( source, _windows[window], item );
			window.Focus();
			return true;
		}
		finally
		{
			CloseEmptyWindows();
			_busy--;
			DrainRequests();
		}
	}

	/// <summary>Returns all floating registrations, including closed panels, to the main host.</summary>
	public void DockAll()
	{
		if ( _disposed ) return;
		_dockAllRequested = true;
		_session?.Cancel();
		DrainRequests();
	}

	/// <summary>Unhooks dragging and returns floating content if the main host survives.</summary>
	public void Dispose()
	{
		if ( _disposed ) return;
		_disposeRequested = true;
		DockAll();
	}

	sealed class DragSession : IPanelWindowDragSession
	{
		readonly PanelDockWindows _owner;
		readonly DockHost _source;
		readonly DockItem _item;
		readonly PanelWindow _sourceWindow;
		readonly Vector2 _pressDesktop;
		readonly float _desktopToUi;
		readonly Dictionary<PanelWindow, bool> _frameRates = new();
		PanelWindow _captureWindow;
		PanelWindow _targetWindow;
		Vector2? _releasePoint;
		bool _released;
		bool _cancel;
		bool _captured;
		bool _finishing;
		bool _ended;
		bool _framing;

		bool _started;
		internal bool CanFinishCancellation => _cancel && !_framing && !_finishing;

		internal DragSession( PanelDockWindows owner, DockHost source, DockItem item, Vector2 press )
		{
			_owner = owner;
			_source = source;
			_item = item;
			_sourceWindow = PanelWindow.FromPanel( source );
			_pressDesktop = PanelWindowDesktop.ToDesktop( _sourceWindow, press );
			_desktopToUi = source.ScaleFromScreen / _sourceWindow.PixelsToWindow( Vector2.One ).x;
		}

		/// <inheritdoc/>
		public void Frame()
		{
			if ( _ended || _framing ) return;
			_framing = true;
			_owner._busy++;
			try
			{
				if ( _cancel || _owner._disposeRequested || !_owner.CanUse( _owner._main ) || !_owner.CanUse( _source )
					|| PanelWindow.FromPanel( _source ) != _sourceWindow || !_item.IsAlive )
				{
					Finish( rollback: true );
					return;
				}

				var buttons = PanelWindowDesktop.GetPointer( out var pointer );
				_released |= (buttons & 1) == 0;
				pointer = _releasePoint ?? pointer;
				if ( !_captured )
				{
					// Release is unconditional even if the backend reports capture unsupported.
					_captured = true;
					_captureWindow = PanelWindow.Focused ?? _sourceWindow;
					PanelWindowDesktop.Capture( true );
					_sourceWindow.Surface?.Input.CancelPointerInteraction();
					if ( _captureWindow != _sourceWindow ) _captureWindow.Surface?.Input.CancelPointerInteraction();
				}
				if ( _cancel )
				{
					Finish( rollback: true );
					return;
				}

				if ( !_started )
				{
					if ( _source.Find( _item.Id ) != _item || !_source.IsOpen( _item.Id ) )
					{
						Finish( rollback: true );
						return;
					}
					if ( (pointer - _pressDesktop).Length * _desktopToUi < 5 )
					{
						if ( _released ) Finish( rollback: false );
						return;
					}
					_started = true;
				}

				if ( _cancel || _source.Find( _item.Id ) != _item || !_source.IsOpen( _item.Id ) || !_owner.CanUse( _source ) )
				{
					Finish( rollback: true );
					return;
				}

				_targetWindow = PanelWindowDesktop.WindowAt( pointer, null );
				DockHost target = null;
				DockDropTarget? placement = null;
				_owner.UpdateReservations();
				foreach ( var host in _owner._reservations.Keys.ToArray() )
				{
					if ( !_owner.CanUse( host ) ) continue;
					var window = PanelWindow.FromPanel( host );
					if ( !_frameRates.ContainsKey( window ) ) _frameRates.Add( window, window.AlwaysFullFrameRate );
					window.AlwaysFullFrameRate = true;
					var hit = host.UpdateDockTargets( window == _targetWindow ? PanelWindowDesktop.ToSurface( window, pointer ) : null, _item.Id );
					if ( hit is null ) continue;
					target = host;
					placement = hit;
				}
				if ( _cancel ) Finish( rollback: true );
				else if ( _released ) Finish( rollback: false, target, placement );
			}
			catch ( Exception exception )
			{
				Log.Warning( exception, "Panel window drag failed" );
				Finish( rollback: true );
			}
			finally
			{
				_framing = false;
				_owner._busy--;
				_owner.DrainRequests();
			}
		}

		internal void Finish( bool rollback, DockHost target = null, DockDropTarget? placement = null, bool open = true )
		{
			if ( _ended || _finishing ) return;
			_finishing = true;
			try
			{
				if ( !rollback && target is not null && placement is not null && _started && _source.Find( _item.Id ) == _item
					&& _source.IsOpen( _item.Id ) && _owner.CanUse( _source ) )
				{
					if ( _owner.CanUse( target ) ) _owner.Transfer( _source, target, _item, placement );
				}
			}
			catch ( Exception exception )
			{
				// Transfer is atomic: a failed drop leaves the original tab where it was.
				Log.Warning( exception, "Could not finish panel window drag" );
			}
			finally
			{
				_ended = true;
				if ( PanelWindows.DragSession == this ) PanelWindows.DragSession = null;
				if ( _owner._session == this ) _owner._session = null;
				Try( () => PanelWindowDesktop.Capture( false ) );
				Try( () => _sourceWindow.Surface?.Input.CancelPointerInteraction() );
				Try( () => _captureWindow?.Surface?.Input.CancelPointerInteraction() );
				foreach ( var host in _owner._reservations.Keys.ToArray() )
				{
					Try( host.HideDockTargets );
					Try( () => PanelWindow.FromPanel( host )?.Surface?.Input.CancelPointerInteraction() );
				}
				foreach ( var (window, fullRate) in _frameRates ) window.AlwaysFullFrameRate = fullRate;
				Try( _owner.UpdateReservations );
				_owner.CloseEmptyWindows();
				if ( !rollback && _started && target is not null )
				{
					var focusWindow = PanelWindow.FromPanel( target );
					if ( focusWindow is { IsOpen: true } ) Try( focusWindow.Focus );
				}
			}
		}

		/// <inheritdoc/>
		public void Cancel() => _cancel = true;

		/// <inheritdoc/>
		public bool OnMouseButton( IntPtr window, ButtonCode button, bool down )
		{
			if ( _ended ) return false;
			if ( button != ButtonCode.MouseLeft ) return _started;
			if ( !down && !_released )
			{
				_released = true;
				try
				{
					PanelWindowDesktop.GetPointer( out var point );
					_releasePoint = point;
				}
				catch ( Exception exception )
				{
					Cancel();
					Log.Warning( exception, "Could not read panel window drag release" );
				}
			}
			return true;
		}

		/// <inheritdoc/>
		public bool OnKey( IntPtr window, ButtonCode button, bool down )
		{
			if ( _ended || button != ButtonCode.KEY_ESCAPE ) return false;
			if ( down ) Cancel();
			return true;
		}

		/// <inheritdoc/>
		public void OnFocus( IntPtr window, bool focused )
		{
			if ( !focused && (window == _sourceWindow.Handle || window == _captureWindow?.Handle) ) Cancel();
		}

		/// <inheritdoc/>
		public void OnWindowClosing( IPanelWindow window )
		{
			if ( window == _sourceWindow || window == _captureWindow || window == _targetWindow ) Cancel();
		}
	}

	sealed class FloatingHost : DockHost
	{
		readonly PanelDockWindows owner;

		internal FloatingHost( PanelDockWindows owner )
		{
			this.owner = owner;
			// Moving the window must not tear its contents into another drag window.
			// Tabs below this native hit-test region remain dedicated docking handles.
			var title = AddChild<Sandbox.UI.Label>();
			title.Text = "Floating panels";
			title.AddClass( "dock-window-title" );
			title.AddClass( "window-drag" );
			SetChildIndex( title, 0 );
		}

		/// <inheritdoc/>
		public override void Tick()
		{
			owner._busy++;
			try
			{
				base.Tick();
				// Closed registrations can be deleted without changing the layout.
				owner.UpdateReservations();
				if ( !Items.Any( x => IsOpen( x.Id ) ) && owner._session is null )
					EngineLoop.DisposeAtFrameEnd( new Sandbox.Utility.DisposeAction( owner.CloseEmptyWindows ) );
			}
			finally
			{
				owner._busy--;
				owner.QueueRequests();
			}
		}
	}

	sealed class FloatingWindow( string title, Vector2 size, Vector2 position, PanelWindow owner, Action<FloatingWindow> closing )
		: PanelWindow( title, size, position, borderless: true )
	{
		internal override IPanelWindow ParentWindow => owner;


		private protected override void OnClosing()
		{
			closing( this );
			base.OnClosing();
		}
	}
}
