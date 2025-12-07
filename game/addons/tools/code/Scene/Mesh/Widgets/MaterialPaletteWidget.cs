namespace Editor.MeshEditor;

public class MaterialPaletteWidget : Widget
{
	const int MaxRecentMaterials = 12;
	const int RecentColumns = 6;

	readonly List<Material> _recentMaterials = new();
	readonly RecentMaterialSlotWidget[] _slots;

	public event Action<Material> MaterialClicked;
	public Func<Material> GetActiveMaterial { get; set; }

	public MaterialPaletteWidget()
	{
		Layout = Layout.Column();
		Layout.Margin = 0;

		var grid = Layout.Grid();
		grid.Spacing = 2;
		Layout.Add( grid );

		_slots = new RecentMaterialSlotWidget[MaxRecentMaterials];

		for ( int i = 0; i < MaxRecentMaterials; i++ )
		{
			var col = i / RecentColumns;
			var row = i % RecentColumns;

			var slot = new RecentMaterialSlotWidget( this )
			{
				ShowFilename = false,
				FixedSize = 32
			};

			_slots[i] = slot;

			grid.AddCell( col, row, slot );
		}

		LoadPaletteFromCookie();
	}

	public void PushMaterial( Material material )
	{
		if ( material is null ) return;

		var path = material.ResourcePath;

		if ( !string.IsNullOrEmpty( path ) )
		{
			_recentMaterials.RemoveAll( m => m is not null && m.ResourcePath == path );
		}
		else
		{
			_recentMaterials.RemoveAll( m => m == material );
		}

		_recentMaterials.Insert( 0, material );

		if ( _recentMaterials.Count > MaxRecentMaterials )
		{
			_recentMaterials.RemoveAt( _recentMaterials.Count - 1 );
		}

		UpdateSlots();
		SavePaletteToCookie();
	}

	void UpdateSlots()
	{
		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( i < _recentMaterials.Count )
			{
				_slots[i].Material = _recentMaterials[i];
			}
			else
			{
				_slots[i].Material = null;
			}
		}
	}

	internal void SlotClickedApply( Material material )
	{
		if ( material is null ) return;
		MaterialClicked?.Invoke( material );
	}

	private void SlotSetMaterial( RecentMaterialSlotWidget slot, Material mat )
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

	private void SlotAssignFromActive( RecentMaterialSlotWidget slot )
	{
		if ( GetActiveMaterial is null )
			return;

		var mat = GetActiveMaterial();
		if ( mat is null )
			return;

		SlotSetMaterial( slot, mat );
	}

	private void SlotAssignMaterial( RecentMaterialSlotWidget slot )
	{
		// Open a picker just for materials and assign the result to this slot.
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

	private void SlotClear( RecentMaterialSlotWidget slot )
	{
		SlotSetMaterial( slot, null );
	}

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

		var data = string.Join( ";", parts );

		// Maybe this should be scene specific? 
		ProjectCookie.Set( "MeshEditor.MaterialPalette", data );
	}

	void LoadPaletteFromCookie()
	{
		string data;

		try
		{
			data = ProjectCookie.Get( "MeshEditor.MaterialPalette", string.Empty );
		}
		catch
		{
			data = string.Empty;
		}

		_recentMaterials.Clear();

		if ( string.IsNullOrEmpty( data ) )
		{
			UpdateSlots();
			return;
		}

		var parts = data.Split( ';' );

		for ( int i = 0; i < MaxRecentMaterials; i++ )
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

			var mat = asset.LoadResource( typeof( Material ) ) as Material;
			_recentMaterials.Add( mat );
		}

		UpdateSlots();
	}

	class RecentMaterialSlotWidget : MaterialWidget
	{
		readonly MaterialPaletteWidget _strip;
		bool _isDownloading;
		bool _isValidDropHover;
		public RecentMaterialSlotWidget( MaterialPaletteWidget strip )
		{
			_strip = strip;
			ToolTip = "";

			AcceptDrops = true;
			Cursor = CursorShape.Finger;
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			base.OnMouseClick( e );

			if ( Material is not null )
			{
				_strip.SlotClickedApply( Material );
			}
			else
			{
				_strip.SlotAssignFromActive( this );
			}
		}

		protected override void OnContextMenu( ContextMenuEvent e )
		{
			var m = new ContextMenu();
			bool hasMaterial = Material is not null;

			var text = hasMaterial ? "Change Material" : "Set Material";
			m.AddOption( text, "format_color_fill", () =>
			{
				_strip.SlotAssignMaterial( this );
			} );

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

			m.AddOption( "Clear", "backspace", () =>
			{
				_strip.SlotClear( this );
			} ).Enabled = hasMaterial;

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

		public override void OnDragLeave()
		{
			base.OnDragLeave();

			_isValidDropHover = false;
		}

		public override void OnDragHover( DragEvent ev )
		{
			if ( ev.Data.Url?.Scheme == "https" )
			{
				ev.Action = DropAction.Link;
				_isValidDropHover = true;
				return;
			}

			if ( ev.Data.HasFileOrFolder )
			{
				var assetFromPath = AssetSystem.FindByPath( ev.Data.FileOrFolder );
				if ( assetFromPath is not null && assetFromPath.AssetType == AssetType.Material )
				{
					ev.Action = DropAction.Link;
					_isValidDropHover = true;
					return;
				}
			}

			if ( ev.Data.Object is Asset asset && asset.AssetType == AssetType.Material )
			{
				ev.Action = DropAction.Link;
				_isValidDropHover = true;
				return;
			}

			if ( ev.Data.Object is Material )
			{
				ev.Action = DropAction.Link;
				_isValidDropHover = true;
			}
		}

		public override void OnDragDrop( DragEvent ev )
		{
			base.OnDragDrop( ev );

			if ( ev.Data.Url?.Scheme == "https" )
			{
				_ = AssignFromUrlAsync( ev.Data.Text );
				ev.Action = DropAction.Link;
				return;
			}

			Material droppedMaterial = null;

			if ( ev.Data.HasFileOrFolder )
			{
				var assetFromPath = AssetSystem.FindByPath( ev.Data.FileOrFolder );
				if ( assetFromPath is not null && assetFromPath.AssetType == AssetType.Material )
				{
					droppedMaterial = assetFromPath.LoadResource( typeof( Material ) ) as Material;
				}
			}
			else if ( ev.Data.Object is Asset asset && asset.AssetType == AssetType.Material )
			{
				droppedMaterial = asset.LoadResource( typeof( Material ) ) as Material;
			}
			else if ( ev.Data.Object is Material material )
			{
				droppedMaterial = material;
			}

			if ( droppedMaterial is null )
				return;

			_strip.SlotSetMaterial( this, droppedMaterial );
			ev.Action = DropAction.Link;
		}

		async Task AssignFromUrlAsync( string identUrl )
		{
			try
			{
				_isDownloading = true;
				Update();

				var asset = await AssetSystem.InstallAsync( identUrl );
				if ( asset is null || asset.AssetType != AssetType.Material )
					return;

				var mat = asset.LoadResource( typeof( Material ) ) as Material;
				if ( mat is null )
					return;

				Material = mat;

				var index = Array.IndexOf( _strip._slots, this );
				if ( index >= 0 )
				{
					if ( index >= _strip._recentMaterials.Count )
					{
						while ( _strip._recentMaterials.Count <= index )
							_strip._recentMaterials.Add( null );
					}

					_strip._recentMaterials[index] = mat;
					_strip.SavePaletteToCookie();
				}
			}
			finally
			{
				_isDownloading = false;
				_isValidDropHover = false;
				Update();
			}
		}

		protected override void OnDragStart()
		{
			if ( Material is null )
				return;

			var asset = AssetSystem.FindByPath( Material.ResourcePath );
			if ( asset == null )
				return;

			var drag = new Drag( this );
			drag.Data.Object = asset;
			drag.Data.Url = new System.Uri( $"file://{asset.AbsolutePath}" );
			drag.Execute();
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
