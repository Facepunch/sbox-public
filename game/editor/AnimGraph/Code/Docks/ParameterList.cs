using System.Text.Json.Serialization;

namespace Editor.AnimGraph;

/// <summary>
/// Dock panel listing the graph's control parameters. Values apply live to the
/// preview, rows drag onto the graph to create parameter nodes.
/// </summary>
public class ParameterList : Widget
{
	private readonly Window _window;
	private readonly LineEdit _filter;
	private readonly Widget _rowsCanvas;
	private readonly Layout _rows;
	private readonly List<IParameterRow> _rowWidgets = new();
	private readonly ToolButton _deleteUnusedButton;
	private readonly HashSet<string> _collapsedGroups;
	private bool? _sortAscending;
	private bool _hasVisibleParameters;

	private const string CollapsedGroupsCookie = "animgraph.parameter-groups.collapsed";

	public ParameterList( Window window ) : base( null )
	{
		_window = window;
		_collapsedGroups = EditorCookie.Get<List<string>>( CollapsedGroupsCookie, [] ).ToHashSet( StringComparer.OrdinalIgnoreCase );

		Name = "Parameters";
		WindowTitle = "Parameters";
		SetWindowIcon( "tune" );

		Layout = Layout.Column();

		var bar = new Widget( this );
		bar.FixedHeight = Theme.RowHeight + 2;
		bar.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( bar.LocalRect );
			return true;
		};

		var toolbar = bar.Layout = Layout.Row();
		toolbar.Margin = new Sandbox.UI.Margin( 0, 0, 0, 2 );
		toolbar.Spacing = 2;
		Layout.Add( bar );

		_filter = toolbar.Add( new LineEdit() { PlaceholderText = "⌕  Filter..", ClearButtonEnabled = true, FixedHeight = Theme.RowHeight }, 1 );
		_filter.TextEdited += _ => Rebuild();

		toolbar.Add( HeaderButton( "swap_vert", "Sort alphabetically", () =>
		{
			_sortAscending = !(_sortAscending ?? false);
			Rebuild();
		} ) );

		AddDivider( toolbar );

		toolbar.Add( HeaderButton( "restart_alt", "Reset all previews to start values", ResetAllPreviews ) );
		toolbar.Add( HeaderButton( "casino", "Randomize all previews", () => ApplyAllPreviews( p => p.RandomizePreview() ) ) );
		_deleteUnusedButton = toolbar.Add( HeaderButton( "delete_sweep", "Delete all unused parameters", DeleteUnused ) );

		AddDivider( toolbar );

		ToolButton addButton = null;
		addButton = toolbar.Add( HeaderButton( "add",
			"<b>Add Control Parameter</b><p>Adds a runtime input supplied by game code.</p><p>Use for gameplay state, movement values, targets, and other external data.</p>",
			() => OpenAddMenu( addButton ) ) );

		IconButton virtualButton = null;
		virtualButton = toolbar.Add( new IconButton( "add_circle" )
		{
			ToolTip = "<b>Add Virtual Parameter</b><p>Adds a named value calculated inside this graph.</p><p>Use to reuse derived values without exposing them to game code.</p>",
			Background = global::Color.Parse( "#d9822b" ).Value.WithAlpha( 0.35f ),
			Foreground = global::Color.Parse( "#f0a45d" ).Value,
			IconSize = 14,
			FixedSize = Theme.RowHeight,
			OnClick = () => OpenVirtualAddMenu( virtualButton )
		} );

		var panel = Layout.Add( new Widget( this ) { Layout = Layout.Column() }, 1 );
		panel.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground.Darken( 0.1f ) );
			Paint.DrawRect( panel.LocalRect, Theme.ControlRadius );

			if ( !_hasVisibleParameters )
			{
				Paint.SetPen( Theme.TextControl.WithAlpha( 0.4f ) );
				Paint.SetDefaultFont();
				Paint.DrawText( panel.LocalRect, EmptyHint, TextFlag.Center );
			}

			return true;
		};

		var scroll = panel.Layout.Add( new ScrollArea( panel ), 1 );
		scroll.VerticalScrollbarMode = ScrollbarMode.Auto;
		scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		scroll.TranslucentBackground = true;
		scroll.NoSystemBackground = true;
		// Scope to the scroll area itself, otherwise the rules cascade into every
		// descendant - including the QToolTip popups shown for the rows inside it.
		scroll.Name = "AnimGraphParameterScroll";
		scroll.SetStyles( "#AnimGraphParameterScroll { border: none; background: transparent; }" );
		scroll.Canvas = new Widget( scroll )
		{
			Layout = Layout.Column(),
			HorizontalSizeMode = SizeMode.Flexible,
			VerticalSizeMode = SizeMode.CanGrow,
			TranslucentBackground = true,
			NoSystemBackground = true,
		};
		scroll.Canvas.Layout.Spacing = 0;

		_rowsCanvas = scroll.Canvas;
		_rows = scroll.Canvas.Layout;
	}

	/// <summary>
	/// Repaint rows so selection highlights stay in sync with the properties target.
	/// </summary>
	internal void UpdateSelection()
	{
		foreach ( var row in _rowWidgets )
			((Widget)row).Update();
	}

	private static void AddDivider( Layout row )
	{
		row.AddSpacingCell( 2 );
		row.Add( new Separator( 1 ) { FixedWidth = 1, FixedHeight = Theme.RowHeight - 8, Color = Color.White.WithAlpha( 0.1f ) } );
		row.AddSpacingCell( 2 );
	}

	private ToolButton HeaderButton( string icon, string toolTip, Action onClick )
	{
		var button = new ToolButton( "", icon, this ) { ToolTip = toolTip, MouseLeftPress = onClick };
		button.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( button.Enabled && Paint.HasMouseOver ? Theme.ControlBackground.Lighten( 0.1f ) : Theme.ControlBackground );
			Paint.DrawRect( button.LocalRect, Theme.ControlRadius );

			Paint.ClearBrush();
			Paint.SetPen( button.Enabled ? Theme.Primary : Theme.TextControl.WithAlpha( 0.25f ) );
			Paint.DrawIcon( button.LocalRect, icon, 14, TextFlag.Center );
			return true;
		};
		return button;
	}

	internal Menu CreateMenu() => new( _window );

	private void ApplyAllPreviews( Action<Parameter> action )
	{
		foreach ( var parameter in _window.Graph?.Parameters ?? [] )
		{
			action( parameter );
			_window.SetPreviewParameter( parameter );
		}

		Rebuild();
	}

	private void ResetAllPreviews()
	{
		var parameters = _window.Graph?.Parameters ?? [];

		foreach ( var parameter in parameters )
		{
			parameter.ResetPreview();
			_window.SetPreviewParameter( parameter );
		}

		Rebuild();
	}

	internal void ResetPreview( Parameter parameter )
	{
		parameter.ResetPreview();
		_window.SetPreviewParameter( parameter );
		Rebuild();
	}

	private List<IParameter> UnusedParameters()
	{
		var graph = _window.Graph;
		if ( graph is null )
			return [];

		return graph.AllParameters
			.Where( parameter => UsageCount( parameter ) == 0 )
			.ToList();
	}

	private void DeleteUnused()
	{
		var unused = UnusedParameters();
		if ( unused.Count == 0 )
			return;

		var confirm = new PopupWindow( "Delete Unused Parameters",
			$"Delete {unused.Count} unused parameter{(unused.Count == 1 ? "" : "s")}?\n{string.Join( ", ", unused.Select( p => p.Name ) )}",
			"Cancel", new Dictionary<string, Action>
			{
				["Delete"] = () =>
				{
					_window.PushGraphUndo( "Delete Unused Parameters" );
					foreach ( var parameter in unused.OfType<VirtualParameter>() )
						_window.OnVirtualParameterRemoved( parameter.Identifier );
					_window.Graph.Parameters.RemoveAll( p => unused.Contains( p ) );
					_window.Graph.VirtualParameters.RemoveAll( p => unused.Contains( p ) );
					_window.PushGraphRedo();
				}
			} );

		confirm.Show();
		confirm.Window.Position = _window.ScreenRect.Center - confirm.Window.Size * 0.5f;
	}

	private string EmptyHint => (_window.Graph?.Parameters.Count ?? 0) + (_window.Graph?.VirtualParameters.Count ?? 0) > 0
		? "No matching parameters"
		: "No parameters\nClick + to add one";

	private IEnumerable<IGrouping<string, IParameter>> FilteredGroups()
	{
		var parameters = (_window.Graph?.Parameters.Cast<IParameter>() ?? [])
			.Concat( _window.Graph?.VirtualParameters ?? [] );
		var filter = _filter.Text?.Trim() ?? "";

		var result = parameters.Where( p => string.IsNullOrEmpty( filter )
			|| p.Name.Contains( filter, StringComparison.OrdinalIgnoreCase )
			|| GroupTitle( GroupName( p ) ).Contains( filter, StringComparison.OrdinalIgnoreCase ) );

		result = _sortAscending switch
		{
			true => result.OrderBy( p => p.Name ),
			false => result.OrderByDescending( p => p.Name ),
			null => result,
		};

		return result
			.GroupBy( GroupName, StringComparer.OrdinalIgnoreCase )
			.OrderBy( group => string.IsNullOrEmpty( group.Key ) ? 0 : 1 )
			.ThenBy( group => group.Key, StringComparer.OrdinalIgnoreCase );
	}

	private IEnumerable<IParameter> DisplayedParameters()
	{
		var filtering = !string.IsNullOrWhiteSpace( _filter.Text );
		foreach ( var group in FilteredGroups() )
		{
			if ( !filtering && _collapsedGroups.Contains( group.Key ) )
				continue;

			foreach ( var parameter in group )
				yield return parameter;
		}
	}

	internal static string GroupName( IParameter parameter ) => NormalizeGroup( parameter.Group );
	internal static string GroupTitle( string group ) => string.IsNullOrEmpty( group ) ? "General" : group;

	public void Rebuild()
	{
		// Only the rows, so a filter being typed into isn't hidden along with them and loses focus
		using var _ = SuspendUpdates.For( _rowsCanvas );

		_rows.Clear( true );
		_rowWidgets.Clear();
		var groups = FilteredGroups().ToList();
		_hasVisibleParameters = groups.Any();
		var filtering = !string.IsNullOrWhiteSpace( _filter.Text );

		foreach ( var group in groups )
		{
			var collapsed = !filtering && _collapsedGroups.Contains( group.Key );
			_rows.Add( new ParameterGroupHeader( this, group.Key, group.Count(), collapsed, !filtering ) );

			if ( collapsed )
				continue;

			var body = new Widget
			{
				Layout = Layout.Column(),
				HorizontalSizeMode = SizeMode.Flexible,
				VerticalSizeMode = SizeMode.CanShrink
			};
			body.Layout.Margin = new Sandbox.UI.Margin( 4, 2, 4, 4 );
			body.Layout.Spacing = 2;
			body.OnPaintOverride = () =>
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.WidgetBackground.Darken( 0.1f ) );
				Paint.DrawRect( body.LocalRect );
				return true;
			};
			_rows.Add( body, 0 );

			foreach ( var parameter in group )
			{
				IParameterRow row = parameter switch
				{
					Parameter control => new ParameterRow( _window, this, control ),
					VirtualParameter virtualParameter => new VirtualParameterRow( _window, this, virtualParameter ),
					_ => throw new NotSupportedException()
				};
				_rowWidgets.Add( row );
				body.Layout.Add( (Widget)row );
			}
		}

		_rows.AddStretchCell();
		UpdateUnusedButton();
	}

	internal void Remove( Parameter parameter )
	{
		var parameterNodes = _window.Graph.AllNodes.OfType<Nodes.ParameterNode>()
			.Where( node => node.ParameterIdentifier == parameter.Identifier
				|| string.Equals( node.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase ) )
			.ToArray();
		var selectors = _window.Graph.AllNodes.OfType<Nodes.IDBasedSelectorNode>()
			.Where( node => node.ParameterIdentifier == parameter.Identifier
				|| string.Equals( node.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase ) )
			.ToArray();

		void Delete()
		{
			_window.PushGraphUndo( "Remove Parameter" );

			foreach ( var node in parameterNodes )
			{
				node.ParameterIdentifier = "";
				node.ParameterName = "";
				node.Update();
			}

			_window.Graph.Parameters.Remove( parameter );

			foreach ( var selector in _window.Graph.AllNodes.OfType<Nodes.IDBasedSelectorNode>() )
				selector.ClearParameterIfMissing();

			_window.PushGraphRedo();
		}

		var referenceCount = parameterNodes.Length + selectors.Length;
		if ( referenceCount == 0 )
		{
			Delete();
			return;
		}

		var usages = new List<string>();
		if ( parameterNodes.Length > 0 )
			usages.Add( $"{parameterNodes.Length} parameter node{(parameterNodes.Length == 1 ? "" : "s")}" );
		if ( selectors.Length > 0 )
			usages.Add( $"{selectors.Length} ID selector{(selectors.Length == 1 ? "" : "s")}" );

		var confirm = new PopupWindow(
			"Delete Parameter",
			$"Delete '{parameter.Name}'?\n\nUsed by {string.Join( " and ", usages )}. These references will be cleared.",
			"Cancel",
			new Dictionary<string, Action> { ["Delete"] = Delete } );
		confirm.Show();
		confirm.Window.Position = _window.ScreenRect.Center - confirm.Window.Size * 0.5f;
	}

	internal static bool AffectsIDSelectors( SerializedProperty property )
	{
		return property is null || ResolveParameterProperty( property )?.Name is nameof( Parameter.Name )
				or nameof( Parameter.Type )
				or nameof( Parameter.EnumType )
				or nameof( Parameter.EnumTypeName )
				or nameof( Parameter.ExpectedValues );
	}

	internal static bool ResetsIDSelectorOptions( SerializedProperty property )
	{
		return ResolveParameterProperty( property )?.Name is nameof( Parameter.EnumType ) or nameof( Parameter.EnumTypeName );
	}

	internal static SerializedProperty ResolveParameterProperty( SerializedProperty property )
	{
		while ( property?.Parent?.ParentProperty is { } parent )
			property = parent;

		return property;
	}

	internal void Duplicate( Parameter parameter )
	{
		var copy = parameter.Duplicate();
		copy.Name = UniqueName( parameter.Name );

		_window.PushGraphUndo( "Duplicate Parameter" );
		_window.Graph.Parameters.Insert( _window.Graph.Parameters.IndexOf( parameter ) + 1, copy );
		_window.PushGraphRedo();

		BeginRename( copy );
	}

	internal void Remove( VirtualParameter parameter )
	{
		_window.PushGraphUndo( "Remove Virtual Parameter" );
		_window.OnVirtualParameterRemoved( parameter.Identifier );
		_window.Graph.VirtualParameters.Remove( parameter );
		_window.PushGraphRedo();
	}

	internal void Duplicate( VirtualParameter parameter )
	{
		var copy = parameter.Duplicate();
		copy.Name = UniqueName( parameter.Name );

		_window.PushGraphUndo( "Duplicate Virtual Parameter" );
		_window.Graph.AddVirtualParameter( copy, _window.Graph.VirtualParameters.IndexOf( parameter ) + 1 );
		_window.PushGraphRedo();

		BeginRename( copy );
	}

	internal void Rename( IParameter parameter, string name )
	{
		name = name?.Trim();

		if ( string.IsNullOrEmpty( name ) || name == parameter.Name )
			return;

		if ( _window.Graph.AllParameters.Any( p => p != parameter && p.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) )
			return;

		_window.PushGraphUndo( "Rename Parameter" );
		_window.Graph.RenameParameter( parameter, name );

		if ( parameter is VirtualParameter virtualParameter )
			_window.OnVirtualParameterRenamed( virtualParameter );
		_window.PushGraphRedo();
		_window.OnParameterSelected( parameter );
	}

	internal void BeginRename( IParameter parameter )
		=> _rowWidgets.FirstOrDefault( r => r.Parameter == parameter )?.StartRename();

	internal void ToggleGroup( string group )
	{
		if ( !string.IsNullOrWhiteSpace( _filter.Text ) )
			return;

		if ( !_collapsedGroups.Add( group ) )
			_collapsedGroups.Remove( group );

		SaveCollapsedGroups();
		Rebuild();
	}

	internal void MoveToGroup( IParameter parameter, string group )
	{
		group = NormalizeGroup( group );
		if ( GroupName( parameter ).Equals( group, StringComparison.OrdinalIgnoreCase ) )
			return;

		_window.PushGraphUndo( "Move Parameter" );
		parameter.Group = group;
		ExpandGroup( group );
		PruneCollapsedGroups();
		_window.PushGraphRedo();
		_window.OnParameterSelected( parameter );
	}

	internal void RenameGroup( string group )
	{
		if ( string.IsNullOrEmpty( group ) )
			return;

		OpenGroupDialog( "Rename Parameter Group", group, newName =>
		{
			newName = NormalizeGroup( newName );
			if ( newName.Equals( group, StringComparison.OrdinalIgnoreCase ) )
				return;

			_window.PushGraphUndo( "Rename Parameter Group" );
			foreach ( var parameter in _window.Graph.AllParameters.Where( p => GroupName( p ).Equals( group, StringComparison.OrdinalIgnoreCase ) ) )
				parameter.Group = newName;

			if ( _collapsedGroups.Remove( group ) && !string.IsNullOrEmpty( newName ) )
				_collapsedGroups.Add( newName );
			PruneCollapsedGroups();

			_window.PushGraphRedo();
		} );
	}

	internal void ClearGroup( string group )
	{
		if ( string.IsNullOrEmpty( group ) )
			return;

		_window.PushGraphUndo( "Remove Parameter Group" );
		foreach ( var parameter in _window.Graph.AllParameters.Where( p => GroupName( p ).Equals( group, StringComparison.OrdinalIgnoreCase ) ) )
			parameter.Group = "";
		_collapsedGroups.Remove( group );
		SaveCollapsedGroups();
		_window.PushGraphRedo();
	}

	internal void AddGroupOptions( Menu menu, IParameter parameter )
	{
		var groups = menu.AddMenu( "Move to Group", "folder" );
		groups.AddOption( "General", string.IsNullOrEmpty( GroupName( parameter ) ) ? "check" : "", () => MoveToGroup( parameter, "" ) );

		foreach ( var group in ExistingGroups() )
			groups.AddOption( group, GroupName( parameter ).Equals( group, StringComparison.OrdinalIgnoreCase ) ? "check" : "", () => MoveToGroup( parameter, group ) );

		groups.AddSeparator();
		groups.AddOption( "New Group…", "create_new_folder", () => OpenGroupDialog( "New Parameter Group", "", group => MoveToGroup( parameter, group ) ) );
	}

	private IEnumerable<string> ExistingGroups() => _window.Graph.AllParameters
		.Select( GroupName )
		.Where( group => !string.IsNullOrEmpty( group ) )
		.Distinct( StringComparer.OrdinalIgnoreCase )
		.OrderBy( group => group, StringComparer.OrdinalIgnoreCase );

	private void OpenGroupDialog( string title, string initialValue, Action<string> accepted )
	{
		Dialog.AskString(
			name => accepted( name.Trim() ),
			"Group name:",
			okay: "Apply",
			initialName: initialValue,
			title: title,
			minLength: 1
		);
	}

	private void OpenAddMenu( Widget anchor )
	{
		var menu = CreateMenu();
		AddControlOptions( menu, "" );
		AddGroupedOptions( menu, false );
		menu.OpenAt( anchor.ScreenRect.BottomLeft );
	}

	private void OpenVirtualAddMenu( Widget anchor )
	{
		var menu = CreateMenu();
		AddVirtualOptions( menu, "" );
		AddGroupedOptions( menu, true );
		menu.OpenAt( anchor.ScreenRect.BottomLeft );
	}

	private void AddGroupedOptions( Menu menu, bool isVirtual )
	{
		var groups = ExistingGroups().ToList();
		if ( groups.Count == 0 )
			return;

		menu.AddSeparator();
		var grouped = menu.AddMenu( "Add to Group", "folder" );
		foreach ( var group in groups )
		{
			var groupMenu = grouped.AddMenu( group, "folder" );
			if ( isVirtual )
				AddVirtualOptions( groupMenu, group );
			else
				AddControlOptions( groupMenu, group );
		}
	}

	internal void AddControlOptions( Menu menu, string group )
	{
		foreach ( var type in AnimParameterUI.ControlTypes )
			menu.AddOption( AnimParameterUI.TypeName( type ), AnimParameterUI.Icon( type ), () => Add( type, group ) );
	}

	internal void AddVirtualOptions( Menu menu, string group )
	{
		foreach ( var type in AnimParameterUI.VirtualTypes )
			menu.AddOption( AnimParameterUI.TypeName( type ), AnimParameterUI.Icon( type ), () => AddVirtual( type, group ) );
	}

	internal void Add( AnimParameterType type, string group = "" )
	{
		group = NormalizeGroup( group );
		var parameter = new Parameter { Name = UniqueName( type.ToString() ), Type = type, Group = group };

		_filter.Text = "";
		ExpandGroup( group );

		_window.PushGraphUndo( "Add Parameter" );
		_window.Graph.Parameters.Add( parameter );
		_window.PushGraphRedo();

		_window.OnParameterSelected( parameter );
		BeginRename( parameter );
	}

	internal void AddVirtual( AnimParameterType type, string group = "" )
	{
		group = NormalizeGroup( group );
		VirtualParameter parameter = type switch
		{
			AnimParameterType.Bool => new BoolVirtualParameter(),
			AnimParameterType.ID => new IDVirtualParameter(),
			AnimParameterType.Float => new FloatVirtualParameter(),
			AnimParameterType.Vector => new VectorVirtualParameter(),
			AnimParameterType.Target => new TargetVirtualParameter(),
			AnimParameterType.BoneMask => new BoneMaskVirtualParameter(),
			_ => throw new ArgumentOutOfRangeException( nameof( type ) )
		};
		parameter.Name = UniqueName( type.ToString() );
		parameter.Group = group;

		_filter.Text = "";
		ExpandGroup( group );

		_window.PushGraphUndo( "Add Virtual Parameter" );
		_window.Graph.AddVirtualParameter( parameter );
		_window.PushGraphRedo();

		_window.OnParameterSelected( parameter );
		BeginRename( parameter );
	}

	private string UniqueName( string baseName ) => _window.Graph.UniqueParameterName( baseName );

	private static string NormalizeGroup( string group )
	{
		group = group?.Trim() ?? "";
		return group.Equals( "General", StringComparison.OrdinalIgnoreCase ) ? "" : group;
	}

	private void ExpandGroup( string group )
	{
		if ( _collapsedGroups.Remove( group ) )
			SaveCollapsedGroups();
	}

	private void PruneCollapsedGroups()
	{
		var groups = _window.Graph.AllParameters.Select( GroupName ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		_collapsedGroups.RemoveWhere( group => !groups.Contains( group ) );
		SaveCollapsedGroups();
	}

	private void SaveCollapsedGroups()
		=> EditorCookie.Set( CollapsedGroupsCookie, _collapsedGroups.OrderBy( x => x ).ToList() );

	internal void OnGraphEdited()
	{
		if ( _window.Graph is null )
			return;

		var parameters = DisplayedParameters().ToList();
		var stale = _rowWidgets.Count != parameters.Count;

		for ( var i = 0; !stale && i < _rowWidgets.Count; i++ )
		{
			var p = parameters[i];
			stale = _rowWidgets[i].Parameter != p || _rowWidgets[i].BuiltGroup != GroupName( p );
			if ( !stale && p is Parameter control && _rowWidgets[i] is ParameterRow row )
			{
				var range = control.HasRange ? new Vector2( control.Min, control.Max ) : (Vector2?)null;
				stale = row.BuiltType != control.Type || row.BuiltRange != range || row.BuiltOptions != ParameterRow.OptionsKey( control );
			}
		}

		if ( stale )
		{
			Rebuild();
			return;
		}

		UpdateUnusedButton();
		UpdateSelection();
	}

	private void UpdateUnusedButton()
		=> _deleteUnusedButton.Enabled = _window.Graph is { } graph && graph.AllParameters.Any( p => UsageCount( p ) == 0 );

	/// <summary>
	/// Uses the same cached references as Find Usages and unused-parameter removal.
	/// </summary>
	internal int UsageCount( IParameter parameter )
		=> _window.GetParameterUsages( parameter ).Count();
}

internal static class AnimParameterUI
{
	public const float NameInset = 26f;
	public const float RowSpacing = 4f;

	public static IReadOnlyList<AnimParameterType> ControlTypes { get; } =
	[
		AnimParameterType.Bool,
		AnimParameterType.ID,
		AnimParameterType.Float,
		AnimParameterType.Vector,
		AnimParameterType.Target
	];

	public static IReadOnlyList<AnimParameterType> VirtualTypes { get; } = Enum.GetValues<AnimParameterType>();

	public static string Icon( AnimParameterType type ) => type switch
	{
		AnimParameterType.Float => "pin",
		AnimParameterType.Bool => "toggle_on",
		AnimParameterType.ID => "label",
		AnimParameterType.Target => "my_location",
		AnimParameterType.BoneMask => "blur_on",
		_ => "open_with",
	};

	public static Color Color( AnimParameterType type ) => type switch
	{
		AnimParameterType.Bool => global::Color.Parse( "#e06c75" ).Value,
		AnimParameterType.ID => global::Color.Parse( "#c678dd" ).Value,
		AnimParameterType.Float => global::Color.Parse( "#98c379" ).Value,
		AnimParameterType.Vector => global::Color.Parse( "#e5c07b" ).Value,
		AnimParameterType.Target => global::Color.Parse( "#56b6c2" ).Value,
		AnimParameterType.BoneMask => global::Color.Parse( "#d19a66" ).Value,
		_ => Theme.TextControl,
	};

	public static string TypeName( AnimParameterType type ) => type == AnimParameterType.BoneMask ? "Bone Mask" : type.ToString();

	public static void PaintRow( Rect rect, bool selected, bool hovered )
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( selected ? Theme.Primary.WithAlpha( 0.2f )
			: hovered ? Theme.ControlBackground : Theme.WidgetBackground.Darken( 0.1f ) );
		Paint.DrawRect( rect.Shrink( 0, 1 ), Theme.ControlRadius );
	}

	public static Rect PaintName( Rect rect, IParameter parameter, bool renaming )
	{
		Paint.SetPen( Color( parameter.Type ) );
		Paint.DrawIcon( new Rect( rect.Left + 4, rect.Top, 16, rect.Height ),
			parameter.IsVirtual ? "functions" : Icon( parameter.Type ), 14, TextFlag.Center );

		if ( renaming )
			return default;

		rect = rect.Shrink( NameInset, 0, 6, 0 );
		Paint.SetPen( Theme.Text );
		Paint.SetDefaultFont();
		var name = Paint.GetElidedText( parameter.Name, Math.Max( 0, rect.Width ), ElideMode.Right );
		return Paint.DrawText( rect, name, TextFlag.LeftCenter | TextFlag.SingleLine );
	}
}

/// <summary>
/// Carried in drag data when dragging a parameter onto the graph.
/// </summary>
public record ParameterDragData( IParameter Parameter );

internal interface IParameterRow
{
	IParameter Parameter { get; }
	string BuiltGroup { get; }
	void StartRename();
}

internal sealed class ParameterGroupHeader : InspectorHeader
{
	private readonly ParameterList _list;
	private readonly string _group;
	private readonly string _title;
	private readonly int _count;
	private bool _dragOver;

	public ParameterGroupHeader( ParameterList list, string group, int count, bool collapsed, bool collapsible )
	{
		_list = list;
		_group = group;
		_title = ParameterList.GroupTitle( group );
		_count = count;

		Title = "";
		Icon = "folder";
		Color = Theme.Blue;
		IsCollapsable = collapsible;
		IsExpanded = !collapsed;
		BuildUI();

		Cursor = collapsible ? CursorShape.Finger : CursorShape.Arrow;
		AcceptDrops = true;
		ToolTip = collapsible
			? "Click to collapse or expand. Drag parameters here to move them into this group. Right-click for group actions."
			: "Matching groups are expanded while filtering. Drag parameters here to move them into this group. Right-click for group actions.";
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );

		base.OnPaint();

		var count = _count.ToString();
		Paint.SetDefaultFont( 8, 500 );
		var countMarginY = MathF.Floor( (LocalRect.Height - 18) * 0.5f );
		var countHeight = LocalRect.Height - countMarginY * 2;
		var countWidth = MathF.Max( countHeight, Paint.MeasureText( count ).x + 8 );
		var countRect = new Rect( LocalRect.Right - countWidth - 12, countMarginY + 1, countWidth, countHeight );

		Paint.SetPen( Theme.Text.WithAlpha( IsExpanded || !IsCollapsable ? 1f : 0.8f ) );
		Paint.SetHeadingFont( 11, 440, sizeInPixels: true );
		Paint.DrawText( LocalRect.Shrink( 48, 0, countWidth + 20, 0 ), _title, TextFlag.LeftCenter | TextFlag.SingleLine );

		Paint.ClearPen();
		Paint.SetBrush( Theme.TextControl.WithAlpha( 0.08f ) );
		Paint.DrawRect( countRect, Theme.ControlRadius );
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.5f ) );
		Paint.SetDefaultFont( 8, 500 );
		Paint.DrawText( countRect, count, TextFlag.Center );

		if ( _dragOver )
		{
			Paint.ClearBrush();
			Paint.SetPen( Theme.Primary, 1.5f );
			Paint.DrawRect( LocalRect.Shrink( 0.75f ) );
		}
	}

	protected override void BuildRightIcons( Layout layout )
	{
	}

	protected override void OnExpandChanged()
	{
		_list.ToggleGroup( _group );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.RightMouseButton )
			OpenContextMenu();
	}

	public override void OnDragHover( DragEvent ev )
	{
		var valid = ev.Data.Object is ParameterDragData data
			&& !ParameterList.GroupName( data.Parameter ).Equals( _group, StringComparison.OrdinalIgnoreCase );
		ev.Action = valid ? DropAction.Move : DropAction.Ignore;
		_dragOver = valid;
		Update();
	}

	public override void OnDragLeave()
	{
		_dragOver = false;
		Update();
	}

	public override void OnDragDrop( DragEvent ev )
	{
		_dragOver = false;
		Update();

		if ( ev.Data.Object is ParameterDragData data )
			_list.MoveToGroup( data.Parameter, _group );
	}

	private void OpenContextMenu()
	{
		var menu = _list.CreateMenu();
		var controls = menu.AddMenu( "Add Control Parameter", "add" );
		_list.AddControlOptions( controls, _group );

		var virtuals = menu.AddMenu( "Add Virtual Parameter", "add_circle" );
		_list.AddVirtualOptions( virtuals, _group );

		if ( !string.IsNullOrEmpty( _group ) )
		{
			menu.AddSeparator();
			menu.AddOption( "Rename Group", "edit", () => _list.RenameGroup( _group ) );
			menu.AddOption( "Remove Group", "folder_off", () => _list.ClearGroup( _group ) );
		}

		menu.OpenAtCursor();
	}
}

internal class ParameterRow : Widget, IParameterRow
{
	public Parameter Parameter { get; }
	IParameter IParameterRow.Parameter => Parameter;
	public string BuiltGroup { get; }
	public AnimParameterType BuiltType { get; }
	public Vector2? BuiltRange { get; }
	public string BuiltOptions { get; }

	private readonly Window _window;
	private readonly ParameterList _list;
	private readonly ControlWidget _control;
	private readonly Widget _nameCell;
	private Widget _targetBoneControl;
	private Widget _targetPositionControl;
	private Widget _targetRotationControl;
	private Vector2? _dragStart;
	private LineEdit _renameEdit;

	public ParameterRow( Window window, ParameterList list, Parameter parameter ) : base( list )
	{
		_window = window;
		_list = list;
		Parameter = parameter;
		parameter.Graph = window.Graph;
		BuiltGroup = ParameterList.GroupName( parameter );
		BuiltType = parameter.Type;
		BuiltRange = parameter.HasRange ? new Vector2( parameter.Min, parameter.Max ) : null;
		BuiltOptions = OptionsKey( parameter );

		FixedHeight = Theme.RowHeight + 2;
		Cursor = CursorShape.None;
		MouseTracking = true;
		FocusMode = FocusMode.Click;
		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 0, 1 );
		Layout.Spacing = 2;

		var header = Layout.AddRow();
		header.Margin = new Sandbox.UI.Margin( AnimParameterUI.NameInset, 0, 0, 0 );
		header.Spacing = AnimParameterUI.RowSpacing;

		_renameEdit = new LineEdit( this ) { Visible = false, FixedHeight = Theme.RowHeight };
		_renameEdit.SetStyles( $"background-color: {Theme.ControlBackground.Hex}; selection-background-color: {Theme.Primary.Hex}; selection-color: white; border: none;" );
		_renameEdit.EditingFinished += FinishRename;

		_nameCell = header.Add( new Widget( this )
		{
			Layout = Layout.Column(),
			TransparentForMouseEvents = true,
			FixedHeight = Theme.RowHeight,
			MinimumWidth = 80,
			HorizontalSizeMode = SizeMode.Flexible
		}, 2 );
		_nameCell.Layout.Margin = new Sandbox.UI.Margin( 0, 0, 2, 0 );
		_nameCell.Layout.AddStretchCell( 1 );
		_nameCell.Layout.Add( _renameEdit );
		_nameCell.Layout.AddStretchCell( 1 );

		var so = parameter.GetSerialized();
		so.OnPropertyChanged += p =>
		{
			p = ParameterList.ResolveParameterProperty( p );

			if ( ParameterList.AffectsIDSelectors( p ) )
			{
				_window.Graph.UpdateParameterReferences( parameter, parameter.Name,
					ParameterList.ResetsIDSelectorOptions( p ) );
			}

			// Transient preview scrubs don't touch the document
			if ( p?.HasAttribute<JsonIgnoreAttribute>() != true )
				_window.MarkDirty();

			_window.SetPreviewParameter( parameter );

			if ( p?.Name == nameof( Parameter.PreviewTargetMode ) )
				UpdateTargetControls();
		};

		if ( parameter.Type == AnimParameterType.Target )
		{
			_control = header.Add( new EnumControlWidget( so.GetProperty( nameof( Parameter.PreviewTargetMode ) ) )
			{
				MinimumWidth = 0,
				HorizontalSizeMode = SizeMode.Flexible
			}, 3 );
			_control.ToolTip = "Target mode";
			_targetBoneControl = AddTargetControl( so, nameof( Parameter.PreviewBone ), "Bone" );
			_targetPositionControl = AddTargetControl( so, nameof( Parameter.PreviewPosition ), "Position" );
			_targetRotationControl = AddTargetControl( so, nameof( Parameter.PreviewRotation ), "Rotation" );
			UpdateTargetControls();
			return;
		}

		var defaultProperty = parameter.Type switch
		{
			AnimParameterType.Float => nameof( Parameter.PreviewFloat ),
			AnimParameterType.Bool => nameof( Parameter.PreviewBool ),
			AnimParameterType.ID => nameof( Parameter.PreviewID ),
			_ => nameof( Parameter.PreviewVector ),
		};

		_control = parameter.IsOptionsID
			? new IDOptionsControlWidget( so.GetProperty( defaultProperty ) )
			: ControlWidget.Create( so.GetProperty( defaultProperty ) );
		_control.MinimumWidth = 0;
		_control.HorizontalSizeMode = SizeMode.Flexible;
		_control.ToolTip = $"{parameter.Name} - preview value";

		if ( _control is FloatControlWidget floatControl )
		{
			floatControl.Label = null;
			if ( parameter.HasRange )
			{
				floatControl.MakeRanged( new Vector2( parameter.Min, parameter.Max ), 0.01f, clamped: true, slider: true );
				floatControl.Prime();
			}
		}

		if ( parameter.Type == AnimParameterType.Bool )
		{
			var cell = header.AddRow( 3 );
			_control.FixedWidth = Theme.RowHeight;
			cell.Add( _control );
			cell.AddStretchCell( 1 );
		}
		else if ( parameter.Type == AnimParameterType.Vector )
		{
			_control.FixedHeight = Theme.RowHeight;
			Layout.Add( _control );
			FixedHeight += Theme.RowHeight + 2;
		}
		else
		{
			header.Add( _control, 3 );
		}
	}

	private Widget AddTargetControl( SerializedObject so, string propertyName, string title )
	{
		var row = Layout.Add( new Widget( this ) { Layout = Layout.Row(), FixedHeight = Theme.RowHeight } );
		row.Layout.Margin = new Sandbox.UI.Margin( AnimParameterUI.NameInset, 0, 0, 0 );
		row.Layout.Spacing = AnimParameterUI.RowSpacing;
		row.Layout.Add( new Label( title, row ) { FixedWidth = 52, Color = Theme.TextControl } );
		var control = ControlWidget.Create( so.GetProperty( propertyName ) );
		control.MinimumWidth = 0;
		control.HorizontalSizeMode = SizeMode.Flexible;
		control.ToolTip = $"Target {title.ToLowerInvariant()}";
		row.Layout.Add( control, 1 );
		return row;
	}

	private void UpdateTargetControls()
	{
		if ( _targetBoneControl is null )
			return;

		using var _ = SuspendUpdates.For( this );

		var showBone = Parameter.PreviewTargetMode == AnimTargetPreviewMode.Bone;
		var showTransform = Parameter.PreviewTargetMode == AnimTargetPreviewMode.Transform;

		_targetBoneControl.Visible = showBone;
		_targetPositionControl.Visible = showTransform;
		_targetRotationControl.Visible = showTransform;

		var extraRows = showBone ? 1 : showTransform ? 2 : 0;
		FixedHeight = Theme.RowHeight + 2 + (Theme.RowHeight + 2) * extraRows;
	}

	internal static string OptionsKey( Parameter parameter )
		=> parameter.Type == AnimParameterType.ID ? string.Join( '\u001F', parameter.Options ) : "";

	public void StartRename()
	{
		if ( _renameEdit.Visible )
			return;

		_nameCell.TransparentForMouseEvents = false;
		_renameEdit.Text = Parameter.Name;
		_renameEdit.Visible = true;
		_renameEdit.SelectAll();
		_renameEdit.Focus();
	}

	private void FinishRename()
	{
		if ( !_renameEdit.Visible )
			return;

		_renameEdit.Visible = false;
		_nameCell.TransparentForMouseEvents = true;
		_list.Rename( Parameter, _renameEdit.Text );
		Update();
	}

	private Rect NameRect
	{
		get
		{
			var rect = LocalRect;
			rect.Height = Theme.RowHeight + 2;
			rect.Right = _nameCell.Position.x + _nameCell.Width;

			return rect;
		}
	}

	protected override void OnPaint()
	{
		var selected = _window.IsSelected( Parameter );
		var hovered = NameRect.IsInside( FromScreen( Application.CursorPosition ) );
		AnimParameterUI.PaintRow( LocalRect, selected, hovered );
		AnimParameterUI.PaintName( NameRect, Parameter, _renameEdit.Visible );
	}

	protected override void OnMouseEnter()
	{
		ToolTip = $"{Parameter.Name}\n{AnimParameterUI.TypeName( Parameter.Type )} parameter\nDrag onto the graph. Double-click to rename. Right-click for actions.";
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !NameRect.IsInside( e.LocalPosition ) )
			return;

		if ( e.LeftMouseButton )
		{
			_dragStart = e.LocalPosition;
			_window.OnParameterSelected( Parameter );
		}
		else if ( e.RightMouseButton )
		{
			_window.OnParameterSelected( Parameter );
			OpenContextMenu();
		}
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		if ( !e.LeftMouseButton || !NameRect.IsInside( e.LocalPosition ) )
			return;

		_dragStart = null;
		StartRename();
	}

	protected override void OnMouseReleased( MouseEvent e ) => _dragStart = null;

	protected override void OnMouseMove( MouseEvent e )
	{
		Cursor = NameRect.IsInside( e.LocalPosition ) ? CursorShape.Finger : CursorShape.None;
		Update();

		if ( _dragStart is null || !e.ButtonState.HasFlag( MouseButtons.Left ) )
			return;

		if ( e.LocalPosition.Distance( _dragStart.Value ) < 10 )
			return;

		_dragStart = null;

		var drag = new Drag( this );
		drag.Data.Text = Parameter.Name;
		drag.Data.Object = new ParameterDragData( Parameter );
		drag.Execute();
	}

	protected override void OnMouseLeave()
	{
		Cursor = CursorShape.None;
		Update();
		base.OnMouseLeave();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		switch ( e.Key )
		{
			case KeyCode.F2:
				StartRename();
				break;
			case KeyCode.Delete:
				_list.Remove( Parameter );
				break;
			case KeyCode.D when e.HasCtrl:
				_list.Duplicate( Parameter );
				break;
			case KeyCode.Escape when _renameEdit.Visible:
				_renameEdit.Text = Parameter.Name;
				_renameEdit.Blur();
				break;
			default:
				base.OnKeyPress( e );
				break;
		}
	}

	private void OpenContextMenu()
	{
		var menu = _list.CreateMenu();
		menu.AddOption( $"Find Usages ({_list.UsageCount( Parameter )})", "manage_search", () => _window.FindParameterUsages( Parameter ) );
		menu.AddOption( "Rename", "edit", StartRename, "F2" );
		menu.AddOption( "Duplicate", "content_copy", () => _list.Duplicate( Parameter ), "Ctrl+D" );
		_list.AddGroupOptions( menu, Parameter );

		menu.AddSeparator();
		menu.AddOption(
			"Reset Preview To Start Value",
			"restart_alt",
			() => _list.ResetPreview( Parameter ) );

		if ( Parameter.Type != AnimParameterType.Target )
		{
			if ( Parameter.Type != AnimParameterType.ID )
				menu.AddOption( "Randomize Preview", "casino", () => ApplyPreview( Parameter.RandomizePreview ) );
		}

		menu.AddSeparator();
		menu.AddOption( "Delete", "delete", () => _list.Remove( Parameter ), "Del" );
		menu.OpenAtCursor();
	}

	private void ApplyPreview( Action action )
	{
		action();
		_window.SetPreviewParameter( Parameter );
		_list.Rebuild();
	}
}

internal sealed class VirtualParameterRow : Widget, IParameterRow
{
	public VirtualParameter Parameter { get; }
	IParameter IParameterRow.Parameter => Parameter;
	public string BuiltGroup { get; }

	private readonly Window _window;
	private readonly ParameterList _list;
	private readonly Widget _nameCell;
	private readonly LineEdit _renameEdit;
	private readonly IconButton _editButton;
	private Vector2? _dragStart;
	private Rect _labelRect;

	public VirtualParameterRow( Window window, ParameterList list, VirtualParameter parameter ) : base( list )
	{
		_window = window;
		_list = list;
		Parameter = parameter;
		BuiltGroup = ParameterList.GroupName( parameter );

		FixedHeight = Theme.RowHeight + 2;
		Cursor = CursorShape.Finger;
		MouseTracking = true;
		FocusMode = FocusMode.Click;
		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( AnimParameterUI.NameInset, 1, 0, 1 );
		Layout.Spacing = AnimParameterUI.RowSpacing;

		_renameEdit = new LineEdit( this ) { Visible = false, FixedHeight = Theme.RowHeight };
		_renameEdit.SetStyles( $"background-color: {Theme.ControlBackground.Hex}; selection-background-color: {Theme.Primary.Hex}; selection-color: white; border: none;" );
		_renameEdit.EditingFinished += FinishRename;

		_nameCell = Layout.Add( new Widget( this ) { Layout = Layout.Column(), TransparentForMouseEvents = true }, 1 );
		_nameCell.Layout.Margin = new Sandbox.UI.Margin( 0, 0, 2, 0 );
		_nameCell.Layout.AddStretchCell();
		_nameCell.Layout.Add( _renameEdit );
		_nameCell.Layout.AddStretchCell();

		_editButton = Layout.Add( new IconButton( "account_tree", () => _window.NavigateInto( Parameter ), this )
		{
			ToolTip = "Edit virtual parameter graph",
			IconSize = 14,
			FixedSize = Theme.RowHeight,
			Foreground = Theme.TextControl,
			Background = Color.Transparent
		} );
	}

	public void StartRename()
	{
		if ( _renameEdit.Visible )
			return;

		_nameCell.TransparentForMouseEvents = false;
		_renameEdit.Text = Parameter.Name;
		_renameEdit.Visible = true;
		_renameEdit.SelectAll();
		_renameEdit.Focus();
	}

	private void FinishRename()
	{
		if ( !_renameEdit.Visible )
			return;

		_renameEdit.Visible = false;
		_nameCell.TransparentForMouseEvents = true;
		_list.Rename( Parameter, _renameEdit.Text );
		Update();
	}

	protected override void OnPaint()
	{
		var selected = _window.IsSelected( Parameter );
		var hovered = Paint.HasMouseOver;
		var name = LocalRect;
		name.Right = _editButton.Position.x - AnimParameterUI.RowSpacing;
		AnimParameterUI.PaintRow( LocalRect, selected, hovered );
		_labelRect = AnimParameterUI.PaintName( name, Parameter, _renameEdit.Visible );
	}

	protected override void OnMouseEnter()
	{
		ToolTip = $"{Parameter.Name}\n{AnimParameterUI.TypeName( Parameter.Type )} virtual parameter\nDrag onto the graph. Press Enter to edit. Right-click for actions.";
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			_dragStart = e.LocalPosition;
			_window.OnParameterSelected( Parameter );
		}
		else if ( e.RightMouseButton )
		{
			_window.OnParameterSelected( Parameter );
			OpenContextMenu();
		}
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		if ( !e.LeftMouseButton )
			return;

		_dragStart = null;
		if ( _labelRect.IsInside( e.LocalPosition ) )
			StartRename();
		else
			_window.NavigateInto( Parameter );
	}

	protected override void OnMouseReleased( MouseEvent e ) => _dragStart = null;

	protected override void OnMouseMove( MouseEvent e )
	{
		Update();
		if ( _dragStart is null || !e.ButtonState.HasFlag( MouseButtons.Left ) || e.LocalPosition.Distance( _dragStart.Value ) < 10 )
			return;

		_dragStart = null;
		var drag = new Drag( this );
		drag.Data.Text = Parameter.Name;
		drag.Data.Object = new ParameterDragData( Parameter );
		drag.Execute();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		switch ( e.Key )
		{
			case KeyCode.F2:
				StartRename();
				break;
			case KeyCode.Delete:
				_list.Remove( Parameter );
				break;
			case KeyCode.D when e.HasCtrl:
				_list.Duplicate( Parameter );
				break;
			case KeyCode.Enter:
				_window.NavigateInto( Parameter );
				break;
			case KeyCode.Escape when _renameEdit.Visible:
				_renameEdit.Text = Parameter.Name;
				_renameEdit.Blur();
				break;
			default:
				base.OnKeyPress( e );
				break;
		}
	}

	private void OpenContextMenu()
	{
		var menu = _list.CreateMenu();
		menu.AddOption( $"Find Usages ({_list.UsageCount( Parameter )})", "manage_search", () => _window.FindParameterUsages( Parameter ) );
		menu.AddOption( "Edit", "edit", () => _window.NavigateInto( Parameter ), "Enter" );
		menu.AddOption( "Rename", "drive_file_rename_outline", StartRename, "F2" );
		menu.AddOption( "Duplicate", "content_copy", () => _list.Duplicate( Parameter ), "Ctrl+D" );
		_list.AddGroupOptions( menu, Parameter );
		menu.AddSeparator();
		menu.AddOption( "Delete", "delete", () => _list.Remove( Parameter ), "Del" );
		menu.OpenAtCursor();
	}
}
