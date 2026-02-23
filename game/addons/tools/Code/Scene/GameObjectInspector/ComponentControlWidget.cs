
namespace Editor;

[CustomEditor( typeof( Component ) )]
public class ComponentControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => true;
	IconButton PickerButton;

	public ComponentControlWidget( SerializedProperty property ) : base( property )
	{
		if ( !property.IsEditable )
			ReadOnly = true;

		SetSizeMode( SizeMode.Default, SizeMode.Default );

		Layout = Layout.Column();
		Layout.Spacing = 2;

		var inner = Layout.Add( Layout.Row() );
		inner.Margin = 2;

		inner.AddStretchCell( 1 );
		PickerButton = inner.Add( new IconButton( "colorize", () =>
		{
			PropertyStartEdit();
			EyeDropperTool.SetTargetProperty( property );
			EyeDropperTool.OnBackToLastTool = () => PropertyFinishEdit();
		}, this ) );
		PickerButton.FixedWidth = Theme.RowHeight - 4;
		PickerButton.FixedHeight = Theme.RowHeight - 4;
		PickerButton.Visible = false;
		PickerButton.ToolTip = $"Pick {property.DisplayName}";

		AcceptDrops = true;
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var m = new ContextMenu( this );

		var component = SerializedProperty.GetValue<Component>();
		if ( component.IsValid() && component.GameObject.IsValid() )
		{
			m.AddOption( "Find in Scene", "search", () => EditorUtility.FindInScene( component ) );
			m.AddSeparator();
		}

		if ( !ReadOnly )
		{
			m.AddOption( "Clear", action: Clear );
		}

		if ( m.OptionCount > 0 )
		{
			m.OpenAtCursor( false );
		}

		e.Accepted = true;
	}

	protected override void PaintControl()
	{
		var rect = LocalRect.Shrink( 6, 0 );
		var component = SerializedProperty.GetValue<Component>();
		var type = EditorTypeLibrary.GetType( SerializedProperty.PropertyType );

		Paint.SetDefaultFont();

		var icon = type?.Icon;
		if ( type.IsInterface ) icon = $"data_object";

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawIcon( rect, icon, 14, TextFlag.LeftCenter );
			rect.Left += 22;
			Paint.DrawText( rect, "Multiple Values", TextFlag.LeftCenter );
		}
		else if ( !component.IsValid() )
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.3f ) );
			Paint.DrawIcon( rect, icon, 14, TextFlag.LeftCenter );
			rect.Left += 22;
			Paint.DrawText( rect, $"None ({type?.Name})", TextFlag.LeftCenter );
			Cursor = CursorShape.None;
		}
		else
		{
			Paint.SetPen( Theme.Green );
			Paint.DrawIcon( rect, icon, 14, TextFlag.LeftCenter );
			rect.Left += 22;
			Paint.DrawText( rect, $"{component.GetType().Name} on ({component.GameObject?.Name ?? "null"})", TextFlag.LeftCenter );
			Cursor = CursorShape.Finger;
		}

		if ( EyeDropperTool.TargetProperty == SerializedProperty )
		{
			Paint.SetPen( ControlHighlightSecondary.WithAlpha( 0.8f ), 2, PenStyle.Dot );
			Paint.SetBrush( ControlHighlightSecondary.WithAlpha( 0.2f ) );
			Paint.DrawRect( LocalRect.Shrink( 2 ), Theme.ControlRadius );
		}

		PickerButton.Visible = IsControlHovered;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( !e.LeftMouseButton )
		{
			return;
		}

		e.Accepted = true;

		var component = SerializedProperty.GetValue<Component>();

		// Open Scene Browser Component Picker
		var picker = new Dialog( null );
		picker.Window.Title = $"Pick {SerializedProperty.PropertyType.Name}";
		picker.Size = new Vector2( 500, 700 );
		picker.Layout = Layout.Column();
		var sceneBrowser = picker.Layout.Add( new GameObjectSceneBrowser( SerializedProperty.PropertyType ) );
		var bottomRow = picker.Layout.Add( Layout.Row() );
		bottomRow.Margin = 8;
		bottomRow.Spacing = 8;
		bottomRow.AddStretchCell( 1 );
		var btnSelect = bottomRow.Add( new Button.Primary( "Select" ) );
		btnSelect.Clicked = () => picker.Close();
		var btnCancel = bottomRow.Add( new Button( "Cancel" ) );
		btnCancel.Clicked = () => picker.Close();
		btnSelect.Enabled = false;

		sceneBrowser.ConfirmButton = btnSelect;
		sceneBrowser.OnConfirm = () => picker.Close();
		sceneBrowser.SelectComponent( component );
		sceneBrowser.OnComponentSelect = ( c ) =>
		{
			PropertyStartEdit();
			SerializedProperty.SetValue( c );
			PropertyFinishEdit();
		};

		picker.Show();
	}

	void Clear()
	{
		PropertyStartEdit();
		SerializedProperty.SetValue<Component>( null );
		PropertyFinishEdit();
	}

	private Component GetMatching( DragData data )
	{
		var type = SerializedProperty.PropertyType;

		return data.OfType( type ).Cast<Component>().FirstOrDefault()
			?? data.OfType<GameObject>()
				.Select( x => x.Components.Get( type, FindMode.EverythingInSelf ) )
				.OfType<Component>()
				.FirstOrDefault();
	}

	public override void OnDragHover( DragEvent ev )
	{
		ev.Action = GetMatching( ev.Data ).IsValid() ? DropAction.Link : DropAction.Ignore;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		if ( GetMatching( ev.Data ) is { } value )
		{
			PropertyStartEdit();
			SerializedProperty.SetValue( value );
			PropertyFinishEdit();
		}
	}
}
