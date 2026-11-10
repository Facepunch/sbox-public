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

	string DockCookie => $"Window.{StateCookie}.Dock";

	/// <summary>
	/// Open and arrange this window's default docks. Override this to define your default layout.
	/// </summary>
	protected virtual void BuildDefaultLayout()
	{
	}

	/// <summary>
	/// Restores window geometry and, on top of the base window, the saved dock layout.
	/// </summary>
	public override void RestoreFromStateCookie()
	{
		base.RestoreFromStateCookie();

		if ( string.IsNullOrWhiteSpace( StateCookie ) )
			return;

		RestoreLayout();
	}

	/// <summary>
	/// Restore the saved dock layout, or the default layout if there's nothing to restore.
	/// </summary>
	public void RestoreLayout()
	{
		var saved = string.IsNullOrWhiteSpace( StateCookie ) ? null : ProjectCookie.GetString( DockCookie, null );
		if ( !string.IsNullOrWhiteSpace( saved ) && DockManager.RestoreState( saved ) )
			return;

		ResetLayout();
	}

	/// <summary>
	/// Reset the window back to its default dock layout.
	/// </summary>
	public void ResetLayout()
	{
		foreach ( var dock in DockManager.DockTypes )
			DockManager.SetDockState( dock.Title, false );

		BuildDefaultLayout();
	}

	/// <summary>
	/// Persist the current dock layout. Happens automatically when the window closes or the app exits.
	/// </summary>
	public void SaveLayout()
	{
		if ( string.IsNullOrWhiteSpace( StateCookie ) || !DockManager.IsValid() )
			return;

		ProjectCookie.SetString( DockCookie, DockManager.State );
	}

	protected override void OnClosed()
	{
		SaveLayout();
		base.OnClosed();
	}

	[Event( "app.exit" )]
	void SaveLayoutOnExit() => SaveLayout();

	/// <summary>
	/// Populate a view menu with dock toggle options and a reset layout action.
	/// </summary>
	public void CreateDynamicViewMenu( Menu menu )
	{
		menu.Clear();

		IToolsDll.Current?.RunEvent( "tools.editorwindow.createview", menu );

		menu.AddOption( "Reset Layout", "restart_alt", ResetLayout );
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
