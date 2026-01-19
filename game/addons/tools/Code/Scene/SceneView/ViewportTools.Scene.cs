namespace Editor;

partial class ViewportTools
{
	private void BuildToolbarScene( Layout layout )
	{
		var so = EditorScene.GizmoSettings.GetSerialized();

		{
			var group = layout.Add( AddGroup() );

			AddToggleButton(
				group.Layout,
				"Global Space",
				() => "public",
				() => EditorScene.GizmoSettings.GlobalSpace,
				( v ) => EditorScene.GizmoSettings.GlobalSpace = v
			);
		}

		{
			var group = layout.Add( AddGroup() );

			AddToggleButton(
				group.Layout,
				"Draw Gizmos",
				() => "touch_app",
				() => EditorScene.GizmoSettings.GizmosEnabled,
				( v ) => EditorScene.GizmoSettings.GizmosEnabled = v
			);

			var b = AddButton(
				group.Layout,
				"Gizmo Settings...",
				"arrow_drop_down",
				() => OpenGizmosMenu()
			);

			b.FixedWidth = Theme.RowHeight * 0.5f;
		}

		{
			var group = layout.Add( AddGroup() );

			AddToggleButton(
				group.Layout,
				"Angle Snap",
				() => "rotate_90_degrees_cw",
				() => EditorScene.GizmoSettings.SnapToAngles,
				( v ) => EditorScene.GizmoSettings.SnapToAngles = v
			);

			{
				var angleStep = new AngleStepWidget( so.GetProperty( nameof( EditorScene.GizmoSettings.AngleSpacing ) ) );
				angleStep.ToolTip = "Angle Step";
				angleStep.FixedWidth = 65;
				group.Layout.Add( angleStep );
			}
		}

		{
			var group = layout.Add( AddGroup() );

			AddToggleButton(
				group.Layout,
				"Grid Snap",
				() => "grid_on",
				() => EditorScene.GizmoSettings.SnapToGrid,
				( v ) => EditorScene.GizmoSettings.SnapToGrid = v
			);

			{
				var snapStep = new SnapStepWidget( so.GetProperty( nameof( EditorScene.GizmoSettings.GridSpacing ) ) )
				{
					Min = 0.125f,
					Max = 128.0f
				};
				snapStep.ToolTip = "Grid Step";
				snapStep.FixedWidth = 65;
				group.Layout.Add( snapStep );
			}
		}
	}

	private void OpenGizmosMenu()
	{
		var menu = new Menu();
		menu.ContentMargins = 0;

		var groups = EditorTypeLibrary.GetTypes()
			.Where( x => x.TargetType.IsAssignableTo( typeof( Component ) ) )
			.Where( x => x.GetAttribute<EditorHandleAttribute>() != null )
			.OrderBy( x => x.Title )
			.GroupBy( x => x.Group ?? "Other" )
			.OrderBy( x => x.Key )
			.ToList();

		var scrollArea = menu.AddWidget( new ScrollArea( menu )
		{
			FixedHeight = 500f,
			ContentMargins = 0
		} );

		scrollArea.Canvas = new Widget();
		scrollArea.Canvas.Layout = Layout.Column();
		scrollArea.Canvas.Layout.Alignment = TextFlag.Top;
		scrollArea.Canvas.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground );
			Paint.DrawRect( scrollArea.Canvas.LocalRect );
			return true;
		};

		var canvas = scrollArea.Canvas;

		for ( int i = 0; i < groups.Count; i++ )
		{
			var isLast = i == groups.Count - 1;
			canvas.Layout.Add( new GizmoCategoryWidget( groups[i].Key, groups[i].ToList() ) );

			if ( !isLast )
				canvas.Layout.AddSpacingCell( 8.0f );
		}

		menu.OpenAtCursor();
	}
}

/// <summary>
/// A category of gizmos with a header checkbox that toggles all items
/// </summary>
file class GizmoCategoryWidget : Widget
{
	public Checkbox CategoryCheckbox { get; }
	public List<GizmoRowWidget> Rows { get; } = new();

	public GizmoCategoryWidget( string title, List<TypeDescription> types )
	{
		Layout = Layout.Column();

		//
		// Header row
		//
		var headerRow = Layout.Add( new Widget
		{
			FixedHeight = Theme.RowHeight,
			Layout = Layout.Row()
		} );

		headerRow.Layout.Spacing = 4;
		headerRow.Layout.Margin = new Sandbox.UI.Margin( 8, 0 );

		var label = headerRow.Layout.Add( new Label( title ) );
		label.SetStyles( "font-weight: bold;" );

		headerRow.Layout.AddStretchCell();

		CategoryCheckbox = headerRow.Layout.Add( new Checkbox() );
		CategoryCheckbox.Clicked = OnCategoryToggled;

		headerRow.Layout.AddSpacingCell( 4.0f );

		Layout.AddSeparator( true );

		//
		// Gizmo rows
		//
		foreach ( var type in types )
		{
			var row = Layout.Add( new GizmoRowWidget( type ) );
			row.CheckboxClicked = UpdateCategoryCheckbox;
			Rows.Add( row );
		}

		UpdateCategoryCheckbox();
	}

	private void OnCategoryToggled()
	{
		var allEnabled = Rows.All( r => r.IsEnabled );
		var enable = !allEnabled;

		foreach ( var row in Rows )
		{
			row.SetEnabled( enable );
		}

		CategoryCheckbox.State = enable ? CheckState.On : CheckState.Off;
	}

	private void UpdateCategoryCheckbox()
	{
		var enabledCount = Rows.Count( r => r.IsEnabled );

		if ( enabledCount == 0 )
			CategoryCheckbox.State = CheckState.Off;
		else if ( enabledCount == Rows.Count )
			CategoryCheckbox.State = CheckState.On;
		else
			CategoryCheckbox.State = CheckState.Partial;
	}
}

/// <summary>
/// A single gizmo row with icon and checkbox
/// </summary>
file class GizmoRowWidget : Widget
{
	public TypeDescription Type { get; }
	public Checkbox Checkbox { get; }
	public Action CheckboxClicked { get; set; }

	public bool IsEnabled => EditorScene.GizmoSettings.IsGizmoEnabled( Type.TargetType );

	public GizmoRowWidget( TypeDescription type )
	{
		Type = type;

		FixedWidth = 250f;
		FixedHeight = Theme.RowHeight;
		Layout = Layout.Row();
		Layout.Spacing = 4;
		Layout.Margin = new Sandbox.UI.Margin( 8, 0 );

		Layout.Add( new Label( type.Title ) );
		Layout.AddStretchCell();

		var icon = Layout.Add( new GizmoIconWidget( type ) );

		Layout.AddSpacingCell( 4.0f );

		Checkbox = Layout.Add( new Checkbox
		{
			State = IsEnabled ? CheckState.On : CheckState.Off
		} );

		Checkbox.Clicked = OnCheckboxClicked;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Paint.HasMouseOver ? Theme.ControlBackground : Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );
	}

	private void OnCheckboxClicked()
	{
		EditorScene.GizmoSettings.SetGizmoEnabled( Type.TargetType, Checkbox.State == CheckState.On );
		CheckboxClicked?.Invoke();
	}

	public void SetEnabled( bool enabled )
	{
		EditorScene.GizmoSettings.SetGizmoEnabled( Type.TargetType, enabled );
		Checkbox.State = enabled ? CheckState.On : CheckState.Off;
	}
}

/// <summary>
/// Displays the gizmo's icon (texture or material icon)
/// </summary>
file class GizmoIconWidget : Widget
{
	private readonly TypeDescription type;

	public GizmoIconWidget( TypeDescription type )
	{
		this.type = type;
		FixedWidth = 16.0f;
		FixedHeight = 16.0f;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearBrush();
		Paint.ClearPen();

		var handleAttr = type.GetAttribute<EditorHandleAttribute>();

		if ( handleAttr.Texture is not null )
		{
			var texture = Texture.Load( handleAttr.Texture );
			var pixmap = Pixmap.FromTexture( texture );
			pixmap = pixmap.Resize( LocalRect.Size );
			Paint.Draw( LocalRect, pixmap );
		}
		else if ( handleAttr.Icon is not null )
		{
			Paint.SetPen( Theme.Text );
			Paint.DrawIcon( LocalRect, handleAttr.Icon, 16.0f );
		}
	}
}

file class AngleStepWidget : SnapStepWidget
{
	private float[] values =
	{
		0.25f,
		0.5f,
		1f,
		5f,
		15f,
		30f,
		45f,
		90f,
		180f
	};

	public AngleStepWidget( SerializedProperty property ) : base( property, "º" )
	{

	}

	public override void Decrease()
	{
		var value = SerializedProperty.GetValue<float>();

		var index = Array.IndexOf( values, values.OrderBy( a => MathF.Abs( value - a ) ).First() );
		if ( index > 0 ) index--;

		LineEdit.Blur();
		SerializedProperty.SetValue( values[index] );
	}

	public override void Increase()
	{
		var value = SerializedProperty.GetValue<float>();

		var index = Array.IndexOf( values, values.OrderBy( a => MathF.Abs( value - a ) ).First() );
		if ( index != values.Count() - 1 ) index++;

		LineEdit.Blur();
		SerializedProperty.SetValue( values[index] );
	}
}

/// <summary>
/// Pretty Spinbox-like widget for headerbar value picking
/// </summary>
class SnapStepWidget : ControlWidget
{
	protected LineEdit LineEdit;

	public float Min { get; set; } = 0.25f;
	public float Max { get; set; } = 128f;

	public SnapStepWidget( SerializedProperty property, string suffix = null ) : base( property )
	{
		Layout = Layout.Row();
		LineEdit = new LineEdit( this );
		LineEdit.TextEdited += ( text ) => property.SetValue<object>( float.TryParse( text, out float v ) ? v : text );
		LineEdit.MinimumSize = Theme.RowHeight;
		LineEdit.MaximumSize = new Vector2( 4096, Size.y );
		LineEdit.ReadOnly = ReadOnly;
		LineEdit.SetStyles( "background-color: transparent; vertical-align: middle; text-align: left;" );
		Layout.Add( LineEdit );

		if ( suffix != null )
		{
			var label = new Label( this );
			label.Text = suffix;
			label.SetStyles( "background-color: transparent; vertical-align: middle; text-align: right;" );
			Layout.Add( label );
		}

		var buttons = Layout.AddColumn();

		var bIncrease = new IconButton( "keyboard_arrow_up", Increase );
		bIncrease.Background = Color.Transparent;
		bIncrease.FixedHeight = Theme.ControlHeight / 2;
		bIncrease.FixedWidth = 20;
		buttons.Add( bIncrease );

		var bDecrease = new IconButton( "keyboard_arrow_down", Decrease );
		bDecrease.Background = Color.Transparent;
		bDecrease.FixedHeight = Theme.ControlHeight / 2;
		bDecrease.FixedWidth = 20;
		buttons.Add( bDecrease );

		LineEdit.Text = property.GetValue<float>().ToString();
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		if ( LineEdit.IsFocused )
			return;

		LineEdit.Text = SerializedProperty.GetValue<float>().ToString();

		// we put the curor at the start of the line so that
		// it keeps the front of the string in focus, since that
		// is most likely the important part
		LineEdit.CursorPosition = 0;
	}

	public virtual void Decrease()
	{
		var value = SerializedProperty.GetValue<float>();
		if ( value <= Min )
			return;

		LineEdit.Blur();
		SerializedProperty.SetValue( value / 2.0f );
	}

	public virtual void Increase()
	{
		var value = SerializedProperty.GetValue<float>();
		if ( value >= Max )
			return;

		LineEdit.Blur();
		SerializedProperty.SetValue( value * 2 );
	}
}
