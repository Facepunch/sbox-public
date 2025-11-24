namespace Editor;

[CustomEditor( ForInterface = true )]
public class InterfaceControlWidget : ControlWidget
{
	bool IsInList => Parent?.Parent is ListControlWidget;
	IconButton PickerButton;

	//
	// Interfaces are assumed to be Component references or GameResource references
	//
	public InterfaceControlWidget( SerializedProperty property ) : base( property )
	{
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
		Cursor = CursorShape.Finger;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( ReadOnly )
		{
			return;
		}

		var menu = new ContextMenu( this );
		menu.AddOption( "Select Asset", "dvr", action: PickAsset );
		menu.AddOption( $"Clear", "clear", action: Clear );

		menu.OpenAtCursor();

		e.Accepted = true;
	}

	void PickAsset()
	{
		var resource = SerializedProperty.GetValue<Resource>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource.ResourcePath ) : null;

		var assetType = AssetType.FromType( resource.IsValid() ? resource.GetType() : SerializedProperty.PropertyType );

		PropertyStartEdit();

		var picker = AssetPicker.Create( null, assetType, new AssetPicker.PickerOptions()
		{
			EnableMultiselect = IsInList
		} );

		picker.Title = $"Select {SerializedProperty.DisplayName}";
		picker.OnAssetHighlighted = ( o ) => UpdateFromAsset( o.FirstOrDefault() );
		picker.OnAssetPicked = ( o ) =>
		{
			UpdateFromAssets( o );
			PropertyFinishEdit();
		};
		picker.Show();

		picker.SetSelection( asset );
	}

	private void UpdateFromAsset( Asset asset )
	{
		if ( asset is null )
			return;

		Resource resource;

		if ( SerializedProperty.PropertyType.IsInterface )
		{
			resource = asset.LoadResource<GameResource>();
		}
		else
		{
			resource = asset.LoadResource( SerializedProperty.PropertyType );
		}

		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( resource );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}

	private void UpdateFromAssets( Asset[] assets )
	{
		if ( assets.Length == 0 ) return;

		UpdateFromAsset( assets[0] );

		if ( IsInList )
		{
			var list = Parent.Parent as ListControlWidget;
			for ( int i = 1; i < assets.Length; i++ )
			{
				var newResource = assets[i].LoadResource( SerializedProperty.PropertyType );
				list.Collection.Add( newResource );
			}
		}
	}


	void Clear()
	{
		PropertyStartEdit();
		SerializedProperty.SetValue<object>( null );
		PropertyFinishEdit();
	}

	private GameResource GetGameResource( DragData data )
	{
		if ( !data.HasFileOrFolder )
		{
			return null;
		}

		var asset = AssetSystem.FindByPath( data.FileOrFolder );
		if ( !asset.TryLoadResource<GameResource>( out var resource ) )
		{
			return null;
		}

		if ( !resource.GetType().IsAssignableTo( SerializedProperty.PropertyType ) )
		{
			return null;
		}

		return resource;
	}

	private object GetMatching( DragData data )
	{
		var type = SerializedProperty.PropertyType;

		//
		// Look for a resource
		//

		if ( GetGameResource( data ) is { } resource )
		{
			return resource;
		}

		var firstComponent = data.OfType( type ).Cast<Component>().FirstOrDefault()
			?? data.OfType<GameObject>()
				.Select( x => x.Components.Get( type, FindMode.EverythingInSelf ) )
				.OfType<Component>()
				.FirstOrDefault();

		//
		// Did we find a component?
		//
		return firstComponent;
	}

	protected override void PaintControl()
	{
		var rect = new Rect( 0, Size );
		rect = rect.Shrink( 6, 0 );

		var iconRect = rect.Shrink( 2 );
		iconRect.Left -= 4;
		iconRect.Width = iconRect.Height;

		var obj = SerializedProperty.GetValue<object>();
		var type = EditorTypeLibrary.GetType( SerializedProperty.PropertyType );

		Paint.SetDefaultFont();

		if ( obj is GameResource resource )
		{
			rect.Left += 22;

			var bitmap = resource.GetAssetTypeIcon( iconRect.Width.CeilToInt(), iconRect.Height.CeilToInt() );

			Paint.Draw( iconRect, Pixmap.FromBitmap( bitmap ) );

			Paint.SetPen( Theme.Green );
			Paint.DrawText( rect, $"{resource.ToString()}", TextFlag.LeftCenter );

			//
			// Little icon at the bottom right of the icon to show it's an interface
			// 
			{
				var interfaceIconRect = new Rect( iconRect.Center - new Vector2( 2, 2 ), new Vector2( 14, 14 ) );

				Paint.SetPen( Theme.ControlBackground );
				Paint.DrawCircle( interfaceIconRect );

				Paint.SetPen( Theme.Green );
				Paint.DrawIcon( interfaceIconRect, "data_object", 10, TextFlag.Center );
			}
		}
		else if ( obj is Component component )
		{
			Paint.SetPen( Theme.Green );
			Paint.DrawIcon( rect, "data_object", 14, TextFlag.LeftCenter );
			rect.Left += 22;
			Paint.DrawText( rect, $"{component.GetType().Name} on ({component.GameObject?.Name ?? "null"})", TextFlag.LeftCenter );
		}
		else
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.3f ) );
			Paint.DrawIcon( rect, "data_object", 14, TextFlag.LeftCenter );
			rect.Left += 22;
			Paint.DrawText( rect, $"None ({type?.Name})", TextFlag.LeftCenter );
		}

		PickerButton.Visible = IsControlHovered;
	}

	public override void OnDragHover( DragEvent ev )
	{
		ev.Action = GetMatching( ev.Data ) is not null ? DropAction.Link : DropAction.Ignore;
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
