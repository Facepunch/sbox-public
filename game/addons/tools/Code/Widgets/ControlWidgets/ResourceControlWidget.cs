namespace Editor;

[CustomEditor( typeof( Resource ) )]
public class ResourceControlWidget : ControlWidget
{
	AssetType AssetType;

	IconButton PreviewButton;

	public override bool IsControlButton => !IsControlDisabled;
	public override bool SupportsMultiEdit => true;

	bool IsInList => Parent?.Parent is ListControlWidget;

	public ResourceControlWidget( SerializedProperty property ) : base( property )
	{
		AssetType = AssetType.FromType( property.PropertyType );

		HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		Cursor = CursorShape.Finger;
		MouseTracking = true;
		AcceptDrops = true;
		IsDraggable = true;

		if ( AssetType == AssetType.SoundFile )
		{
			PreviewButton = new PreviewButton( property )
			{
				Background = Theme.Blue,
				Foreground = Color.White,
				Icon = "volume_up",
				Parent = this,
				ToolTip = "Play Sound",
				Visible = false,
				OnClick = () => EditorUtility.PlayAssetSound( property.GetValue<SoundFile>() )
			};
		}
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		if ( PreviewButton.IsValid() )
		{
			PreviewButton.FixedSize = Height - 2;
			PreviewButton.Position = new Vector2( Width - Height + 1, 1 );
		}
	}

	protected override void PaintControl()
	{
		var resource = SerializedProperty.GetValue<Resource>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource.ResourcePath ) : null;

		var rect = new Rect( 0, Size );

		var iconRect = rect.Shrink( 2 );
		iconRect.Width = iconRect.Height;

		rect.Left = iconRect.Right + 10;

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.2f ) * Tint );
		Paint.DrawRect( iconRect, 2 );

		var pickerName = DisplayInfo.ForType( SerializedProperty.PropertyType ).Name;
		if ( AssetType is not null ) pickerName = AssetType.FriendlyName;

		Pixmap icon = AssetType?.Icon64;
		var alpha = IsControlDisabled ? 0.6f : 1f;

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			var textRect = rect.Shrink( 0, 3 );
			if ( icon != null ) Paint.Draw( iconRect, icon, alpha );

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.MultipleValues.WithAlpha( alpha ) );
			Paint.DrawText( textRect, $"Multiple Values", TextFlag.LeftCenter );
		}
		else if ( asset is not null && !asset.IsDeleted )
		{
			Paint.Draw( iconRect, asset.GetAssetThumb( true ), alpha );

			DrawContent( rect, asset.Name, asset.RelativePath );
		}
		else if ( resource != null && !string.IsNullOrEmpty( resource.ResourcePath ) )
		{
			Paint.SetBrush( Theme.Red.Darken( 0.8f ).WithAlpha( alpha ) );
			Paint.DrawRect( iconRect, 2 );

			Paint.SetPen( Theme.Red.WithAlpha( alpha ) );
			Paint.DrawIcon( iconRect, "error", Math.Max( 16, iconRect.Height / 2 ) );

			DrawContent( rect, $"Missing {pickerName}", resource.ResourcePath );
		}
		else
		{
			var textRect = rect.Shrink( 0, 3 );
			if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.Text.WithAlpha( 0.2f * alpha ) * Tint );
			Paint.DrawText( textRect, $"{pickerName}", TextFlag.LeftCenter );
		}

		if ( PreviewButton.IsValid() )
		{
			PreviewButton.Visible = IsControlHovered;
		}
	}

	private void DrawContent( Rect rect, string title, string path )
	{
		bool multiline = Height > 32;
		Rect textRect = rect.Shrink( 0, 6 );
		var alpha = IsControlDisabled ? 0.6f : 1f;

		if ( multiline )
		{
			textRect = new Rect( textRect.TopLeft, new Vector2( textRect.Width, textRect.Height / 2 ) );
		}

		Paint.SetPen( Theme.Text.WithAlpha( 0.9f * alpha ) * Tint );
		Paint.SetHeadingFont( 8, 450 );
		var t = Paint.DrawText( textRect, title, multiline ? TextFlag.LeftCenter : TextFlag.LeftCenter );

		if ( multiline )
		{
			textRect.Position += new Vector2( 0, textRect.Height );
		}
		else
		{
			textRect.Left = t.Right + 6;
		}

		Paint.SetDefaultFont( 7 );
		Theme.DrawFilename( textRect, path, multiline ? TextFlag.LeftCenter : TextFlag.LeftBottom, Color.White.WithAlpha( 0.5f * alpha ) * Tint );
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var m = new ContextMenu();

		var resource = SerializedProperty.GetValue<Resource>( null );
		var asset = (resource != null) ? AssetSystem.FindByPath( resource.ResourcePath ) : null;

		m.AddOption( "Open in Editor", "edit", () => asset?.OpenInEditor() ).Enabled = asset != null && !asset.IsProcedural;
		m.AddOption( "Find in Asset Browser", "search", () => LocalAssetBrowser.OpenTo( asset, true ) ).Enabled = asset is not null;
		m.AddSeparator();
		m.AddOption( "Copy", "file_copy", action: Copy ).Enabled = asset != null;
		m.AddOption( "Paste", "content_paste", action: Paste );
		m.AddSeparator();
		m.AddOption( "Clear", "backspace", action: Clear ).Enabled = resource != null;

		m.OpenAtCursor( false );
		e.Accepted = true;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( ReadOnly ) return;

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
		if ( asset is null ) return;
		if ( !CanAssignAssetType( asset ) ) return;

		Resource resource;
		if ( SerializedProperty.PropertyType.IsInterface )
		{
			// For interface types, use the asset directly since we've already verified
			// it implements our interface in CanAssignAssetType
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

		if ( CanAssignAssetType( assets[0] ) )
		{
			var resource = assets[0].LoadResource( SerializedProperty.PropertyType );
			SerializedProperty.SetValue( resource );
		}

		if ( IsInList )
		{
			var list = Parent.Parent as ListControlWidget;
			for ( int i = 1; i < assets.Length; i++ )
			{
				if ( !CanAssignAssetType( assets[i] ) ) continue;
				var newResource = assets[i].LoadResource( SerializedProperty.PropertyType );
				list.Collection.Add( newResource );
			}
		}
	}

	public override void OnDragHover( DragEvent ev )
	{
		// This might be a cloud asset
		if ( ev.Data.Url?.Scheme == "https" )
		{
			ev.Action = DropAction.Link;
			return;
		}

		if ( !ev.Data.HasFileOrFolder )
			return;

		var asset = AssetSystem.FindByPath( ev.Data.FileOrFolder );

		if ( !CanAssignAssetType( asset ) )
			return;

		ev.Action = DropAction.Link;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		if ( ev.Data.Url?.Scheme == "https" )
		{
			PropertyStartEdit();
			_ = DroppedUrl( ev.Data.Text );
			PropertyFinishEdit();
			ev.Action = DropAction.Link;
			return;
		}

		if ( !ev.Data.HasFileOrFolder )
			return;

		var asset = AssetSystem.FindByPath( ev.Data.FileOrFolder );

		if ( !CanAssignAssetType( asset ) )
			return;

		PropertyStartEdit();
		UpdateFromAsset( asset );
		PropertyFinishEdit();
		ev.Action = DropAction.Link;
	}

	private bool CanAssignAssetType( Asset asset )
	{
		if ( asset is null )
		{
			return false;
		}

		if ( AssetType == null )
		{
			return true;
		}

		// Handle interface types
		if ( SerializedProperty.PropertyType.IsInterface )
		{
			if ( !asset.AssetType.IsGameResource )
				return false;

			// Quick type check from asset
			return asset.AssetType.ResourceType.IsAssignableTo( SerializedProperty.PropertyType );
		}

		if ( AssetType != asset.AssetType )
		{
			if ( !AssetType.IsGameResource )
				return false;

			if ( !asset.AssetType.IsGameResource )
				return false;

			if ( !asset.AssetType.ResourceType.IsAssignableTo( SerializedProperty.PropertyType ) )
				return false;
		}

		var scene = SerializedProperty.GetContainingGameObject()?.Scene;

		// Check that we aren't changing this prefab to the same prefab we're already in (recursive nightmare)
		if ( SerializedProperty.PropertyType == typeof( PrefabFile ) && scene is PrefabScene prefabScene )
		{
			var prefab = asset.LoadResource<PrefabFile>();
			var scenePrefab = prefabScene.ToPrefabFile();
			if ( prefab is null ) return false;
			if ( scenePrefab == prefab ) return false;
		}

		return true;
	}

	async Task DroppedUrl( string identUrl )
	{
		var asset = await AssetSystem.InstallAsync( identUrl );

		if ( !CanAssignAssetType( asset ) )
			return;

		UpdateFromAsset( asset );
	}

	protected override void OnDragStart()
	{
		var resource = SerializedProperty.GetValue<Resource>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource.ResourcePath ) : null;

		if ( asset == null )
			return;

		var drag = new Drag( this );
		drag.Data.Url = new System.Uri( $"file://{asset.AbsolutePath}" );
		drag.Execute();
	}

	void Copy()
	{
		var resource = SerializedProperty.GetValue<Resource>( null );
		if ( resource == null ) return;

		var asset = AssetSystem.FindByPath( resource.ResourcePath );
		if ( asset == null ) return;

		EditorUtility.Clipboard.Copy( asset.Path );
	}

	void Paste()
	{
		var path = EditorUtility.Clipboard.Paste();
		var asset = AssetSystem.FindByPath( path );
		UpdateFromAsset( asset );
	}

	void Clear()
	{
		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( (Resource)null );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}
}

file class PreviewButton : IconButton
{
	private SerializedProperty property;

	public PreviewButton( SerializedProperty property ) : base( "people" )
	{
		this.property = property;
	}
}
