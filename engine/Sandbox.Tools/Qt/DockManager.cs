using Sandbox.Utility;
using System;

namespace Editor;

public partial class DockManager : Widget
{
	internal Native.CToolWindowManager _tw;

	/// <summary>
	/// Called when the layout state is changed programatically. This is generally called when the default
	/// layout is loaded, or a saved layout is loaded.
	/// </summary>
	public Action OnLayoutLoaded { get; set; }


	public DockManager( Widget parent = null ) : base( false )
	{
		Sandbox.InteropSystem.Alloc( this );

		_tw = Native.CToolWindowManager.Create( parent?._widget ?? default, this );
		NativeInit( _tw );
	}

	internal unsafe override void NativeInit( IntPtr ptr )
	{
		_tw = ptr;

		base.NativeInit( ptr );
	}
	internal override void NativeShutdown()
	{
		base.NativeShutdown();

		_tw = default;
	}

	/// <summary>
	/// Description of a dock that is available to create by the backend.
	/// </summary>
	public class DockInfo
	{
		/// <summary>
		/// This is what the dock will be shown as in the menu - but also what it will be referenced as internally.
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// Icon to show in the menu.
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// Called when the window wants to create this dock but it doesn't exist.
		/// </summary>
		public Func<Widget> CreateAction { get; set; }

		/// <summary>
		/// If true we'll delete the widget when it's closed. Otherwise it'll just be hidden.
		/// </summary>
		public bool DeleteOnClose { get; set; }
	}

	/// <summary>
	/// A list of dock types that are registered.
	/// </summary>
	public IEnumerable<DockInfo> DockTypes => docks.Values;

	Dictionary<string, DockInfo> docks = new();
	Dictionary<string, Widget> windows = new();

	/// <summary>
	/// Register a type of dock for the backend to be able to create.
	/// </summary>
	public void RegisterDockType( string name, string icon, Func<Widget> create, bool deleteOnClose = true )
	{
		var di = new DockInfo
		{
			Title = name,
			Icon = icon,
			CreateAction = create,
			DeleteOnClose = deleteOnClose
		};

		docks[name] = di;
	}

	/// <summary>
	/// Unregister a dock type.
	/// </summary>
	public void UnregisterDockType( string name )
	{
		docks.Remove( name );
	}

	/// <summary>
	/// Add a window next (or on top of) to the specified window.
	/// </summary>
	public void AddDock( Widget sibling, Widget window, DockArea dockArea = DockArea.Left, DockProperty properties = default, float split = 0.5f )
	{
		ArgumentNullException.ThrowIfNull( window );

		// force always display tabs
		properties |= DockProperty.AlwaysDisplayFullTabs;

		_tw.AddWindowToArea( sibling?._widget ?? default, window._widget, (Area)dockArea, properties, split );

		var name = window.Name;
		if ( !string.IsNullOrEmpty( name ) )
		{
			windows[name] = window;
		}
	}

	/// <summary>
	/// Whether the given dock-able window is visible or not.
	/// </summary>
	public bool IsDockOpen( string title )
	{
		if ( !windows.TryGetValue( title, out var widget ) )
			return false;

		return IsDockOpen( widget );
	}

	/// <summary>
	/// Whether the given dock-able window is visible or not.
	/// </summary>
	public bool IsDockOpen( Widget widget, bool includeCookied = true )
	{
		if ( !widget.IsValid() )
			return false;

		if ( _tw.IsOpen( widget._widget ) )
			return true;

		if ( !includeCookied )
			return false;

		// Sol: i do not love this but it prevents us doing things like redocking
		// because a tab wasn't considered "open" on the initial cookie restore
		return State.Contains( $"\"name\":\"{widget.Name}\"" );
	}

	/// <summary>
	/// Get an active, created dock
	/// </summary>
	public Widget GetDockWidget( string name )
	{
		if ( windows.TryGetValue( name, out var val ) && val.IsValid )
			return val;

		return null;
	}

	/// <summary>
	/// Raise this dock to the front of any tabs.
	/// </summary>
	public bool RaiseDock( string name )
	{
		if ( windows.TryGetValue( name, out var val ) && val.IsValid )
		{
			RaiseDock( val );
			return true;
		}

		return false;
	}

	/// <summary>
	/// Raise this dock to the front of any tabs.
	/// </summary>
	public void RaiseDock( Widget val )
	{
		_tw.raiseToolWindow( val._widget );
	}

	/// <summary>
	/// Set dock as visible, or hidden, by name.
	/// </summary>
	public void SetDockState( string name, bool visible )
	{
		// unknown dock type
		if ( !docks.TryGetValue( name, out var dock ) )
			return;

		// nothing to do
		if ( IsDockOpen( name ) == visible )
		{
			Log.Info( "Dock is open, nothing to do" );
			return;
		}

		var current = GetDockWidget( name );

		//
		// We want the window visible and it already exists (but is assumably not visible)
		//
		if ( visible && current != null )
		{
			AddDock( null, current, DockArea.Floating, default, 0.25f );
			return;
		}

		//
		// We want it hidden and we have a reference to it
		//
		if ( current != null )
		{
			_tw.hideToolWindow( current._widget );

			if ( dock.DeleteOnClose )
			{
				windows.Remove( name );
			}

			return;
		}

		//
		// We want it visible and it doesn't exist
		//
		if ( visible )
		{
			var created = dock.CreateAction();

			if ( created == null )
				return;

			AddDock( null, created, DockArea.Floating, default, 0.25f ); // I dunno!??
		}
	}

	private Widget Create( string managedType )
	{
		var result = EditorTypeLibrary.GetTypesWithAttribute<DockAttribute>().FirstOrDefault( x => x.Type.ClassName == managedType );
		var typeDescription = result.Type;
		var dockInfo = result.Attribute;

		if ( typeDescription is null )
			return null;

		if ( dockInfo is null )
			return null;

		//
		// Set up widget
		//
		var args = new object[] { this };
		var widget = typeDescription.Create<Widget>( args ) ?? throw new NullReferenceException( $"Couldn't create instance of '{managedType}'" );

		widget.DeleteOnClose = true;
		widget.WindowTitle = dockInfo.Name;
		widget.Name = dockInfo.Name;
		widget.SetWindowIcon( dockInfo.Icon );

		//
		// Do any instances of this window already exist?
		// Widget Name needs to be unique - so we'll append current count.
		//
		var widgetType = typeDescription.TargetType;
		var existingWindows = windows.Select( x => x.Value ).Where( x => x.GetType() == widgetType && x.IsValid() );

		if ( existingWindows.Any() )
		{
			var suffix = $" {existingWindows.Count() + 1}";
			widget.Name += suffix;
		}

		return widget;
	}

	/// <summary>
	/// Creates a widget by type
	/// </summary>
	public T Create<T>() where T : Widget
	{
		var type = typeof( T );
		var widget = Create( type.Name ) as T;
		AddDock( null, widget, DockArea.Floating );

		return widget;
	}

	internal void OnRightClickTab( Native.QWidget qwidget )
	{
		var widget = Widget.FindOrCreate( qwidget ) as Widget;
		if ( widget is null )
			return;

		var menu = new Menu( Parent );
		menu.AddOption( $"Close Tab", "close", () => _tw.closeToolWindow( widget._widget, false ) );
		// Weren't done right before, if we want them add them again
		// Close Other Tabs
		// menu.AddOption( $"Show {tabName} in Asset Browser", "manage_search", () => EditorEvent.Run( "assetsystem.highlight", session.Scene.Source.ResourcePath ); );
		menu.OpenAtCursor();
	}

	internal bool _creatingDock = false;

	/// <summary>
	/// Called from the native class when restoring a layout to ask to make a widget.
	/// </summary>
	internal IntPtr OnCreateDock( string name, string managedType )
	{
		// Set this bool so we can choose if our widget needs to try to dock or not... bleugh
		_creatingDock = true;
		using var _ = DisposeAction.Create( () => _creatingDock = false );

		// Check if we have a registered [Dock()] widget
		if ( docks.TryGetValue( name, out var dock ) )
		{
			// already created
			var w = GetDockWidget( name );
			if ( w != null ) return w._widget;

			w = dock.CreateAction();
			windows[name] = w;
			return w?._widget ?? default;
		}

		// Special case for SceneDock, we want to try and restore a SceneEditorSession here
		// I can probably make SceneDock an actual [Dock] and have it handled there
		if ( managedType == "SceneDock" )
		{
			// name is `SceneDock:scenes/sample.scene`
			string path = name.Split( ':', 2 ) is { Length: 2 } parts ? parts[1] : string.Empty;

			var session = SceneEditorSession.CreateFromPath( path );

			// If we can't open it (deleted?) just make a blank
			if ( session is null )
				session = SceneEditorSession.CreateDefault();

			return session.SceneDock._widget;
		}

		try
		{
			// No registered dock value, see if we can create by name
			var widget = Create( managedType );

			if ( widget.IsValid() )
			{
				windows[widget.Name] = widget;
				return widget._widget;
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't create docked widget: '{e}'" );
		}

		return default;
	}

	/// <summary>
	/// A JSON string representing the entire state of the dock manager, i.e. position of all the docks, etc.
	/// </summary>
	public string State
	{
		get => _tw.saveStateJson();
		set
		{
			_tw.loadStateJson( value );
			OnLayoutLoaded?.InvokeWithWarning();
		}
	}

	/// <summary>
	/// Clear the known widgets, reset manager to an empty state.
	/// </summary>
	public void Clear()
	{
		_tw.clear( true );
		windows.Clear();
		docks.Clear();
	}
}
