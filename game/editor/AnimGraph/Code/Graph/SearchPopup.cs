using Editor.AnimGraph.Nodes;
using System.Collections;

namespace Editor.AnimGraph;

internal sealed class SearchPopup : PopupWidget
{
	private const string ActiveTypesCookie = "animgraph.search.active-types";
	private const string ExcludedTypesCookie = "animgraph.search.excluded-types";

	private readonly Window _window;
	private readonly SearchLineEdit _search;
	private readonly IconButton.WithCornerIcon _typeFilter;
	private readonly Layout _filterTags;
	private readonly SearchResultList _results;
	private readonly Label _summary;
	private readonly List<SearchResult> _index;
	private readonly HashSet<string> _activeTypes;
	private readonly HashSet<string> _excludedTypes;
	private readonly Dictionary<Type, PropertyDescription[]> _searchProperties = new();

	private sealed record SearchType( string Key, string Title, string Group, string Icon, Color Color );
	private sealed record SearchResult( string Title, string Context, string Icon, Color Color, SearchType Type, string SearchText, Action Activate );

	public SearchPopup( Window window, HashSet<string> activeTypes, HashSet<string> excludedTypes ) : base( window )
	{
		_window = window;
		_activeTypes = activeTypes;
		_excludedTypes = excludedTypes;
		FixedSize = new Vector2( 580, 440 );

		Layout = Layout.Column();
		Layout.Margin = 6;
		Layout.Spacing = 4;

		var searchRow = Layout.AddRow();
		searchRow.Spacing = 4;

		_search = searchRow.Add( new SearchLineEdit( this ) { ClearButtonEnabled = true }, 1 );
		_search.PlaceholderText = "Search nodes, properties, states and parameters...";
		_search.FixedHeight = Theme.RowHeight;
		_search.TextEdited += _ => Refresh();
		_search.KeyPressed = HandleSearchKey;

		var filterTags = searchRow.Add( new Widget( this ) { Layout = Layout.Row() } );
		filterTags.Layout.Spacing = 2;
		_filterTags = filterTags.Layout;

		_typeFilter = searchRow.Add( new IconButton.WithCornerIcon( "filter_list", OpenTypeFilter, this )
		{
			ToolTip = "Filter result types",
			IconSize = 15,
			CornerIconSize = 11,
			CornerIconColor = Theme.TextControl.WithAlpha( 0.5f )
		} );

		_results = Layout.Add( new SearchResultList( this ), 1 );
		_results.ItemActivated = item => Activate( item as SearchResult );

		_summary = Layout.Add( new Label( "", this ) );
		_summary.FixedHeight = 16;
		_summary.Color = Theme.TextControl.WithAlpha( 0.45f );
		_summary.Alignment = TextFlag.LeftCenter;
		_index = BuildIndex();
		RestoreTypeFilter();
		UpdateTypeFilter();
		_search.Focus();
	}

	private List<SearchResult> BuildIndex()
	{
		var results = new List<SearchResult>();

		foreach ( var node in _window.Graph.AllNodes )
		{
			var title = SearchTitle( node );
			var path = _window.GetNodePath( node );
			var searchText = SearchableText( node, title, path, node.DisplayInfo.Description );
			var display = DisplayInfo.ForType( node.GetType() );
			var nodeType = new SearchType( $"node:{node.GetType().FullName}", display.Name,
				string.IsNullOrWhiteSpace( display.Group ) ? "Nodes" : display.Group,
				display.Icon ?? node.DisplayInfo.Icon ?? "account_tree", node.PrimaryColor );
			results.Add( new( title, path, nodeType.Icon, nodeType.Color, nodeType, searchText,
				() => _window.NavigateToNode( node.Identifier ) ) );

			if ( node is not StateMachineNode machine )
				continue;

			foreach ( var state in machine.States )
			{
				var context = $"{path} › {machine.DisplayInfo.Name} · State";
				var stateType = new SearchType( "state", "State", "State Machine", "animation", Theme.Red );
				results.Add( new( state.Name, context, stateType.Icon, stateType.Color, stateType,
					SearchableText( state, state.Name, context ),
					() => _window.NavigateToStateMachineItem( machine, new StateMachineCanvas.StateSelection( state.Name ) ) ) );
			}

			foreach ( var conduit in machine.Conduits )
			{
				var conduitTitle = $"{conduit.From} → {conduit.To}";
				var context = $"{path} › {machine.DisplayInfo.Name} · Transition";
				var transitionType = new SearchType( "transition", "Transition", "State Machine", "route", Theme.Pink );
				results.Add( new( conduitTitle, context, transitionType.Icon, transitionType.Color, transitionType,
					SearchableText( conduit, conduitTitle, context ),
					() => _window.NavigateToStateMachineItem( machine, new StateMachineCanvas.ConduitSelection( conduit.From, conduit.To ) ) ) );
			}
		}

		foreach ( var parameter in _window.Graph.AllParameters )
		{
			var kind = parameter.IsVirtual ? "Virtual Parameter" : "Control Parameter";
			var context = string.IsNullOrWhiteSpace( parameter.Group ) ? kind : $"{kind} · {parameter.Group}";
			var parameterType = new SearchType( parameter.IsVirtual ? "virtual-parameter" : "control-parameter", kind, "Parameters",
				AnimParameterUI.Icon( parameter.Type ), AnimParameterUI.Color( parameter.Type ) );
			results.Add( new( parameter.Name, context, parameterType.Icon, parameterType.Color, parameterType,
				SearchableText( parameter, parameter.Name, context, parameter.Type.ToString() ),
				() => _window.NavigateToParameter( parameter ) ) );
		}

		return results;
	}

	private void OpenTypeFilter()
	{
		var popup = new PopupWidget( _typeFilter )
		{
			FixedSize = new Vector2( 460, 300 ),
			Layout = Layout.Column()
		};
		popup.Layout.Margin = 8;
		popup.Layout.Spacing = 4;

		var header = popup.Layout.AddRow();
		header.Add( new Widget( popup ) { FixedWidth = 2 } );
		var heading = header.Add( new Label.Header( "Filter Result Types", popup ), 1 );
		heading.FixedHeight = Theme.RowHeight;
		heading.Alignment = TextFlag.LeftCenter;
		var clear = header.Add( new IconButton( "filter_alt_off", parent: popup )
		{
			ToolTip = "Clear type filters",
			IconSize = 15,
			Enabled = _activeTypes.Count + _excludedTypes.Count > 0
		} );

		var columns = popup.Layout.AddRow( 1 );
		columns.Spacing = 6;
		var syncGroups = new List<Action>();

		void ApplyFilter()
		{
			clear.Enabled = _activeTypes.Count + _excludedTypes.Count > 0;
			UpdateTypeFilter();
		}

		foreach ( var groups in BuildTypeColumns() )
		{
			var scroll = columns.Add( new ScrollArea( popup ), 1 );
			scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
			scroll.TranslucentBackground = true;
			scroll.NoSystemBackground = true;
			scroll.SetStyles( "border: none; background: transparent;" );

			var canvas = new Widget( scroll ) { Layout = Layout.Column() };
			canvas.Layout.Spacing = 2;
			scroll.Canvas = canvas;

			var firstGroup = true;
			foreach ( var group in groups )
			{
				if ( !firstGroup )
					canvas.Layout.Add( new Widget( canvas ) { FixedHeight = 2 } );

				firstGroup = false;
				var types = group.OrderBy( type => type.Title ).ToArray();
				var groupToggle = canvas.Layout.Add( new Checkbox( group.Key, canvas ) { TriState = true } );
				var entries = new List<(SearchType Type, TagPicker.TagOption Entry)>();
				var syncing = false;

				void SyncGroup()
				{
					syncing = true;

					var active = types.Count( type => _activeTypes.Contains( type.Key ) );
					var excluded = types.Count( type => _excludedTypes.Contains( type.Key ) );
					groupToggle.State = active == types.Length
						? CheckState.On
						: active > 0 || excluded > 0 ? CheckState.Partial : CheckState.Off;

					foreach ( var (type, entry) in entries )
					{
						entry.IsSelected = _activeTypes.Contains( type.Key );
						entry.IsExcluded = _excludedTypes.Contains( type.Key );
						entry.Update();
					}

					syncing = false;
				}

				groupToggle.StateChanged = state =>
				{
					if ( syncing )
						return;

					foreach ( var type in types )
					{
						_excludedTypes.Remove( type.Key );

						if ( state == CheckState.Off )
							_activeTypes.Remove( type.Key );
						else
							_activeTypes.Add( type.Key );
					}

					SyncGroup();
					ApplyFilter();
				};

				foreach ( var type in types )
				{
					var entry = CreateTypeOption( canvas, type, () =>
					{
						SyncGroup();
						ApplyFilter();
					} );
					entries.Add( (type, entry) );
					canvas.Layout.Add( entry );
				}

				SyncGroup();
				syncGroups.Add( SyncGroup );
			}

			canvas.Layout.AddStretchCell();
		}

		clear.OnClick = () =>
		{
			_activeTypes.Clear();
			_excludedTypes.Clear();

			foreach ( var sync in syncGroups )
				sync();

			ApplyFilter();
		};

		var searchRect = ScreenRect;
		popup.OpenAt( new Vector2( searchRect.Right + 4, _typeFilter.ScreenRect.Top ), false );

		if ( searchRect.IsInside( popup.ScreenRect ) )
		{
			popup.Position = new Vector2( searchRect.Right - popup.Width, searchRect.Bottom + 4 );
			popup.ConstrainToScreen();
		}

		if ( searchRect.IsInside( popup.ScreenRect ) )
		{
			popup.Position = new Vector2( searchRect.Left - popup.Width - 4, _typeFilter.ScreenRect.Top );
			popup.ConstrainToScreen();
		}
	}

	private List<IGrouping<string, SearchType>>[] BuildTypeColumns()
	{
		var groups = _index.Select( result => result.Type )
			.DistinctBy( type => type.Key )
			.GroupBy( type => type.Group )
			.OrderByDescending( group => group.Count() )
			.ToArray();

		var columns = new[] { new List<IGrouping<string, SearchType>>(), new List<IGrouping<string, SearchType>>() };
		var sizes = new int[2];

		foreach ( var group in groups )
		{
			var column = sizes[0] <= sizes[1] ? 0 : 1;
			columns[column].Add( group );
			sizes[column] += group.Count() + 1;
		}

		return columns;
	}

	private TagPicker.TagOption CreateTypeOption( Widget parent, SearchType type, Action onChanged )
	{
		var entry = new TagPicker.TagOption( parent, new TagPicker.Option( type.Key )
		{
			Title = type.Title,
			Icon = type.Icon,
			Color = type.Color,
			Count = () => _index.Count( result => result.Type.Key == type.Key )
		} )
		{
			IsSelected = _activeTypes.Contains( type.Key ),
			IsExcluded = _excludedTypes.Contains( type.Key )
		};

		entry.MouseLeftPress = () =>
		{
			ToggleType( _activeTypes, _excludedTypes, type.Key );
			entry.IsSelected = _activeTypes.Contains( type.Key );
			entry.IsExcluded = false;
			entry.Update();
			onChanged();
		};

		entry.MouseRightPress = () =>
		{
			ToggleType( _excludedTypes, _activeTypes, type.Key );
			entry.IsSelected = false;
			entry.IsExcluded = _excludedTypes.Contains( type.Key );
			entry.Update();
			onChanged();
		};

		return entry;
	}

	private static void ToggleType( HashSet<string> target, HashSet<string> other, string key )
	{
		other.Remove( key );

		if ( !target.Add( key ) )
			target.Remove( key );
	}

	private void UpdateTypeFilter()
	{
		var count = _activeTypes.Count + _excludedTypes.Count;
		_typeFilter.IsActive = count > 0;
		_typeFilter.ToolTip = count > 0 ? $"Filter result types ({count} active)" : "Filter result types";
		_filterTags.Clear( true );

		foreach ( var (key, excluded) in _activeTypes.Select( key => (key, false) ).Concat( _excludedTypes.Select( key => (key, true) ) ) )
		{
			var type = _index.Select( result => result.Type ).FirstOrDefault( type => type.Key == key );
			if ( type is null )
				continue;

			var tag = _filterTags.Add( new TagPicker.TagEntry( null, new TagPicker.Option( type.Key )
			{
				Title = type.Title,
				Icon = type.Icon,
				Color = type.Color
			}, excluded ) );
			tag.MouseLeftPress = () =>
			{
				_activeTypes.Remove( key );
				_excludedTypes.Remove( key );
				UpdateTypeFilter();
			};
		}

		ProjectCookie.Set( ActiveTypesCookie, string.Join( ';', _activeTypes.Order() ) );
		ProjectCookie.Set( ExcludedTypesCookie, string.Join( ';', _excludedTypes.Order() ) );
		Refresh();
	}

	private void RestoreTypeFilter()
	{
		var available = _index.Select( result => result.Type.Key ).ToHashSet();
		_activeTypes.RemoveWhere( key => !available.Contains( key ) );
		_excludedTypes.RemoveWhere( key => !available.Contains( key ) );

		if ( _activeTypes.Count > 0 || _excludedTypes.Count > 0 )
			return;

		if ( ProjectCookie.TryGet( ActiveTypesCookie, out string active ) )
			_activeTypes.UnionWith( active.Split( ';', StringSplitOptions.RemoveEmptyEntries ).Where( available.Contains ) );

		if ( ProjectCookie.TryGet( ExcludedTypesCookie, out string excluded ) )
			_excludedTypes.UnionWith( excluded.Split( ';', StringSplitOptions.RemoveEmptyEntries ).Where( available.Contains ) );
	}

	private void Refresh()
	{
		var terms = (_search.Text ?? "").Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		var matches = _index
			.Select( result => (Result: result, Score: Score( result, terms )) )
			.Where( match => match.Score >= 0 && MatchesTypeFilter( match.Result ) )
			.OrderByDescending( match => match.Score )
			.ThenBy( match => match.Result.Title, StringComparer.OrdinalIgnoreCase )
			.Take( 100 )
			.Select( match => match.Result )
			.ToArray();

		_results.SetItems( matches );

		if ( matches.FirstOrDefault() is { } first )
			_results.SelectItem( first );

		_summary.Text = matches.Length switch
		{
			0 => "No results",
			1 => "1 result",
			100 => "Showing the best 100 results",
			var count => $"{count} results"
		};
	}

	private bool MatchesTypeFilter( SearchResult result )
	{
		if ( _excludedTypes.Contains( result.Type.Key ) )
			return false;

		return _activeTypes.Count == 0 || _activeTypes.Contains( result.Type.Key );
	}

	private static int Score( SearchResult result, string[] terms )
	{
		if ( terms.Length == 0 )
			return 0;

		var score = 0;
		foreach ( var term in terms )
		{
			if ( !result.SearchText.Contains( term, StringComparison.OrdinalIgnoreCase ) )
				return -1;

			if ( result.Title.Equals( term, StringComparison.OrdinalIgnoreCase ) )
				score += 1000;
			else if ( result.Title.StartsWith( term, StringComparison.OrdinalIgnoreCase ) )
				score += 500;
			else if ( result.Title.Contains( term, StringComparison.OrdinalIgnoreCase ) )
				score += 250;
			else if ( result.Context.Contains( term, StringComparison.OrdinalIgnoreCase ) )
				score += 50;
			else
				score += 10;
		}

		return score;
	}

	private void HandleSearchKey( KeyEvent e )
	{
		switch ( e.Key )
		{
			case KeyCode.Down:
				_results.SelectMoveRow( 1 );
				break;
			case KeyCode.Up:
				_results.SelectMoveRow( -1 );
				break;
			case KeyCode.Enter:
			case KeyCode.Return:
				Activate( _results.SelectedItems.FirstOrDefault() as SearchResult );
				break;
			case KeyCode.Escape:
				Destroy();
				break;
			default:
				return;
		}

		e.Accepted = true;
	}

	private void Activate( SearchResult result )
	{
		if ( result is null )
			return;

		Destroy();
		result.Activate();
	}

	private string SearchTitle( Node node )
	{
		if ( node is CommentNode comment && !string.IsNullOrWhiteSpace( comment.Title ) )
			return comment.Title;

		var typeName = node.DisplayInfo.Name ?? node.GetType().Name;
		var key = GetSearchProperties( node.GetType() )
			.Where( property => property.PropertyType == typeof( string )
				&& PropertyNamePriority( property.Name ) < 10 )
			.OrderBy( property => PropertyNamePriority( property.Name ) )
			.Select( property => property.GetValue( node ) as string )
			.FirstOrDefault( value => !string.IsNullOrWhiteSpace( value ) );

		return key is null ? typeName : $"{typeName} · {key}";
	}

	private static int PropertyNamePriority( string name ) => name switch
	{
		"Name" => 0,
		"AnimationName" => 1,
		"ParameterName" => 2,
		"StateName" => 3,
		"Id" or "ID" => 4,
		_ => 10
	};

	private string SearchableText( object target, params string[] text )
	{
		var values = new List<string>( text.Where( x => !string.IsNullOrWhiteSpace( x ) ) );
		AppendPropertyValues( target, values, 0 );
		return string.Join( '\n', values );
	}

	private void AppendPropertyValues( object target, List<string> values, int depth )
	{
		if ( target is null || depth > 1 )
			return;

		if ( target is string text )
		{
			if ( !string.IsNullOrWhiteSpace( text ) )
				values.Add( text );

			return;
		}

		if ( EditorTypeLibrary.GetType( target.GetType() )?.IsValueType == true )
		{
			values.Add( target.ToString() );
			return;
		}

		foreach ( var property in GetSearchProperties( target.GetType() ) )
		{
			var value = property.GetValue( target );
			if ( value is string propertyText )
			{
				if ( !string.IsNullOrWhiteSpace( propertyText ) )
					values.Add( propertyText );
			}
			else if ( value is IEnumerable collection )
			{
				foreach ( var item in collection )
					AppendPropertyValues( item, values, depth + 1 );
			}
			else if ( value is not null && EditorTypeLibrary.GetType( value.GetType() )?.IsValueType == true )
			{
				values.Add( value.ToString() );
			}
		}
	}

	private PropertyDescription[] GetSearchProperties( Type type )
	{
		if ( _searchProperties.TryGetValue( type, out var properties ) )
			return properties;

		return _searchProperties[type] = EditorTypeLibrary.GetType( type )?.Properties.Where( property =>
			property.IsPublic
			&& property.CanRead
			&& !property.IsIndexer
			&& property.Attributes.Any( attribute => attribute is PropertyAttribute )
			&& property.Attributes.All( attribute => attribute is not HideAttribute ) ).ToArray() ?? [];
	}

	private sealed class SearchResultList : ListView
	{
		public SearchResultList( Widget parent ) : base( parent )
		{
			ItemSize = new Vector2( 0, 42 );
			ItemSpacing = 0;
			Margin = 0;
		}

		protected override void PaintItem( VirtualWidget item )
		{
			if ( item.Object is not SearchResult result )
				return;

			var active = item.Selected || item.Pressed;
			if ( active || item.Hovered )
			{
				Paint.ClearPen();
				Paint.SetBrush( active ? Theme.SelectedBackground : Theme.SelectedBackground.WithAlpha( 0.25f ) );
				Paint.DrawRect( item.Rect );
			}

			var icon = new Rect( item.Rect.Position + new Vector2( 10, 0 ), new Vector2( 22, item.Rect.Height ) );
			Paint.SetPen( result.Color.WithAlpha( active || item.Hovered ? 1f : 0.8f ) );
			Paint.DrawIcon( icon, result.Icon, 17, TextFlag.Center );

			var title = item.Rect;
			title.Left += 42;
			title.Top += 4;
			title.Bottom = title.Top + 19;
			Paint.SetDefaultFont( 8, active ? 600 : 400 );
			Paint.SetPen( Theme.TextControl.WithAlpha( active || item.Hovered ? 1f : 0.85f ) );
			Paint.DrawText( title, result.Title, TextFlag.LeftCenter | TextFlag.SingleLine );

			var context = title;
			context.Top += 17;
			context.Bottom += 17;
			Paint.SetDefaultFont( 7 );
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.4f ) );
			Paint.DrawText( context, result.Context, TextFlag.LeftCenter | TextFlag.SingleLine );
		}

		protected override bool OnItemPressed( VirtualWidget item, MouseEvent e )
		{
			if ( !e.LeftMouseButton || item.Object is not SearchResult result )
				return false;

			OnItemActivated( result );
			return true;
		}
	}
}
