using System;

namespace Editor;

public partial class DockManager
{
	/// <summary>
	/// A list of dock types that are registered.
	/// </summary>
	public IEnumerable<DockInfo> DockTypes => docks.Values;

	Dictionary<string, DockInfo> docks = new();

	/// <summary>
	/// Description of a registered dock type.
	/// </summary>
	public class DockInfo
	{
		/// <summary>
		/// Display title and internal key for the dock.
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// Icon shown in menus and tabs.
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// Default dock area when first created.
		/// </summary>
		public DockArea Area { get; set; }

		/// <summary>
		/// Factory to create the content widget on demand.
		/// </summary>
		public Func<Widget> CreateAction { get; set; }
	}

	/// <summary>
	/// Register a dock type and immediately create and dock it.
	/// </summary>
	public void AddDock( DockInfo info )
	{
		docks[info.Title] = info;

		// already created (e.g. re-registered after a hotload)
		if ( FindDockWidget( info.Title ) is not null )
			return;

		var widget = info.CreateAction();
		if ( widget is null )
			return;

		AddDock( info.Title, info.Icon, widget, info.Area );
	}

	/// <summary>
	/// Register a dock type and create it closed in its default area, so it's available
	/// to view menus and layout restoring without appearing in the layout.
	/// </summary>
	public void RegisterDock( DockInfo info )
	{
		docks[info.Title] = info;

		// already created (e.g. re-registered after a hotload)
		if ( FindDockWidget( info.Title ) is not null )
			return;

		var widget = info.CreateAction();
		if ( widget is null )
			return;

		var dock = CreateDockWidget( info.Title, info.Icon, widget );
		AddDock( dock, info.Area == DockArea.Hidden ? DockArea.Center : info.Area );
		dock.ToggleView( false );
	}

	/// <summary>
	/// Unregister a dock type by name.
	/// </summary>
	public void UnregisterDockType( string name )
	{
		docks.Remove( name );
	}
}
