namespace Editor;

/// <summary>
/// This is a callback control widget, which is used to edit classes.
/// It shows key properties, and an edit button. On clicking the edit button
/// it'll show a propery sheet popup.
/// </summary>
public class GenericControlWidget : ControlObjectWidget
{
	public override bool SupportsMultiEdit => true;

	public override bool IsWideMode => _isInlineEditor;
	public override bool IncludeLabel => !_isInlineEditor;

	bool _isInlineEditor;
	RealTimeSince _visibilityDebounce = 0;
	Widget PlaceholderWidget;
	Dictionary<SerializedProperty, ControlWidget> KeyPropertyWidgets;

	public GenericControlWidget( SerializedProperty property ) : base( property, true )
	{
		PaintBackground = false;

		if ( property.TryGetAttribute<InlineEditorAttribute>( out var inlineEditor ) )
		{
			_isInlineEditor = true;
			BuildInlineEditor( inlineEditor );
			return;
		}

		Layout = Layout.Row();
		Layout.Spacing = 2;
		KeyPropertyWidgets = new();

		var keys = SerializedObject?.Where( x => x.HasAttribute<KeyPropertyAttribute>() ).ToArray();
		if ( keys is not null && keys.Length > 0 )
		{
			foreach ( var key in keys )
			{
				var widget = Layout.Add( ControlSheetRow.CreateEditor( key ) );
				widget.Visible = key.ShouldShow();
				KeyPropertyWidgets.Add( key, widget );
			}

			// Only show the full edit button if we have editable properties that aren't showing on the front bar.
			var propertiesThatArentKeys = SerializedObject?.Where( x => x.IsEditable && x.ShouldShow() && !x.IsMethod && !x.HasAttribute<KeyPropertyAttribute>() ).ToArray();

			if ( propertiesThatArentKeys.Length > 0 )
			{
				var row = Layout.AddColumn();
				var popupButton = new IconButton( "edit_note" );
				popupButton.OnClick = OpenPopup;
				popupButton.ToolTip = $"Edit";
				popupButton.Background = Theme.ControlBackground;
				popupButton.Foreground = Theme.Text;
				popupButton.IconSize = 16;

				row.Add( popupButton );
				row.AddStretchCell( 1 );
			}
		}
		else
		{
			PlaceholderWidget = new Widget( this );
			PlaceholderWidget.Size = Theme.RowHeight;
			PlaceholderWidget.VerticalSizeMode = SizeMode.CanGrow;
			PlaceholderWidget.HorizontalSizeMode = SizeMode.Flexible;
			PlaceholderWidget.OnPaintOverride = PaintPlaceholder;
			PlaceholderWidget.MouseClick = OpenPopup;
			PlaceholderWidget.Cursor = CursorShape.Finger;
			PlaceholderWidget.ToolTip = $"Edit";
			Layout.Add( PlaceholderWidget, 1 );
		}
	}

	StickyPopup Popup;

	public override void OnDestroyed()
	{
		Popup?.Destroy();
		Popup = null;

		base.OnDestroyed();
	}

	protected void OpenPopup()
	{
		if ( Popup.IsValid() )
		{
			Popup?.Destroy();
			Popup = null;
			return;
		}

		var obj = SerializedObject;

		// if it's nullable, create for the actual value rather than the nullable container
		if ( SerializedProperty.IsNullable )
		{
			// best way to do this?
			obj = SerializedProperty.GetValue<object>().GetSerialized();
			obj.ParentProperty = SerializedProperty;
		}

		if ( obj is null )
		{
			Log.Error( "Cannot create ControlSheet for a null object" );
			return;
		}

		//
		// Create a popup for the control sheet editor
		//
		{
			var popup = new StickyPopup( null )
			{
				Owner = this,
				MinimumWidth = Width,
				Position = ScreenRect.BottomLeft
			};

			var editor = EditorUtility.OpenControlSheet( obj, this, false );

			popup.Layout.Add( editor );
			popup.OnPaintOverride = PaintPopupBackground;
			popup.Visible = true;
			popup.Focus( true );

			Popup = popup;

			//
			// Clear any unrelated popups
			//
			Popup.DestroyUnrelatedPopups();
		}
	}

	bool PaintPopupBackground()
	{
		Paint.ClearPen();
		Paint.SetBrushLinear( 0, Vector2.Down * 256, Theme.SurfaceBackground.Lighten( 0.2f ).WithAlpha( 0.98f ), Theme.SurfaceBackground.WithAlpha( 0.95f ) );
		Paint.DrawRect( Paint.LocalRect );

		Paint.ClearBrush();
		Paint.SetPen( Color.Black.WithAlpha( 0.33f ), 2, PenStyle.Solid );
		Paint.DrawRect( Paint.LocalRect.Shrink( 0, -10, 1, 1 ), 4 );

		return true;
	}

	bool PaintPlaceholder()
	{
		var type = SerializedProperty.IsNullable ? SerializedProperty.NullableType : SerializedProperty.PropertyType;

		var value = SerializedProperty.GetValue<object>();

		type = value?.GetType() ?? type;
		var displayInfo = DisplayInfo.ForType( type );
		string labelText = displayInfo.Name;

		// If the type has a custom ToString(), use that instead of the type name.
		if ( type.GetMethod( "ToString" ).DeclaringType != typeof( object ) )
		{
			labelText = value?.ToString() ?? labelText;
		}

		Theme.DrawDropdown( LocalRect, labelText, displayInfo.Icon ?? "edit_note", Popup.IsValid(), IsControlDisabled );
		return true;
	}

	/// <summary>
	/// The property has [InlineEditor], so we want to unfold this and show it inline.
	/// </summary>
	void BuildInlineEditor( InlineEditorAttribute attribute )
	{
		Layout = Layout.Column();

		if ( attribute.Label && !SerializedProperty.Parent.GetType().IsAssignableTo( typeof( SerializedCollection ) ) )
		{
			Layout.Add( ControlSheet.CreateLabel( SerializedProperty ) );
		}

		var cs = new ControlSheet();
		cs.AddObject( SerializedObject );
		cs.Margin = 0;

		Layout.Add( cs );
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		if ( !_isInlineEditor ) return;

		e.Accepted = true;

		var property = SerializedProperty;

		var menu = new ContextMenu( this );
		menu.AddOption( $"Copy {property.DisplayName}", "content_copy", () =>
		{
			var str = ToClipboardString();
			EditorUtility.Clipboard.Copy( str );
		} );

		menu.AddOption( $"Paste as {property.DisplayName}", "content_paste", () =>
		{
			var str = EditorUtility.Clipboard.Paste();
			FromClipboardString( str );
		} );

		menu.AddOption( "Reset to Default", "restart_alt", () =>
		{
			property.Parent.NoteStartEdit( property );
			property.SetValue( property.GetDefault() );
			property.Parent.NoteFinishEdit( property );
		} );

		menu.OpenAt( e.ScreenPosition, false );
	}

	[EditorEvent.Frame]
	public void UpdateVisibility()
	{
		if ( KeyPropertyWidgets is null )
			return;
		if ( _visibilityDebounce < 0.2f )
			return;

		_visibilityDebounce = Random.Shared.Float( 0, 0.1f );

		foreach ( var kvp in KeyPropertyWidgets )
		{
			kvp.Value.Visible = kvp.Key.ShouldShow();
		}
	}
}
