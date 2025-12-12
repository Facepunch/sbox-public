namespace Editor.MeshEditor;

class ActiveMaterialWidget : ControlWidget
{
	public override bool IsControlButton => !IsControlDisabled;

	readonly MaterialWidget _materialWidget = null;
	readonly MaterialPaletteWidget _paletteStrip;

	public ActiveMaterialWidget( SerializedProperty property ) : base( property )
	{
		FixedHeight = 220;

		Layout = Layout.Row();
		Layout.Margin = 8;

		ToolTip = "";

		_materialWidget = Layout.Add( new MaterialWidget() );
		_materialWidget.ToolTip = "Active Material";
		_materialWidget.FixedSize = FixedHeight - 26;
		_materialWidget.Cursor = CursorShape.Finger;

		Layout.AddStretchCell( 1 );

		_paletteStrip = Layout.Add( new MaterialPaletteWidget() );
		_paletteStrip.MaterialClicked += OnPaletteMaterialClicked;
		_paletteStrip.FixedHeight = FixedHeight - 26;
		_paletteStrip.GetActiveMaterial = () => _materialWidget.Material;

		Layout.AddStretchCell( 1 );

		Frame();
	}

	protected override void OnPaint()
	{
		// nothing
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

	private void UpdateFromAsset( Asset asset )
	{
		if ( asset is null ) return;

		var resource = asset.LoadResource( SerializedProperty.PropertyType );
		if ( resource is null ) return;

		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( resource );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		// If we are selecting the Material Widget continue. (Probably better way of doing this)
		if ( !_materialWidget.LocalRect.IsInside( e.LocalPosition ) )
			return;

		if ( ReadOnly ) return;

		var resource = SerializedProperty.GetValue<Resource>( null );
		var asset = resource != null ? AssetSystem.FindByPath( resource.ResourcePath ) : null;

		var assetType = AssetType.FromType( resource.IsValid() ? resource.GetType() : SerializedProperty.PropertyType );

		PropertyStartEdit();

		var picker = AssetPicker.Create( null, assetType, new AssetPicker.PickerOptions()
		{
			EnableMultiselect = false
		} );
		picker.Title = $"Select {SerializedProperty.DisplayName}";
		picker.OnAssetHighlighted = ( o ) => UpdateFromAsset( o.FirstOrDefault() );
		picker.OnAssetPicked = ( o ) =>
		{
			UpdateFromAsset( o.FirstOrDefault() );
			PropertyFinishEdit();
		};

		picker.Show();

		picker.SetSelection( asset );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		var resource = SerializedProperty.GetValue<Resource>( null );
		var material = resource as Material;

		_materialWidget.Material = material;
	}

	void OnPaletteMaterialClicked( Material material )
	{
		if ( ReadOnly || material is null ) return;

		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( material );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}
}
