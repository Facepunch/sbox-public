using Sandbox.Engine;
using System;

namespace Editor;

internal sealed class ClientInstanceWidget : Widget
{
	readonly ClientSceneWidget _view;
	DockWidget _dock;
	bool _closing;

	public LocalClientWorld World { get; private set; }

	public bool IsFocusedWorld => InputRouter.FocusedWorld == World.Context;

	public bool ShouldClose => World.IsDefunct || (_dock is not null && _dock.IsClosed);

	public ClientInstanceWidget( LocalClientWorld world ) : base( null )
	{
		World = world;

		Layout = Layout.Column();
		Layout.Margin = 2;
		MinimumSize = new Vector2( 320, 180 );

		_view = new ClientSceneWidget( this );
		Layout.Add( _view );
	}

	public void Open( DockArea area, DockWidget relativeTo )
	{
		_dock = EditorWindow.DockManager.CreateDockWidget( World.PlayerName, "person", this );
		_dock._nativeDockWidget.setFeature( DockManager.DockWidgetFeature.Floatable, false );

		EditorWindow.DockManager.AddDock( _dock, relativeTo is null ? DockArea.Center : area, relativeTo );
	}

	public DockWidget Dock => _dock is not null && !_dock.IsClosed ? _dock : null;

	public void FocusView() => _view.Focus();

	public bool IsStale => World.CodeVersion != (IGameInstanceDll.Current?.CodeVersion ?? 0);

	public void Rebuild()
	{
		var focused = IsFocusedWorld;
		var name = World.PlayerName;

		_view.Scene = null;
		World.Dispose();
		World = LocalClientWorld.Create( name );

		if ( focused )
			LocalClients.Focus( this );
	}

	public void Frame()
	{
		_view.Scene = World.Scene;

		if ( _view.SwapChain != default )
		{
			World.Context.Surface = new GameSurface( WindowInput.GetEditorMainWindow(), _view.SwapChain );
		}
	}

	public void Shutdown()
	{
		if ( _closing )
			return;

		_closing = true;

		if ( IsFocusedWorld )
			LocalClients.Focus( null );

		_view.ReleaseInput();
		World.Dispose();

		if ( _dock is not null && !_dock.IsClosed )
			_dock.CloseDockWidget();

		_dock?.Destroy();
		_dock = null;
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( IsFocusedWorld ? Theme.Green : Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );
	}

	sealed class ClientSceneWidget : SceneRenderingWidget
	{
		readonly ClientInstanceWidget _owner;
		IntPtr _window;

		public ClientSceneWidget( ClientInstanceWidget owner ) : base( owner )
		{
			_owner = owner;
			MouseTracking = true;
			RenderScope = () => _owner.World.Push();
		}

		void EnsureRegistered()
		{
			if ( _window != default )
				return;

			_window = _widget.winId();
			GameMode.RegisterInputWindow( _window );
		}

		internal void ReleaseInput()
		{
			if ( _window == default )
				return;

			GameMode.UnregisterInputWindow( _window );
			_window = default;
		}

		protected override void OnFocus( FocusChangeReason reason )
		{
			base.OnFocus( reason );

			EnsureRegistered();
			LocalClients.Focus( _owner );
			GameMode.SetInputWindowFocus( _window, true );
		}

		protected override void OnBlur( FocusChangeReason reason )
		{
			base.OnBlur( reason );

			if ( _window != default )
				GameMode.SetInputWindowFocus( _window, false );

			LocalClients.Blur( _owner );
		}
	}
}
