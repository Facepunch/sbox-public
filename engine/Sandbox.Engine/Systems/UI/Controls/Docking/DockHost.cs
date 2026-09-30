using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A registered dock panel. Its content survives tab changes, closing and moving between hosts.
/// </summary>
public sealed class DockItem
{
	/// <summary>
	/// Stable ID used in saved layouts.
	/// </summary>
	public string Id { get; }

	/// <summary>
	/// Title shown on the tab.
	/// </summary>
	public string Title { get; }

	/// <summary>
	/// Optional Material icon shown before the title.
	/// </summary>
	public string Icon { get; }

	/// <summary>
	/// The panel owned by the host, or null until a factory registration is opened.
	/// </summary>
	public Panel Content { get; private set; }
	Func<Panel> _create;
	internal bool IsAlive => Content is null || (Content.IsValid && !Content.IsDeleting);

	/// <summary>
	/// Whether the panel can be closed.
	/// </summary>
	public bool CanClose { get; }

	internal Panel Container { get; }

	internal void EnsureContent()
	{
		if ( Content is not null ) return;
		var content = _create() ?? throw new InvalidOperationException( "The dock factory returned null." );
		if ( !content.IsValid || content.IsDeleting || content is RootPanel
			|| Container.AncestorsAndSelf.Contains( content ) || content.Ancestors.OfType<DockHost>().Any() )
			throw new InvalidOperationException( "The dock factory returned content already owned by a docking host or an invalid panel." );
		Content = content;
		content.Parent = Container;
		_create = null;
	}

	internal DockItem( string id, string title, Func<Panel> create, bool canClose, string icon )
		: this( id, title, (Panel)null, canClose, icon )
	{
		_create = create;
	}

	internal DockItem( string id, string title, Panel content, bool canClose, string icon )
	{
		Id = id;
		Title = title;
		Icon = icon;
		Content = content;
		CanClose = canClose;
		Container = new Panel();
		Container.AddClass( "dock-content" );
		if ( content is not null ) content.Parent = Container;
	}
}

/// <summary>
/// Tabbed panel docking with nested splits. Registered content is owned until this host is deleted or transfers it.
/// </summary>
[Library( "dockhost" )]
[StyleSheet.Inline( "dockhost", Styles )]
public partial class DockHost : Panel
{
	readonly DockLayout _layout = new();
	readonly Dictionary<string, DockItem> _items = new( StringComparer.Ordinal );
	readonly Dictionary<DockNode, Panel> _views = new();
	readonly Dictionary<string, DockTab> _tabs = new( StringComparer.Ordinal );
	readonly Panel _parking;
	readonly Panel _workspace;
	readonly Panel _preview;
	readonly Label _empty;
	int _deferNotifications;
	internal Dictionary<string, DockItem> ReservedItems { get; } = new( StringComparer.Ordinal );
	internal event Action RegistrationChanged;

	/// <summary>
	/// All registered panels, including closed panels.
	/// </summary>
	public IReadOnlyCollection<DockItem> Items => _items.Values;

	/// <summary>
	/// Called after a layout edit. Content instances are not recreated.
	/// </summary>
	public event Action LayoutChanged;

	internal Action<DockHost, string, Vector2> DragPressed { get; set; }
	internal Func<string, bool> FloatRequested { get; set; }
	internal bool UsesWindowDragging => DragPressed is not null
		&& PanelWindows.All.Any( x => x.IsOpen && x.Surface?.Root == FindRootPanel() );

	internal IDisposable DeferLayoutNotifications()
	{
		_deferNotifications++;
		return new Sandbox.Utility.DisposeAction( () =>
		{
			_deferNotifications--;
			NotifyLayoutChanged();
		} );
	}

	void NotifyLayoutChanged()
	{
		if ( _deferNotifications == 0 && IsValid && !IsDeleting ) LayoutChanged?.Invoke();
	}

	// Workspace restore already checks nonclosable panels across all destinations.
	internal bool RestoreWorkspaceLayout( string json )
	{
		CheckAlive();
		CancelDrag();
		return _layout.Restore( json, _items.Keys );
	}

	/// <summary>
	/// Creates an empty docking workspace.
	/// </summary>
	public DockHost()
	{
		AddClass( "dockhost" );
		CanDragScroll = false;
		_parking = Add.Panel( "dock-parking" );
		_workspace = Add.Panel( "dock-workspace" );
		_empty = _workspace.Add.Label( "No panels open", "dock-empty" );
		_preview = Add.Panel( "dock-preview" );
		_preview.Style.Display = DisplayMode.None;
		_layout.Changed += OnLayoutChanged;
	}

	void CheckAlive()
	{
		if ( !IsValid || AncestorsAndSelf.Any( x => x.IsDeleting ) )
			throw new InvalidOperationException( "The docking host is being deleted." );
	}

	/// <summary>
	/// Registers content, initially closed. A panel instance can belong to only one registration.
	/// </summary>
	public DockItem Register( string id, string title, Panel content, bool canClose = true, string icon = null )
	{
		CheckAlive();
		ArgumentNullException.ThrowIfNull( content );
		// Validate IDs using the same rules as layout persistence.
		new DockLayout().Dock( id );
		if ( _items.ContainsKey( id ) || ReservedItems.ContainsKey( id ) )
			throw new ArgumentException( "This panel ID is already registered or floating.", nameof( id ) );
		if ( !content.IsValid || content.IsDeleting || content is RootPanel
			|| AncestorsAndSelf.Contains( content ) || content.Ancestors.OfType<DockHost>().Any() )
			throw new ArgumentException( "The content cannot be owned by this docking host.", nameof( content ) );

		var item = new DockItem( id, title ?? id, content, canClose, icon );
		item.Container.Parent = _parking;
		_items.Add( id, item );
		RegistrationChanged?.Invoke();
		return item;
	}

	/// <summary>
	/// Registers a dock factory without creating its panel. Content is created on first open and retained until the host is deleted.
	/// </summary>
	public DockItem Register( string id, string title, Func<Panel> create, bool canClose = true, string icon = null )
	{
		CheckAlive();
		ArgumentNullException.ThrowIfNull( create );
		new DockLayout().Dock( id );
		if ( _items.ContainsKey( id ) || ReservedItems.ContainsKey( id ) )
			throw new ArgumentException( "This panel ID is already registered or floating.", nameof( id ) );
		var item = new DockItem( id, title ?? id, create, canClose, icon );
		item.Container.Parent = _parking;
		_items.Add( id, item );
		RegistrationChanged?.Invoke();
		return item;
	}

	/// <summary>
	/// Finds a registered panel, including closed panels.
	/// </summary>
	public DockItem Find( string id ) => id is not null && _items.TryGetValue( id, out var item ) ? item : null;

	/// <summary>
	/// Whether a registered panel is in the layout.
	/// </summary>
	public bool IsOpen( string id ) => _layout.FindGroup( id ) is not null;

	/// <summary>
	/// Opens or moves a registered panel. Edge fractions describe the incoming panel's share.
	/// </summary>
	public void Dock( string id, string relativeTo = null, DockPosition position = DockPosition.Center, float fraction = 0.5f, int tabIndex = -1 )
	{
		CheckAlive();
		if ( Find( id ) is not { IsAlive: true } )
			throw new ArgumentException( "The panel is not registered or has been deleted.", nameof( id ) );
		var candidate = new DockLayout();
		candidate.Restore( _layout.Save(), _items.Keys );
		candidate.Dock( id, relativeTo, position, fraction, tabIndex );
		Find( id ).EnsureContent();
		CancelDrag();
		_layout.Dock( id, relativeTo, position, fraction, tabIndex );
	}

	/// <summary>
	/// Closes a panel without deleting its content. Nonclosable panels are left open.
	/// </summary>
	public bool Close( string id )
	{
		CheckAlive();
		if ( Find( id )?.CanClose != true ) return false;
		CancelDrag();
		return _layout.Close( id );
	}

	/// <summary>
	/// Preserves a transferred registration's closed state, including never-opened nonclosable panels.
	/// </summary>
	internal void HideTransferredItem( string id ) => _layout.Close( id );

	/// <summary>
	/// Selects an open panel without moving it.
	/// </summary>
	public bool Activate( string id )
	{
		CheckAlive();
		return _layout.Activate( id );
	}

	/// <summary>
	/// Saves tab order, selection and split proportions. Closed panels are omitted.
	/// </summary>
	public string State
	{
		get => _layout.Save();
		set => RestoreState( value );
	}

	/// <summary>
	/// Restores a valid layout atomically. Open nonclosable panels must remain present.
	/// </summary>
	public bool RestoreState( string json )
	{
		CheckAlive();
		var candidate = new DockLayout();
		if ( !candidate.Restore( json, _items.Keys ) ) return false;
		if ( _items.Values.Any( x => !x.CanClose && IsOpen( x.Id ) && candidate.FindGroup( x.Id ) is null ) ) return false;
		foreach ( var item in _items.Values.Where( x => candidate.FindGroup( x.Id ) is not null ) ) item.EnsureContent();
		CancelDrag();
		return _layout.Restore( json, _items.Keys );
	}

	/// <summary>
	/// Transfers ownership and docks the same content in another host. Invalid placement leaves both hosts unchanged.
	/// </summary>
	internal void TransferTo( DockHost target, string id, string relativeTo = null, DockPosition position = DockPosition.Center, float fraction = 0.5f, int tabIndex = -1, bool open = true )
	{
		CheckAlive();
		ArgumentNullException.ThrowIfNull( target );
		target.CheckAlive();
		if ( target == this )
		{
			Dock( id, relativeTo, position, fraction, tabIndex );
			return;
		}

		var item = Find( id ) ?? throw new ArgumentException( "The panel is not registered.", nameof( id ) );
		if ( !item.IsAlive || target.Find( id ) is not null
			|| (target.ReservedItems.TryGetValue( id, out var reserved ) && reserved != item)
			|| target.AncestorsAndSelf.Contains( item.Container ) )
			throw new ArgumentException( "The target cannot receive this panel.", nameof( target ) );

		var candidate = new DockLayout();
		candidate.Restore( target.State, target._items.Keys );
		if ( open )
		{
			candidate.Dock( id, relativeTo, position, fraction, tabIndex );
			item.EnsureContent();
		}

		CancelDrag();
		target.CancelDrag();
		UISystem?.ReleaseFocusSubtree( item.Container );
		// Reparent before any old group can be deleted, including closed content in the parking panel.
		_deferNotifications++;
		target._deferNotifications++;
		try
		{
			item.Container.Parent = target._parking;
			_items.Remove( id );
			target._items.Add( id, item );
			if ( _tabs.Remove( id, out var tab ) ) tab.Delete( true );
			if ( !_layout.Close( id ) ) OnLayoutChanged();
			if ( open ) target._layout.Dock( id, relativeTo, position, fraction, tabIndex );
			else target.OnLayoutChanged();
		}
		finally
		{
			_deferNotifications--;
			target._deferNotifications--;
		}
		// Observers can only see the fully committed transfer, even if one throws or edits a host.
		try { NotifyLayoutChanged(); }
		finally { target.NotifyLayoutChanged(); }
	}

	void OnLayoutChanged()
	{
		if ( !IsValid || IsDeleting ) return;
		var used = new HashSet<DockNode>();
		if ( _layout.Root is not null ) BuildView( _layout.Root, _workspace, used );
		_empty.Style.Display = _layout.Root is null ? DisplayMode.Flex : DisplayMode.None;
		foreach ( var item in _items.Values )
		{
			if ( IsOpen( item.Id ) ) continue;
			item.Container.Parent = _parking;
			if ( _tabs.TryGetValue( item.Id, out var tab ) ) tab.Parent = _parking;
		}

		// Surviving groups and content have already left obsolete branches.
		foreach ( var node in _views.Keys.Where( x => !used.Contains( x ) ).ToArray() )
		{
			_views[node].Delete( true );
			_views.Remove( node );
		}
		NotifyLayoutChanged();
	}

	Panel BuildView( DockNode node, Panel parent, HashSet<DockNode> used )
	{
		used.Add( node );
		if ( !_views.TryGetValue( node, out var view ) )
		{
			view = node is DockSplit split ? new SplitView( this, split ) : new GroupView( this );
			_views.Add( node, view );
		}
		view.Parent = parent;
		if ( node is DockSplit branch )
		{
			var split = (SplitView)view;
			BuildView( branch.First, split.First, used );
			BuildView( branch.Second, split.Second, used );
			split.UpdateFraction();
		}
		else if ( node is DockGroup group )
		{
			var region = (GroupView)view;
			region.SetClass( "single-tab", group.Tabs.Count == 1 );
			for ( int i = 0; i < group.Tabs.Count; i++ )
			{
				var id = group.Tabs[i];
				var item = _items[id];
				item.EnsureContent();
				if ( !_tabs.TryGetValue( id, out var tab ) )
					_tabs.Add( id, tab = new DockTab( this, item ) );
				tab.Parent = region.Tabs;
				region.Tabs.SetChildIndex( tab, i );
				tab.SetClass( "selected", group.ActiveId == id );
				item.Container.Parent = region.Body;
				item.Container.Style.Display = group.ActiveId == id ? DisplayMode.Flex : DisplayMode.None;
			}
			region.Tabs.SetSelection( _tabs[group.ActiveId] );
		}
		return view;
	}

	/// <inheritdoc/>
	public override void Tick()
	{
		base.Tick();
		foreach ( var item in _items.Values.Where( x => !x.IsAlive ).ToArray() )
		{
			CancelDrag();
			_items.Remove( item.Id );
			if ( _tabs.Remove( item.Id, out var tab ) ) tab.Delete( true );
			item.Container.Delete( true );
			_layout.Close( item.Id );
			RegistrationChanged?.Invoke();
		}
		if ( _dragId is not null && UISystem?.Input is SurfaceInput input && !input.MouseInside ) CancelDrag();
	}

	/// <inheritdoc/>
	public override void OnDeleted()
	{
		_layout.Changed -= OnLayoutChanged;
		LayoutChanged = null;
		RegistrationChanged = null;
		DragPressed = null;
		FloatRequested = null;
		_items.Clear();
		ReservedItems.Clear();
		_tabs.Clear();
		_views.Clear();
		base.OnDeleted();
	}
}
