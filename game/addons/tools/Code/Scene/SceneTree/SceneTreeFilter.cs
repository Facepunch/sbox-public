namespace Editor;

/// <summary>
/// One condition of the hierarchy's advanced filter, shown as a chip in the search box.
/// </summary>
/// <param name="Field">What is tested.</param>
/// <param name="Negate">Match objects that fail the test instead.</param>
/// <param name="Value">The name text or tag, for <see cref="Fields.Name"/> and <see cref="Fields.Tag"/>.</param>
/// <param name="Type">The component type, for <see cref="Fields.Component"/>.</param>
public sealed record SceneTreeFilter( SceneTreeFilter.Fields Field, bool Negate, string Value = null, TypeDescription Type = null )
{
	public enum Fields
	{
		[Icon( "badge" )] Name,
		[Icon( "extension" )] Component,
		[Icon( "sell" )] Tag,
		[Icon( "lock" )] Static,
		[Icon( "edit_off" ), Title( "Editor Only" )] EditorOnly,
	}

	/// <summary>
	/// Whether this field is a yes/no property of the object, picked with a checkbox.
	/// </summary>
	public static bool IsFlag( Fields field ) => field is Fields.Static or Fields.EditorOnly;

	public static string Title( Fields field ) => field == Fields.EditorOnly ? "Editor Only" : field.ToString();

	/// <summary>
	/// What the chip says, e.g. "Component = Model Renderer", "Not Static", "Tag != player".
	/// </summary>
	public string Label => IsFlag( Field )
		? $"{(Negate ? "Not " : "")}{Title( Field )}"
		: $"{Title( Field )} {(Negate ? "!=" : "=")} {(Field == Fields.Component ? Type?.Title : Value)}";

	public bool Matches( GameObject go )
	{
		var matched = Field switch
		{
			Fields.Name => go.Name.Contains( Value, StringComparison.OrdinalIgnoreCase ),
			// The picked type or anything deriving from it, like the inspector's component lookups.
			Fields.Component => Type?.TargetType is { } type && go.Components.GetAll().Any( type.IsInstanceOfType ),
			Fields.Tag => go.Tags.Has( Value ),
			Fields.Static => go.IsStatic,
			Fields.EditorOnly => go.Flags.Contains( GameObjectFlags.EditorOnly ),
			_ => true,
		};

		return matched != Negate;
	}
}

/// <summary>
/// The popup behind the hierarchy's filter button: one editable row per active filter, stacked
/// above a blank row that adds a new one.
/// </summary>
internal sealed class SceneTreeFilterPopup : PopupWidget
{
	readonly SceneTreeWidget _tree;
	readonly Layout _rows;

	/// <summary>
	/// Set while a row is pushing its own edit, so the rows aren't rebuilt under the one being typed in.
	/// </summary>
	bool _applying;

	public SceneTreeFilterPopup( SceneTreeWidget tree ) : base( tree )
	{
		_tree = tree;

		FixedWidth = 460;
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 6;

		Layout.Add( new Label( "Advanced filter" ) ).SetStyles( "font-weight: bold;" );
		Layout.Add( new Label( "Objects must match every condition and the search text." ) { WordWrap = true } );

		_rows = Layout.AddColumn();
		_rows.Spacing = 4;

		var create = Layout.Add( new SceneTreeConditionEditor( this, null ) );
		create.Submitted = filter =>
		{
			_tree.AddFilter( filter );
			create.Reset();
		};

		Layout.AddSeparator( true );

		var disabled = Layout.Add( new Checkbox( "Include disabled objects" ) { Value = tree.IncludeDisabled } );
		disabled.ToolTip = "List objects that are disabled, or sit under a disabled parent";
		disabled.Toggled = () => tree.IncludeDisabled = disabled.Value;

		var footer = Layout.AddRow();
		footer.AddStretchCell();
		footer.Add( new Button( "Clear filters", "filter_alt_off" ) { Clicked = tree.ClearFilters } );

		tree.FiltersChanged += OnFiltersChanged;
		RebuildRows();
	}

	public override void OnDestroyed()
	{
		_tree.FiltersChanged -= OnFiltersChanged;
		base.OnDestroyed();
	}

	void OnFiltersChanged()
	{
		if ( !_applying )
			RebuildRows();
	}

	void RebuildRows()
	{
		if ( !IsValid )
			return;

		_rows.Clear( true );

		foreach ( var filter in _tree.Filters )
		{
			var current = filter;
			var row = _rows.Add( new SceneTreeConditionEditor( this, filter ) );

			// Edits apply as soon as the row describes a complete condition.
			row.Edited = edited =>
			{
				var count = _tree.Filters.Count;

				_applying = true;
				try
				{
					_tree.ReplaceFilter( current, edited );
					current = edited;
				}
				finally
				{
					_applying = false;
				}

				// The edit matched another filter and merged into it; drop this row.
				if ( _tree.Filters.Count != count )
					RebuildRows();
			};
			row.Submitted = _ => _tree.RemoveFilter( current );
		}

		AdjustSize();
	}

	/// <summary>
	/// Open under <paramref name="anchor"/>, right-aligned with it like the scene compile popup.
	/// </summary>
	public static void Open( SceneTreeWidget tree, Widget anchor )
	{
		var popup = new SceneTreeFilterPopup( tree );
		popup.AdjustSize();
		popup.OpenAt( anchor.ScreenRect.BottomRight + new Vector2( -popup.Width, 4 ), false );
	}
}

/// <summary>
/// One "field / = or != / value" row of the advanced filter. Edits an existing filter, or builds a
/// new one when created without.
/// </summary>
/// <remarks>
/// Choices use the editor's own controls, which open their own menu; a <see cref="ComboBox"/>'s
/// dropdown won't open from inside a popup.
/// </remarks>
internal sealed class SceneTreeConditionEditor : Widget
{
	readonly bool _isNew;
	readonly Button _operator;
	readonly LineEdit _name;
	readonly Button _component;
	readonly Button _tag;
	readonly Checkbox _flag;
	readonly Button _action;

	bool _negate;
	TypeDescription _componentType;
	string _tagValue;
	bool _loading;

	/// <summary>
	/// What the condition tests. A property so the enum dropdown control can edit it.
	/// </summary>
	[Property] public SceneTreeFilter.Fields Field { get; set; }

	/// <summary>
	/// An existing row changed into another complete condition.
	/// </summary>
	public Action<SceneTreeFilter> Edited { get; set; }

	/// <summary>
	/// The row's button: Add on the new row (with the condition), remove on an existing one.
	/// </summary>
	public Action<SceneTreeFilter> Submitted { get; set; }

	public SceneTreeConditionEditor( Widget parent, SceneTreeFilter filter ) : base( parent )
	{
		_isNew = filter is null;

		Layout = Layout.Row();
		Layout.Spacing = 4;

		if ( filter is not null )
		{
			Field = filter.Field;
			_negate = filter.Negate;
			_componentType = filter.Type;
			_tagValue = filter.Field == SceneTreeFilter.Fields.Tag ? filter.Value : null;
		}

		var serialized = this.GetSerialized();
		serialized.OnPropertyChanged = _ =>
		{
			OnFieldChanged();
			Apply();
		};

		var field = Layout.Add( ControlWidget.Create( serialized.GetProperty( nameof( Field ) ) ) );
		field.FixedWidth = 130;
		field.FixedHeight = Theme.RowHeight;

		_operator = Layout.Add( new Button( string.Empty, this ) { FixedWidth = 44, ToolTip = "Click to switch between = and !=" } );
		_operator.Clicked = () =>
		{
			_negate = !_negate;
			UpdateOperator();
			Apply();
		};

		// One value input per kind of field; only the current field's is shown.
		_name = Layout.Add( new LineEdit( this ) { FixedHeight = Theme.RowHeight, PlaceholderText = "Name" }, 1 );
		_name.TextChanged += _ => Apply();
		_name.ReturnPressed += () => { if ( _isNew ) Submit(); };

		_component = Layout.Add( new Button( string.Empty, this ) { ToolTip = "Pick the component type" }, 1 );
		_component.Clicked = PickComponent;

		_tag = Layout.Add( new Button( string.Empty, this ) { ToolTip = "Pick a tag used in the scene, or type one" }, 1 );
		_tag.Clicked = PickTag;

		_flag = Layout.Add( new Checkbox( this ) { Value = !(filter?.Negate ?? false) }, 1 );
		_flag.Toggled = () =>
		{
			UpdateFlag();
			Apply();
		};

		_action = _isNew
			? Layout.Add( new Button.Primary( "Add", "add" ) { Clicked = Submit } )
			: Layout.Add( new Button( string.Empty, "close", this ) { FixedWidth = Theme.RowHeight, ToolTip = "Remove this condition", Clicked = Submit } );

		_loading = true;
		if ( filter?.Field == SceneTreeFilter.Fields.Name )
			_name.Text = filter.Value;
		_loading = false;

		OnFieldChanged();
	}

	/// <summary>
	/// The condition this row describes, or null while it still needs a value.
	/// </summary>
	SceneTreeFilter Build() => Field switch
	{
		SceneTreeFilter.Fields.Name => string.IsNullOrWhiteSpace( _name.Text ) ? null : new SceneTreeFilter( Field, _negate, _name.Text.Trim() ),
		SceneTreeFilter.Fields.Component => _componentType is null ? null : new SceneTreeFilter( Field, _negate, Type: _componentType ),
		SceneTreeFilter.Fields.Tag => string.IsNullOrWhiteSpace( _tagValue ) ? null : new SceneTreeFilter( Field, _negate, _tagValue ),
		_ => new SceneTreeFilter( Field, !_flag.Value ),
	};

	/// <summary>
	/// Clear the new row after adding, ready for the next condition. The field stays as it was.
	/// </summary>
	public void Reset()
	{
		_negate = false;
		_componentType = null;
		_tagValue = null;
		_flag.Value = true;
		_name.Text = string.Empty;
		OnFieldChanged();

		if ( Field == SceneTreeFilter.Fields.Name )
			_name.Focus();
	}

	void Apply()
	{
		UpdateAction();

		if ( _isNew || _loading )
			return;

		if ( Build() is { } filter )
			Edited?.Invoke( filter );
	}

	void Submit()
	{
		if ( !_isNew )
		{
			Submitted?.Invoke( null );
			return;
		}

		if ( Build() is { } filter )
			Submitted?.Invoke( filter );
	}

	void OnFieldChanged()
	{
		var field = Field;

		_name.Visible = field == SceneTreeFilter.Fields.Name;
		_component.Visible = field == SceneTreeFilter.Fields.Component;
		_tag.Visible = field == SceneTreeFilter.Fields.Tag;
		_flag.Visible = SceneTreeFilter.IsFlag( field );

		// A flag's checkbox already says "is" or "is not".
		_operator.Visible = !SceneTreeFilter.IsFlag( field );

		UpdateOperator();
		UpdateComponent();
		UpdateTag();
		UpdateFlag();
		UpdateAction();
	}

	void UpdateOperator() => _operator.Text = _negate ? "!=" : "=";

	void UpdateComponent()
	{
		_component.Text = _componentType?.Title ?? "Select component…";
		_component.Icon = _componentType?.Icon ?? "extension";
	}

	void UpdateTag()
	{
		_tag.Text = _tagValue ?? "Select tag…";
		_tag.Icon = "sell";
	}

	void UpdateFlag()
	{
		var title = SceneTreeFilter.Title( Field );
		_flag.Text = _flag.Value ? $"Is {title}" : $"Is not {title}";
	}

	void UpdateAction()
	{
		if ( _isNew )
			_action.Enabled = Build() is not null;
	}

	void PickComponent()
	{
		var selector = new ComponentTypeSelector( this, findExisting: true );
		selector.OnSelect += type =>
		{
			_componentType = type;
			UpdateComponent();
			Apply();
		};
		selector.OpenAt( _component.ScreenRect.BottomLeft, animateOffset: new Vector2( 0, -4 ) );
		selector.FixedWidth = MathF.Max( _component.Width, 300 );
	}

	void PickTag()
	{
		SceneTreeTagPicker.Open( this, _tag, tag =>
		{
			_tagValue = tag;
			UpdateTag();
			Apply();
		} );
	}
}

/// <summary>
/// Pick a tag for a filter: the tags used in the active scene, most common first, or a typed one.
/// </summary>
file sealed class SceneTreeTagPicker : PopupWidget
{
	readonly Action<string> _picked;
	readonly (string Tag, int Count)[] _tags;
	readonly LineEdit _search;
	readonly GridLayout _grid;

	SceneTreeTagPicker( Widget parent, Action<string> picked ) : base( parent )
	{
		_picked = picked;
		_tags = SceneEditorSession.Active?.Scene is { } scene
			? scene.GetAllObjects( false )
				.SelectMany( x => x.Tags.TryGetAll() )
				.GroupBy( x => x, StringComparer.OrdinalIgnoreCase )
				.Select( g => (g.Key, g.Count()) )
				.OrderByDescending( x => x.Item2 )
				.ThenBy( x => x.Key, StringComparer.OrdinalIgnoreCase )
				.ToArray()
			: [];

		FixedWidth = 260;
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 4;

		_search = Layout.Add( new LineEdit( this ) { PlaceholderText = "Search or type a tag…", FixedHeight = Theme.RowHeight } );
		_search.TextChanged += _ => Rebuild();
		_search.ReturnPressed += () =>
		{
			if ( !string.IsNullOrWhiteSpace( _search.Text ) )
				Pick( _search.Text.Trim() );
		};

		_grid = Layout.Add( Layout.Grid() ) as GridLayout;
		Rebuild();
	}

	public static void Open( Widget parent, Widget anchor, Action<string> picked )
	{
		var picker = new SceneTreeTagPicker( parent, picked );
		picker.OpenAt( anchor.ScreenRect.BottomLeft, animateOffset: new Vector2( 0, -4 ) );
		picker._search.Focus();
	}

	void Rebuild()
	{
		_grid.Clear( true );

		var search = _search.Text.Trim();
		var tags = _tags.Where( x => x.Tag.Contains( search, StringComparison.OrdinalIgnoreCase ) ).Take( 32 ).ToArray();

		for ( var i = 0; i < tags.Length; i++ )
		{
			var (tag, count) = tags[i];
			var button = new Button( string.Empty, this ) { MouseLeftPress = () => Pick( tag ), FixedHeight = Theme.RowHeight };
			button.OnPaintOverride = () => PaintTag( button.LocalRect, tag, count );
			_grid.AddCell( i % 2, i / 2, button );
		}

		if ( tags.Length == 0 )
			_grid.AddCell( 0, 0, new Label( _tags.Length == 0 ? "No tags in this scene. Type one and press Enter." : "No matching tag. Press Enter to use it." ) { WordWrap = true } );

		AdjustSize();
	}

	void Pick( string tag )
	{
		_picked( tag.ToLowerInvariant() );
		Close();
	}

	/// <summary>
	/// Same look as the inspector's tag popup.
	/// </summary>
	static bool PaintTag( Rect rect, string tag, int count )
	{
		var hovered = Paint.HasMouseOver;
		var color = hovered ? Theme.TextControl : Theme.TextControl.WithAlpha( 0.7f );

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		Paint.SetBrush( Theme.TextControl.WithAlpha( hovered ? 0.2f : 0.1f ) );
		Paint.ClearPen();
		Paint.DrawRect( rect.Shrink( 2 ), 3 );

		Paint.SetDefaultFont( 8 );
		Paint.SetPen( color );
		Paint.DrawText( rect.Shrink( 10, 0 ), tag.ToLower(), TextFlag.LeftCenter );

		Paint.SetDefaultFont( 7 );
		Paint.SetPen( color.WithAlphaMultiplied( 0.5f ) );
		Paint.DrawText( rect.Shrink( 10, 0 ), $"{count}", TextFlag.RightCenter );

		return true;
	}
}

/// <summary>
/// A filter shown inside the search box, with an X to remove it.
/// </summary>
file sealed class SceneTreeFilterChip : Widget
{
	readonly SceneTreeFilter _filter;
	readonly Action _remove;

	const float CloseSize = 14;

	public SceneTreeFilterChip( SceneTreeFilter filter, Action remove ) : base( null )
	{
		_filter = filter;
		_remove = remove;

		FixedHeight = Theme.RowHeight - 6;
		MouseTracking = true;
		ToolTip = $"{filter.Label}\nClick the X to remove this filter";

		Paint.SetDefaultFont( 7 );
		FixedWidth = Paint.MeasureText( filter.Label ).x + CloseSize + 16;
	}

	Rect CloseRect => new( Width - CloseSize - 4, (Height - CloseSize) / 2, CloseSize, CloseSize );

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		Cursor = CloseRect.IsInside( e.LocalPosition ) ? CursorShape.Finger : CursorShape.Arrow;
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton && CloseRect.IsInside( e.LocalPosition ) )
		{
			e.Accepted = true;
			_remove();
		}
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		var tint = _filter.Negate ? Theme.Red : Theme.Primary;

		Paint.ClearPen();
		Paint.SetBrush( tint.WithAlpha( 0.25f ) );
		Paint.DrawRect( LocalRect, Height * 0.5f );

		Paint.SetDefaultFont( 7 );
		Paint.SetPen( Theme.Text );
		Paint.DrawText( LocalRect.Shrink( 8, 0, CloseSize + 6, 0 ), _filter.Label, TextFlag.LeftCenter | TextFlag.SingleLine );

		var hovered = IsUnderMouse && CloseRect.IsInside( FromScreen( Application.CursorPosition ) );
		Paint.SetPen( Theme.Text.WithAlpha( hovered ? 1.0f : 0.6f ) );
		Paint.DrawIcon( CloseRect, "close", 12, TextFlag.Center );
	}
}

partial class SceneTreeWidget
{
	readonly List<SceneTreeFilter> _filters = new();
	bool _includeDisabled = true;
	Layout _chips;

	/// <summary>
	/// The advanced filter's conditions. Every one must match, along with the search text.
	/// </summary>
	public IReadOnlyList<SceneTreeFilter> Filters => _filters;

	/// <summary>
	/// Raised when a filter is added, removed or cleared.
	/// </summary>
	public event Action FiltersChanged;

	/// <summary>
	/// Whether filtered results list disabled objects, or ones under a disabled parent.
	/// </summary>
	public bool IncludeDisabled
	{
		get => _includeDisabled;
		set
		{
			if ( _includeDisabled == value )
				return;

			_includeDisabled = value;
			queryDirty = true;
		}
	}

	public void AddFilter( SceneTreeFilter filter )
	{
		if ( _filters.Contains( filter ) )
			return;

		_filters.Add( filter );
		OnFiltersChanged();
	}

	/// <summary>
	/// Swap a filter for its edited version, keeping its place. An edit that duplicates another
	/// filter just removes the edited one.
	/// </summary>
	public void ReplaceFilter( SceneTreeFilter filter, SceneTreeFilter edited )
	{
		var index = _filters.IndexOf( filter );
		if ( index < 0 || filter == edited )
			return;

		if ( _filters.Contains( edited ) )
			_filters.RemoveAt( index );
		else
			_filters[index] = edited;

		OnFiltersChanged();
	}

	public void RemoveFilter( SceneTreeFilter filter )
	{
		if ( _filters.Remove( filter ) )
			OnFiltersChanged();
	}

	public void ClearFilters()
	{
		if ( _filters.Count == 0 )
			return;

		_filters.Clear();
		OnFiltersChanged();
	}

	void OnFiltersChanged()
	{
		RebuildChips();
		queryDirty = true;
		FiltersChanged?.Invoke();
	}

	void RebuildChips()
	{
		if ( _chips is null )
			return;

		_chips.Clear( true );
		foreach ( var filter in _filters )
			_chips.Add( new SceneTreeFilterChip( filter, () => RemoveFilter( filter ) ) );
	}

	void OpenFilterPopup( Widget anchor ) => SceneTreeFilterPopup.Open( this, anchor );

	/// <summary>
	/// Whether an object passes the advanced filter, on top of the name search.
	/// </summary>
	bool PassesFilters( GameObject go )
	{
		if ( !_includeDisabled && !go.Active )
			return false;

		foreach ( var filter in _filters )
		{
			if ( !filter.Matches( go ) )
				return false;
		}

		return true;
	}
}
