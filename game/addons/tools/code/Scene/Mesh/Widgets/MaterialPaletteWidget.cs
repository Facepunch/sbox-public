namespace Editor.MeshEditor;

public class MaterialPaletteWidget : Widget
{
	const int MaxCells = 12;
	const int PaletteColumns = 6;

	readonly List<Material> _recentMaterials = new();
	readonly PaletteMaterialSlotWidget[] _slots;

	readonly List<string> _paletteNames = new();
	string _paletteId = "Default";

	public event Action<Material> MaterialClicked;
	public Func<Material> GetActiveMaterial { get; set; }

	public string PaletteId
	{
		get => _paletteId;
		set
		{
			if ( string.IsNullOrEmpty( value ) || _paletteId == value )
				return;

			_paletteId = value;

			SaveActivePalette();

			LoadPaletteFromCookie();
			Update();
		}
	}

	public MaterialPaletteWidget()
	{
		Layout = Layout.Column();
		Layout.Alignment = TextFlag.Center;

		var grid = Layout.Grid();
		grid.Spacing = 2;
		Layout.Add( grid );

		_slots = new PaletteMaterialSlotWidget[MaxCells];

		for ( int i = 0; i < MaxCells; i++ )
		{
			var col = i / PaletteColumns;
			var row = i % PaletteColumns;

			var slot = new PaletteMaterialSlotWidget( this )
			{
				ShowFilename = false,
				FixedSize = 32
			};

			_slots[i] = slot;
			grid.AddCell( col, row, slot );
		}

		LoadPalettes();
		LoadPaletteFromCookie();
	}

	void LoadPalettes()
	{
		_paletteNames.Clear();

		string rawNames;
		try { rawNames = ProjectCookie.Get( "MeshEditor.MaterialPalettes.Names", string.Empty ); }
		catch { rawNames = string.Empty; }

		if ( string.IsNullOrWhiteSpace( rawNames ) )
		{
			_paletteNames.Add( "Default" );
		}
		else
		{
			foreach ( var name in rawNames.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
			{
				if ( !_paletteNames.Contains( name ) )
					_paletteNames.Add( name );
			}

			if ( _paletteNames.Count == 0 )
				_paletteNames.Add( "Default" );
		}

		try { _paletteId = ProjectCookie.Get( "MeshEditor.MaterialPalettes.Active", _paletteNames[0] ); }
		catch { _paletteId = _paletteNames[0]; }

		if ( !_paletteNames.Contains( _paletteId ) )
			_paletteId = _paletteNames[0];
	}

	void SavePalettes()
	{
		ProjectCookie.Set( "MeshEditor.MaterialPalettes.Names", string.Join( ";", _paletteNames ) );
		SaveActivePalette();
	}

	void SaveActivePalette()
	{
		ProjectCookie.Set( "MeshEditor.MaterialPalettes.Active", _paletteId ?? string.Empty );
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		base.OnContextMenu( e );

		var m = new ContextMenu();

		AddPaletteMenu( m );

		m.OpenAtCursor( false );
		e.Accepted = true;
	}

	internal void AddPaletteMenu( ContextMenu m )
	{
		LoadPalettes();

		var p = m.AddMenu( "Palettes", "palette" );

		foreach ( var name in _paletteNames )
		{
			var localName = name;
			var icon = (localName == _paletteId) ? "check" : "palette";
			p.AddOption( localName, icon, () => PaletteId = localName );
		}

		p.AddSeparator();

		p.AddOption( "New Palette…", "add", ShowCreatePalettePopup );
		p.AddOption( "Rename Palette…", "edit", () => ShowRenamePalettePopup( _paletteId ) ).Enabled = _paletteNames.Count > 0;
		p.AddOption( "Duplicate Palette", "content_copy", () => DuplicatePalette( _paletteId ) ).Enabled = _paletteNames.Count > 0;

		var del = p.AddOption( "Delete Palette", "delete", () => DeletePalette( _paletteId ) );
		del.Enabled = _paletteNames.Count > 1;
	}

	void ShowCreatePalettePopup()
	{
		var popup = new PopupWidget( this );
		popup.FixedWidth = 220;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 8;
		popup.Layout.Spacing = 4;

		_ = popup.Layout.Add( new Label.Small( "New palette" ) );
		var entry = popup.Layout.Add( new LineEdit( popup ) );
		entry.FixedHeight = Theme.RowHeight;
		entry.PlaceholderText = "Palette name…";

		void Commit()
		{
			var name = entry.Value?.Trim();
			if ( string.IsNullOrEmpty( name ) ) { popup.Destroy(); return; }
			if ( _paletteNames.Contains( name ) ) { popup.Destroy(); return; }

			_paletteNames.Add( name );
			_paletteId = name;

			SavePalettes();
			LoadPaletteFromCookie();

			popup.Destroy();
		}

		entry.ReturnPressed += Commit;

		popup.OpenAtCursor();
		entry.Focus();
	}

	void ShowRenamePalettePopup( string oldName )
	{
		if ( string.IsNullOrEmpty( oldName ) )
			return;

		var popup = new PopupWidget( this );
		popup.FixedWidth = 220;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 8;
		popup.Layout.Spacing = 4;

		_ = popup.Layout.Add( new Label.Small( "Rename palette" ) );
		var entry = popup.Layout.Add( new LineEdit( popup ) );
		entry.FixedHeight = Theme.RowHeight;
		entry.Value = oldName;

		void Commit()
		{
			var newName = entry.Value?.Trim();
			if ( string.IsNullOrEmpty( newName ) || newName == oldName ) { popup.Destroy(); return; }
			if ( _paletteNames.Contains( newName ) ) { popup.Destroy(); return; }

			RenamePalette( oldName, newName );
			popup.Destroy();
		}

		entry.ReturnPressed += Commit;

		popup.OpenAtCursor();
		entry.Focus();
	}

	void RenamePalette( string oldName, string newName )
	{
		var idx = _paletteNames.IndexOf( oldName );
		if ( idx < 0 ) return;

		_paletteNames[idx] = newName;

		var oldKey = $"MeshEditor.MaterialPalette.{oldName}";
		var newKey = $"MeshEditor.MaterialPalette.{newName}";

		try
		{
			var data = ProjectCookie.Get( oldKey, string.Empty );
			ProjectCookie.Set( newKey, data );
			ProjectCookie.Set( oldKey, string.Empty );
		}
		catch { }

		if ( _paletteId == oldName )
			_paletteId = newName;

		SavePalettes();
		LoadPaletteFromCookie();
	}

	void DuplicatePalette( string sourceName )
	{
		if ( string.IsNullOrEmpty( sourceName ) )
			return;

		var baseName = $"{sourceName} Copy";
		var newName = baseName;
		int counter = 2;

		while ( _paletteNames.Contains( newName ) )
			newName = $"{baseName} {counter++}";

		_paletteNames.Add( newName );

		var srcKey = $"MeshEditor.MaterialPalette.{sourceName}";
		var dstKey = $"MeshEditor.MaterialPalette.{newName}";

		try
		{
			var data = ProjectCookie.Get( srcKey, string.Empty );
			ProjectCookie.Set( dstKey, data );
		}
		catch { }

		_paletteId = newName;

		SavePalettes();
		LoadPaletteFromCookie();
	}

	void DeletePalette( string name )
	{
		if ( _paletteNames.Count <= 1 )
			return;

		var idx = _paletteNames.IndexOf( name );
		if ( idx < 0 )
			return;

		_paletteNames.RemoveAt( idx );

		var key = $"MeshEditor.MaterialPalette.{name}";
		try { ProjectCookie.Set( key, string.Empty ); }
		catch { }

		_paletteId = _paletteNames[Math.Clamp( idx - 1, 0, _paletteNames.Count - 1 )];

		SavePalettes();
		LoadPaletteFromCookie();
	}

	public void PushMaterial( Material material )
	{
		if ( material is null ) return;

		var path = material.ResourcePath;

		if ( !string.IsNullOrEmpty( path ) )
			_recentMaterials.RemoveAll( m => m is not null && m.ResourcePath == path );
		else
			_recentMaterials.RemoveAll( m => m == material );

		_recentMaterials.Insert( 0, material );

		if ( _recentMaterials.Count > _slots.Length )
			_recentMaterials.RemoveAt( _recentMaterials.Count - 1 );

		UpdateSlots();
		SavePaletteToCookie();
	}

	void UpdateSlots()
	{
		for ( int i = 0; i < _slots.Length; i++ )
			_slots[i].Material = i < _recentMaterials.Count ? _recentMaterials[i] : null;
	}

	internal void SlotClickedApply( Material material )
	{
		if ( material is null ) return;
		MaterialClicked?.Invoke( material );
	}

	private void SlotSetMaterial( PaletteMaterialSlotWidget slot, Material mat )
	{
		if ( slot is null ) return;

		var index = Array.IndexOf( _slots, slot );
		if ( index < 0 ) return;

		if ( index >= _recentMaterials.Count )
		{
			while ( _recentMaterials.Count <= index )
				_recentMaterials.Add( null );
		}

		_recentMaterials[index] = mat;
		UpdateSlots();
		SavePaletteToCookie();
	}

	private void SlotAssignFromActive( PaletteMaterialSlotWidget slot )
	{
		if ( GetActiveMaterial is null ) return;

		var mat = GetActiveMaterial();
		if ( mat is null ) return;

		SlotSetMaterial( slot, mat );
	}

	private void SlotAssignMaterial( PaletteMaterialSlotWidget slot )
	{
		var picker = AssetPicker.Create( null, AssetType.Material, new AssetPicker.PickerOptions()
		{
			EnableMultiselect = false
		} );

		picker.Title = "Select Palette Material";

		picker.OnAssetPicked = assets =>
		{
			var asset = assets.FirstOrDefault();
			if ( asset is null ) return;

			var mat = asset.LoadResource( typeof( Material ) ) as Material;
			if ( mat is null ) return;

			SlotSetMaterial( slot, mat );
		};

		picker.Show();
	}

	private void SlotClear( PaletteMaterialSlotWidget slot ) => SlotSetMaterial( slot, null );

	void SavePaletteToCookie()
	{
		if ( _recentMaterials.Count < _slots.Length )
		{
			while ( _recentMaterials.Count < _slots.Length )
				_recentMaterials.Add( null );
		}

		var parts = _recentMaterials
			.Take( _slots.Length )
			.Select( m => m is not null ? m.ResourcePath ?? string.Empty : string.Empty );

		ProjectCookie.Set( $"MeshEditor.MaterialPalette.{_paletteId}", string.Join( ";", parts ) );
	}

	void LoadPaletteFromCookie()
	{
		string data;
		try { data = ProjectCookie.Get( $"MeshEditor.MaterialPalette.{_paletteId}", string.Empty ); }
		catch { data = string.Empty; }

		_recentMaterials.Clear();

		if ( string.IsNullOrEmpty( data ) )
		{
			UpdateSlots();
			return;
		}

		var parts = data.Split( ';' );

		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( i >= parts.Length || string.IsNullOrWhiteSpace( parts[i] ) )
			{
				_recentMaterials.Add( null );
				continue;
			}

			var path = parts[i].Trim();
			var asset = AssetSystem.FindByPath( path );
			if ( asset is null || asset.IsDeleted )
			{
				_recentMaterials.Add( null );
				continue;
			}

			_recentMaterials.Add( asset.LoadResource( typeof( Material ) ) as Material );
		}

		UpdateSlots();
	}

	class PaletteMaterialSlotWidget : MaterialWidget
	{
		readonly MaterialPaletteWidget _strip;

		public PaletteMaterialSlotWidget( MaterialPaletteWidget strip ) : base( null )
		{
			_strip = strip;
			ToolTip = "";
		}

		protected override void OnMaterialDropped( Material material )
		{
			_strip.SlotSetMaterial( this, material );
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			base.OnMouseClick( e );

			if ( Material.IsValid() ) _strip.SlotClickedApply( Material );
			else _strip.SlotAssignFromActive( this );
		}

		protected override void OnContextMenu( ContextMenuEvent e )
		{
			var m = new ContextMenu();
			bool hasMaterial = Material.IsValid();

			var text = hasMaterial ? "Change Material" : "Set Material";
			m.AddOption( text, "format_color_fill", () => _strip.SlotAssignMaterial( this ) );

			m.AddSeparator();

			if ( Material.IsValid() )
			{
				var asset = AssetSystem.FindByPath( Material.ResourcePath );
				if ( asset.AbsolutePath != string.Empty )
				{
					m.AddOption( "Open in Editor", "edit", () => asset?.OpenInEditor() ).Enabled = asset != null && !asset.IsProcedural;
					m.AddOption( "Find in Asset Browser", "search", () => LocalAssetBrowser.OpenTo( asset, true ) ).Enabled = asset is not null;
					m.AddSeparator();
				}
			}

			_strip.AddPaletteMenu( m );
			m.AddSeparator();

			m.AddOption( "Clear", "backspace", () => _strip.SlotClear( this ) ).Enabled = hasMaterial;

			m.OpenAtCursor( false );
			e.Accepted = true;
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.ClearBrush();

			var asset = Material != null ? AssetSystem.FindByPath( Material.ResourcePath ) : null;
			var icon = AssetType.Material?.Icon64;

			var controlRect = Paint.LocalRect;
			controlRect = controlRect.Shrink( 2 );

			Paint.Antialiasing = true;
			Paint.TextAntialiasing = true;

			if ( asset is not null && !asset.IsDeleted )
			{
				icon = asset.GetAssetThumb( true );
			}

			if ( icon is not null && Material.IsValid() )
			{
				Paint.Draw( LocalRect.Shrink( 2 ), icon );

				if ( Paint.HasMouseOver )
				{
					Paint.SetBrushAndPen( Color.Transparent, Color.White );
					Paint.DrawRect( controlRect, 0 );
				}
			}
			else
			{
				var baseFill = Theme.Text.WithAlpha( 0.01f );
				var baseLine = Theme.Text.WithAlpha( 0.1f );
				var iconColor = Theme.Text.WithAlpha( 0.1f );

				if ( Paint.HasMouseOver )
				{
					baseFill = Theme.Text.WithAlpha( 0.04f );
					baseLine = Theme.Text.WithAlpha( 0.2f );
					iconColor = Theme.Text.WithAlpha( 0.2f );
				}
				else
				{
					baseFill = Theme.Text.WithAlpha( 0.01f );
					baseLine = Theme.Text.WithAlpha( 0.1f );
					iconColor = Theme.Text.WithAlpha( 0.1f );
				}

				if ( _isValidDropHover )
				{
					baseFill = Theme.Green.WithAlpha( 0.05f );
					baseLine = Theme.Green.WithAlpha( 0.8f );
					iconColor = Theme.Green;
				}

				Paint.SetBrushAndPen( baseFill, baseLine, style: _isValidDropHover ? PenStyle.Solid : PenStyle.Dot );
				Paint.DrawRect( controlRect, 2 );

				var iconName = _isDownloading ? "download" : "add";

				Paint.SetPen( iconColor );
				Paint.DrawIcon( LocalRect.Shrink( 2 ), iconName, 16 );
			}
		}

		Widget tt;

		protected override void OnMouseEnter()
		{
			base.OnMouseEnter();

			var material = Material;
			var asset = material != null ? AssetSystem.FindByPath( material.ResourcePath ) : null;
			var icon = AssetType.Material?.Icon64;

			if ( !this.tt.IsValid() && asset is not null && !asset.IsDeleted )
			{
				var tt = new TextureTooltip( this, ScreenRect with { Size = 128 } );
				icon = asset.GetAssetThumb( true );
				tt.SetTexture( icon, asset );
				tt.Show();

				this.tt = tt;
			}
		}

		protected override void OnMouseLeave()
		{
			base.OnMouseLeave();

			tt?.Destroy();
		}
	}
}
file class TextureTooltip : Widget
{
	Widget target;
	int frames;

	Pixmap Texture;
	Asset _asset;
	public TextureTooltip( Widget parent, Rect screenRect ) : base( null )
	{
		WindowFlags = WindowFlags.ToolTip | WindowFlags.FramelessWindowHint | WindowFlags.WindowDoesNotAcceptFocus;
		FocusMode = FocusMode.None;
		TransparentForMouseEvents = true;
		ShowWithoutActivating = true;
		NoSystemBackground = true;
		Position = Editor.Application.CursorPosition - new Vector2( Size.x + 10, 0 );
		Size = screenRect.Size;
		target = parent;
	}

	public void SetTexture( Pixmap texture, Asset asset )
	{
		Texture = texture;
		_asset = asset;

		if ( texture is null )
		{
			Size = new Vector2( 128, 128 );
			return;
		}

		Size = texture.Size;

		if ( Size.x < 128 || Size.y < 128 )
		{
			Size = new Vector2( 128, 128 );
		}

		if ( Size.x > 512 ) Size *= 512 / Size.x;
		if ( Size.y > 512 ) Size *= 512 / Size.y;
	}

	[EditorEvent.Frame]
	public void FrameUpdate()
	{
		this.Place( target, WidgetAnchor.BottomStart with { Offset = 5 } );

		if ( Application.HoveredWidget != target && frames > 2 )
			Destroy();

		frames++;
	}

	protected override void OnPaint()
	{
		if ( Texture is null ) return;

		Paint.ClearBrush();
		Paint.ClearPen();

		Paint.SetBrushAndPen( Theme.ControlBackground, Theme.Border );
		Paint.DrawRect( LocalRect );

		var content = ContentRect.Shrink( 16 );
		content.Top -= 6;
		content.Bottom -= 6;
		Paint.Draw( content, Texture );

		Paint.SetDefaultFont( 7, 500 );
		Theme.DrawFilename( LocalRect.Shrink( 4 ), _asset.RelativePath, TextFlag.LeftBottom, Color.White );
	}
}
