namespace Editor;

/// <summary>
/// Resources stored as strings
/// </summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( TextureImagePathAttribute )] )]
public class TextureImageControlWidget : ControlWidget
{
	public override bool IsControlButton => true;
	public override bool SupportsMultiEdit => true;

	public string WarningText { get; set; }

	public TextureImageControlWidget( SerializedProperty property ) : base( property )
	{
		Cursor = CursorShape.Finger;
		MouseTracking = true;
		AcceptDrops = true;
		IsDraggable = true;

		OnValueChanged();
	}

	protected override void PaintControl()
	{
		var resource = SerializedProperty.GetValue<string>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource ) : null;

		var rect = new Rect( 0, Size );

		var iconRect = rect.Shrink( 2 );
		iconRect.Width = iconRect.Height;

		rect.Left = iconRect.Right + 10;

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.2f ) );
		Paint.DrawRect( iconRect, 2 );

		var pickerName = "Texture Image";

		if ( !string.IsNullOrEmpty( WarningText ) )
		{
			Rect warningRect = iconRect;
			warningRect.Left = rect.Left + 16;
			Paint.SetPen( Theme.Yellow );
			Paint.DrawIcon( warningRect, "warning", Math.Max( 16, warningRect.Height / 2 ) );
			rect.Left += 16;
		}

		Pixmap icon = AssetType.ImageFile.Icon64;

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			var textRect = rect.Shrink( 0, 3 );
			if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawText( textRect, $"Multiple Values", TextFlag.LeftCenter );
		}
		else if ( asset is not null && !asset.IsDeleted )
		{
			Paint.Draw( iconRect, asset.GetAssetThumb( true ) );

			var textRect = rect.Shrink( 0, 3 );

			Paint.SetPen( Theme.Text.WithAlpha( 0.9f ) );
			Paint.SetHeadingFont( 8, 450 );
			var t = Paint.DrawText( textRect, $"{asset.Name}", TextFlag.LeftTop );

			textRect.Left = t.Right + 6;
			Paint.SetDefaultFont( 7 );
			Theme.DrawFilename( textRect, asset.RelativePath, TextFlag.LeftCenter, Theme.Text.WithAlpha( 0.5f ) );
		}
		else if ( !string.IsNullOrWhiteSpace( resource ) )
		{
			var textRect = rect.Shrink( 0, 3 );

			bool isPackage = !resource.Contains( ".vmap" ) && Package.TryParseIdentParts( resource, out _ );
			if ( !isPackage )
			{
				Paint.SetBrush( Theme.Red.Darken( 0.8f ) );
				Paint.DrawRect( iconRect, 2 );

				Paint.SetPen( Theme.Red );
				Paint.DrawIcon( iconRect, "error", Math.Max( 16, iconRect.Height / 2 ) );
			}
			else if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetPen( Theme.Text.WithAlpha( 0.9f ) );
			Paint.SetHeadingFont( 8, 450 );
			var t = Paint.DrawText( textRect, isPackage ? (Package.TryGetCached( resource, out Package package ) ? $"{package.Title} ☁️" : $"Cloud {pickerName}") : $"Missing {pickerName}", TextFlag.LeftTop );

			textRect.Left = t.Right + 6;
			Paint.SetDefaultFont( 7 );
			Theme.DrawFilename( textRect, resource, TextFlag.LeftCenter, Theme.Text.WithAlpha( 0.5f ) );
		}
		else
		{
			var textRect = rect.Shrink( 0, 3 );
			if ( icon != null ) Paint.Draw( iconRect, icon );

			Paint.SetDefaultFont( italic: true );
			Paint.SetPen( Theme.Text.WithAlpha( 0.2f ) );
			Paint.DrawText( textRect, $"{pickerName}", TextFlag.LeftCenter );
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var m = new ContextMenu();

		var resource = SerializedProperty.GetValue<string>( null );
		var asset = (resource != null) ? AssetSystem.FindByPath( resource ) : null;

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
		OpenPicker();
	}

	public void OpenPicker()
	{
		var resource = SerializedProperty.GetValue<string>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource ) : null;

		var options = new AssetPicker.PickerOptions
		{
			AdditionalTypes = [AssetType.Texture, AssetType.ImageFile]
		};

		var picker = AssetPicker.Create( this, AssetType.Texture, options );
		picker.SetSelection( resource );
		picker.Title = $"Select Image File";
		picker.OnAssetHighlighted = ( o ) => UpdateFromAsset( o.FirstOrDefault() );
		picker.OnAssetPicked = ( o ) => UpdateFromAsset( o.FirstOrDefault() );
		picker.Show();

		picker.SetSelection( asset );
	}

	private void UpdateFromAsset( Asset asset )
	{
		if ( asset is null ) return;

		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( asset.RelativePath );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}

	public override void OnDragHover( DragEvent ev )
	{
		if ( !ev.Data.HasFileOrFolder )
			return;

		var asset = AssetSystem.FindByPath( ev.Data.FileOrFolder );

		if ( asset == null )
			return;

		if ( asset.AssetType != AssetType.ImageFile && asset.AssetType != AssetType.Texture )
			return;

		ev.Action = DropAction.Link;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		if ( !ev.Data.HasFileOrFolder )
			return;

		var asset = AssetSystem.FindByPath( ev.Data.FileOrFolder );

		if ( asset == null )
			return;

		if ( asset.AssetType != AssetType.ImageFile && asset.AssetType != AssetType.Texture )
			return;

		UpdateFromAsset( asset );
		ev.Action = DropAction.Link;
	}

	protected override void OnDragStart()
	{
		var resource = SerializedProperty.GetValue<string>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource ) : null;

		if ( asset == null )
			return;

		var drag = new Drag( this );
		drag.Data.Url = new Uri( $"file://{asset.AbsolutePath}" );
		drag.Execute();
	}

	void Copy()
	{
		var resource = SerializedProperty.GetValue<string>( null );
		if ( resource == null ) return;

		var asset = AssetSystem.FindByPath( resource );
		if ( asset != null )
			resource = asset.RelativePath;

		EditorUtility.Clipboard.Copy( resource );
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
