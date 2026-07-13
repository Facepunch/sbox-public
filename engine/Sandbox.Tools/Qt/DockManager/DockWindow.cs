using Sandbox.Engine;

namespace Editor;

/// <summary>
/// A window that is built from docking windows.
/// </summary>
public partial class DockWindow : Window
{
	/// <summary>
	/// The dock manager for this window, created automatically.
	/// </summary>
	public DockManager DockManager { get; init; }

	public DockWindow()
	{
		DockManager = new DockManager( this );
		Canvas = DockManager;
	}

	internal DockWindow( Native.CFramelessMainWindow ptr ) : base( ptr )
	{
		DockManager = new DockManager( this );
		Canvas = DockManager;
	}

	string _defaultDockState;

	/// <summary>
	/// Override to create this window's default dock layout.
	/// </summary>
	protected virtual void CreateDefaultDockLayout()
	{
	}

	/// <summary>
	/// Override to apply a default layout to your window. The layout that exists when the
	/// state cookie is first restored is captured automatically, so the default implementation
	/// restores that snapshot.
	/// </summary>
	protected virtual void RestoreDefaultDockLayout()
	{
		if ( string.IsNullOrEmpty( _defaultDockState ) )
			return;

		DockManager.State = _defaultDockState;
	}

	public override void RestoreFromStateCookie()
	{
		if ( string.IsNullOrWhiteSpace( StateCookie ) )
			return;

		base.RestoreFromStateCookie();

		if ( _defaultDockState is null )
		{
			CreateDefaultDockLayout();
			_defaultDockState = DockManager.State;
		}

		var state = ProjectCookie.GetString( $"Window.{StateCookie}.Dock", null );
		if ( string.IsNullOrWhiteSpace( state ) || !DockManager.RestoreState( state ) )
		{
			RestoreDefaultDockLayout();
		}
	}

	public override void SaveToStateCookie()
	{
		if ( string.IsNullOrWhiteSpace( StateCookie ) )
			return;

		base.SaveToStateCookie();

		ProjectCookie.SetString( $"Window.{StateCookie}.Dock", DockManager.State );
	}

	/// <summary>
	/// Populate a view menu with dock toggle options and a reset layout action.
	/// </summary>
	public void CreateDynamicViewMenu( Menu menu )
	{
		menu.Clear();

		IToolsDll.Current?.RunEvent( "tools.editorwindow.createview", menu );

		menu.AddOption( "Reset Layout", "restart_alt", RestoreDefaultDockLayout );
		menu.AddSeparator();

		foreach ( var dock in DockManager.DockTypes.OrderBy( x => x.Title ) )
		{
			var o = menu.AddOption( dock.Title, dock.Icon );
			o.Checkable = true;
			o.Checked = DockManager.IsDockOpen( dock.Title );
			o.Toggled += ( b ) => DockManager.SetDockState( dock.Title, b );
		}

		IToolsDll.Current?.RunEvent( "tools.editorwindow.postcreateview", menu );
	}
}
