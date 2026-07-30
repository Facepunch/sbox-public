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
	/// Create an additional floating instance of a registered dock type. The first instance
	/// keeps the registered name, extras get "Name 2", "Name 3"... and delete on close.
	/// </summary>
	public DockWidget CreateDockInstance( string name )
	{
		if ( !docks.TryGetValue( name, out var info ) )
			return null;

		var instanceName = info.Title;
		for ( var i = 2; FindDockWidget( instanceName ) is not null; i++ )
		{
			instanceName = $"{info.Title} {i}";
		}

		var dock = CreateInstance( info, instanceName );
		if ( dock is null ) return null;

		_nativeDockManager.addDockWidgetFloating( dock._nativeDockWidget );

		return dock;
	}

	readonly List<string> instanceNames = new();

	internal DockWidget CreateInstance( DockInfo info, string instanceName )
	{
		var widget = info.CreateAction();
		if ( widget is null ) return null;

		var dock = CreateDockWidget( instanceName, info.Icon, widget );

		if ( instanceName != info.Title )
		{
			dock._nativeDockWidget.setFeature( DockWidgetFeature.DeleteOnClose, true );

			if ( !instanceNames.Contains( instanceName ) )
				instanceNames.Add( instanceName );
		}

		return dock;
	}

	/// <summary>
	/// Close every additional dock instance, leaving only the primary of each type.
	/// </summary>
	public void CloseDockInstances()
	{
		foreach ( var name in instanceNames )
		{
			FindDockWidget( name )?.CloseDockWidget();
		}

		instanceNames.Clear();
	}

	/// <summary>
	/// Unregister a dock type by name.
	/// </summary>
	public void UnregisterDockType( string name )
	{
		docks.Remove( name );
	}
}
