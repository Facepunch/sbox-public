using Sandbox;
using Sandbox.UI;

namespace Editor.TerrainEditor;

public class TerrainMaterialList : ListView
{
	Terrain Terrain;
	Drag dragData;
	int dragOverIndex = -1;
	public TerrainMaterialList( Widget parent, Terrain terrain ) : base( parent )
	{
		ItemContextMenu = ShowItemContext;
		ItemSelected = OnItemClicked;
		ItemActivated = OnItemDoubleClicked;
		ItemSpacing = 4;
		AcceptDrops = true;
		SetSizeMode( SizeMode.Expand, SizeMode.Flexible );

		ItemSize = new Vector2( 64, ThumbHeight + 4 + LineHeight * 3 + 2 );
		ItemAlign = Sandbox.UI.Align.FlexStart;
		Margin = 8;

		Terrain = terrain;
		Terrain.OnTerrainModified += OnTerrainModified;

		BuildItems();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		if ( Terrain.IsValid() )
			Terrain.OnTerrainModified -= OnTerrainModified;
	}

	void OnTerrainModified( Terrain.SyncFlags flags, RectInt region )
	{
		if ( flags.HasFlag( Terrain.SyncFlags.Control ) )
			_coverageDirty = true;
	}

	protected override bool OnDragItem( VirtualWidget item )
	{
		if ( item.Object is not TerrainMaterial material ) return false;

		dragData = new Drag( this );
		dragData.Data.Object = material;
		dragData.Execute();
		return true;
	}

	protected void OnItemClicked( object value )
	{
		if ( value is not TerrainMaterial material )
			return;

		PaintTextureTool.SplatChannel = Terrain.Storage.Materials.IndexOf( material );
	}

	protected void OnItemDoubleClicked( object obj )
	{
		if ( obj is not TerrainMaterial entry ) return;
		var asset = AssetSystem.FindByPath( entry.ResourcePath );
		asset?.OpenInEditor();
	}

	protected override DropAction OnItemDrag( ItemDragEvent e )
	{
		dragOverIndex = -1;

		if ( e.IsDrop && e.Data.Object is TerrainMaterial draggedMaterial && e.Item.Object is TerrainMaterial targetMaterial )
		{
			var materials = Terrain.Storage.Materials;
			var oldIndex = materials.IndexOf( draggedMaterial );
			var newIndex = materials.IndexOf( targetMaterial );

			if ( oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex )
			{
				(materials[oldIndex], materials[newIndex]) = (materials[newIndex], materials[oldIndex]);
				Terrain.UpdateMaterialsBuffer();
				BuildItems();
			}
			Update();
			return DropAction.Move;
		}

		if ( !e.IsDrop && e.Item.Object is TerrainMaterial hoverMaterial )
		{
			dragOverIndex = Terrain.Storage.Materials.IndexOf( hoverMaterial );
			Update();
			return DropAction.Move;
		}

		if ( e.Data.Assets != null )
		{
			foreach ( var dragAsset in e.Data.Assets )
			{
				if ( dragAsset.AssetPath?.EndsWith( ".tmat" ) ?? false )
					return DropAction.Link;
			}
		}

		return base.OnItemDrag( e );
	}

	public override void OnDragHover( DragEvent ev )
	{
		base.OnDragHover( ev );

		if ( ev.Data.Object is not TerrainMaterial )
		{
			dragOverIndex = -1;
			foreach ( var dragAsset in ev.Data.Assets )
			{
				if ( dragAsset.AssetPath?.EndsWith( ".tmat" ) ?? false )
					continue;

				ev.Action = DropAction.Link;
				break;
			}
		}
	}

	public override void OnDragDrop( DragEvent ev )
	{
		base.OnDragDrop( ev );

		dragOverIndex = -1;

		if ( ev.Data.Object is not TerrainMaterial )
		{
			AddMaterials( ev.Data.Assets );
		}

		Update();
	}

	public override void OnDragLeave()
	{
		base.OnDragLeave();
		dragOverIndex = -1;
		Update();
	}

	private async void AddMaterials( IEnumerable<DragAssetData> draggedAssets )
	{
		foreach ( var dragAsset in draggedAssets )
		{
			var asset = await dragAsset.GetAssetAsync();
			if ( asset is null ) continue;
			if ( asset.TryLoadResource<TerrainMaterial>( out var material ) )
			{
				if ( Terrain.Storage.Materials.Contains( material ) ) continue;
				Terrain.Storage.Materials.Add( material );
				Terrain.UpdateMaterialsBuffer();
			}
		}

		BuildItems();
	}

	private void ShowItemContext( object obj )
	{
		if ( obj is not TerrainMaterial entry ) return;

		var m = new ContextMenu( this );
		m.AddOption( "Open In Editor", "edit", () =>
		{
			var asset = AssetSystem.FindByPath( entry.ResourcePath );
			asset?.OpenInEditor();
		} );

		m.AddOption( "Replace With...", "swap_horiz", () =>
		{
			var picker = AssetPicker.Create( null, AssetType.FromExtension( "tmat" ) );
			picker.OnAssetPicked = x =>
			{
				var replacement = x.FirstOrDefault()?.LoadResource<TerrainMaterial>();
				if ( replacement is not null ) ReplaceMaterial( entry, replacement );
			};
			picker.Show();
		} );

		m.AddOption( "Remove", "delete", () =>
		{
			Terrain.Storage.Materials.Remove( entry );
			Terrain.UpdateMaterialsBuffer();
			BuildItems();
		} );

		m.OpenAtCursor();
	}

	/// <summary>
	/// Everything painted with <paramref name="from"/> becomes <paramref name="to"/>. A new material takes over the
	/// slot; one already in the list gets the slot's texels, leaving the old slot unused. Undoable.
	/// </summary>
	void ReplaceMaterial( TerrainMaterial from, TerrainMaterial to )
	{
		var storage = Terrain.Storage;
		var slot = storage.Materials.IndexOf( from );
		if ( slot < 0 || from == to ) return;

		var materialsBefore = storage.Materials.ToList();
		var controlBefore = storage.ControlMap.ToArray();

		var target = storage.Materials.IndexOf( to );
		if ( target < 0 )
		{
			storage.Materials[slot] = to;
		}
		else
		{
			var control = storage.ControlMap;
			for ( int i = 0; i < control.Length; i++ )
			{
				var m = new CompactTerrainMaterial( control[i] );
				if ( m.BaseTextureId != slot && m.OverlayTextureId != slot ) continue;
				if ( m.BaseTextureId == slot ) m.BaseTextureId = (byte)target;
				if ( m.OverlayTextureId == slot ) m.OverlayTextureId = (byte)target;
				control[i] = m.Packed;
			}
		}

		var materialsAfter = storage.Materials.ToList();
		var controlAfter = storage.ControlMap.ToArray();
		Apply( materialsAfter, controlAfter );

		SceneEditorSession.Active?.UndoSystem.Insert( $"Replace Terrain Material {from.ResourceName}",
			() => Apply( materialsBefore, controlBefore ),
			() => Apply( materialsAfter, controlAfter ) );
	}

	void Apply( List<TerrainMaterial> materials, uint[] control )
	{
		if ( !Terrain.IsValid() || Terrain.Storage is null ) return;

		Terrain.Storage.Materials = materials.ToList();
		control.CopyTo( Terrain.Storage.ControlMap, 0 );
		Terrain.UpdateMaterialsBuffer();
		Terrain.ApplyStorageChanges( Terrain.SyncFlags.Control, new RectInt( 0, 0, Terrain.Storage.Resolution, Terrain.Storage.Resolution ) );
		BuildItems();
	}

	public enum ViewMode
	{
		[Title( "Slot Order" )] Slot,
		[Title( "Most Used" )] MostUsed,
		[Title( "Least Used" )] LeastUsed,
		[Title( "Largest VRAM" )] LargestVram,
		[Title( "Unused Only" )] UnusedOnly,
	}

	/// <summary>
	/// How the list is ordered or filtered. Only changes what's shown: slots, painting and drag reordering use the
	/// material list itself.
	/// </summary>
	public ViewMode View
	{
		get;
		set { field = value; BuildItems(); }
	}

	public void BuildItems()
	{
		var materials = Terrain.Storage.Materials;
		var coverage = GetCoverage();
		double Used( TerrainMaterial m ) => coverage is not null && materials.IndexOf( m ) is var i and >= 0 && i < coverage.Length ? coverage[i] : 0;

		IEnumerable<TerrainMaterial> items = View switch
		{
			ViewMode.MostUsed => materials.OrderByDescending( Used ),
			ViewMode.LeastUsed => materials.OrderBy( Used ),
			ViewMode.LargestVram => materials.OrderByDescending( GetVram ),
			ViewMode.UnusedOnly => materials.Where( m => Used( m ) <= 0 ),
			_ => materials,
		};

		SetItems( items.Cast<object>() );

		if ( Summary.IsValid() )
			Summary.Text = $"{materials.Count} materials, VRAM {materials.Sum( GetVram ) / (1024.0 * 1024.0):0} MB";
	}

	/// <summary>
	/// Shows the material count and their total VRAM, refreshed whenever the list is rebuilt.
	/// </summary>
	public Label Summary
	{
		get;
		set { field = value; BuildItems(); }
	}

	/// <summary>
	/// Rows shown before the list starts scrolling.
	/// </summary>
	const int MaxVisibleRows = 4;

	/// <summary>
	/// Grow with the items up to <see cref="MaxVisibleRows"/>, then scroll.
	/// </summary>
	protected override void LayoutScrollbar()
	{
		var perRow = Math.Max( 1, ((CanvasRect.Width + ItemSpacing.x) / (ItemSize.x + ItemSpacing.x)).FloorToInt() );
		var rows = Math.Clamp( (Items.Count() + perRow - 1) / perRow, 1, MaxVisibleRows );
		var height = MathF.Ceiling( rows * ItemSize.y + (rows - 1) * ItemSpacing.y + (Height - CanvasRect.Height) );

		// Only on change: a new height relayouts the widget, which lands back here
		if ( MathF.Abs( FixedHeight - height ) > 0.5f )
			FixedHeight = height;

		base.LayoutScrollbar();
	}

	double[] _coverage;
	uint[] _coverageSource;
	bool _coverageDirty;
	RealTimeSince _coverageAge;

	/// <summary>
	/// Fraction of the terrain each material slot covers, from the control map: the base gets 1 - blend,
	/// the overlay gets blend, holes count for nothing. Recomputed at most once a second while painting.
	/// </summary>
	double[] GetCoverage()
	{
		var storage = Terrain.Storage;
		if ( storage is null ) return null;
		// Painting raises OnTerrainModified; resampling or loading replaces the array
		var stale = _coverageDirty || _coverageSource != storage.ControlMap;
		if ( _coverage is not null && (!stale || _coverageAge < 1) ) return _coverage;

		var weights = new double[32];
		long texels = 0;
		foreach ( var packed in storage.ControlMap )
		{
			var m = new CompactTerrainMaterial( packed );
			if ( m.IsHole ) continue;
			texels++;
			var blend = m.BlendFactor / 255.0;
			weights[m.BaseTextureId] += 1 - blend;
			weights[m.OverlayTextureId] += blend;
		}

		for ( int i = 0; i < weights.Length; i++ )
			weights[i] = texels > 0 ? weights[i] / texels : 0;

		_coverage = weights;
		_coverageSource = storage.ControlMap;
		_coverageDirty = false;
		_coverageAge = 0;
		return _coverage;
	}

	readonly Dictionary<TerrainMaterial, (Texture Bcr, Texture Nho, long Bytes)> _vram = [];

	/// <summary>
	/// GPU memory of the material's two generated textures at full resolution (streaming can hold less).
	/// Cached until either texture is replaced, e.g. by a recompile.
	/// </summary>
	long GetVram( TerrainMaterial material )
	{
		if ( _vram.TryGetValue( material, out var cached ) && cached.Bcr == material.BCRTexture && cached.Nho == material.NHOTexture )
			return cached.Bytes;

		static long Size( Texture texture ) => TextureResidencyInfo.Get( texture ) is { } info ? Math.Max( info.Disk.MemorySize, info.Loaded.MemorySize ) : 0;

		var bytes = Size( material.BCRTexture ) + Size( material.NHOTexture );
		_vram[material] = (material.BCRTexture, material.NHOTexture, bytes);
		return bytes;
	}

	readonly Dictionary<TerrainMaterial, (Texture Source, Pixmap Swatch)> _swatches = [];
	const int SwatchSize = 64;

	/// <summary>
	/// The material's base color as a flat square, read back from a small mip of its BCR texture. Works for package
	/// materials too, which have no local asset thumbnail. Null until the GPU readback lands.
	/// </summary>
	Pixmap GetSwatch( TerrainMaterial material )
	{
		var texture = material.BCRTexture;
		if ( texture is null ) return null;
		if ( _swatches.TryGetValue( material, out var cached ) && cached.Source == texture ) return cached.Swatch;

		_swatches[material] = (texture, null);

		var mip = Math.Clamp( (int)MathF.Log2( Math.Max( texture.Width, texture.Height ) / (float)SwatchSize ), 0, texture.Mips - 1 );
		var width = Math.Max( 1, texture.Width >> mip );
		var height = Math.Max( 1, texture.Height >> mip );

		texture.GetPixelsAsync( pixels =>
		{
			// Alpha holds roughness, not opacity
			var bytes = new byte[pixels.Length * 4];
			for ( int i = 0; i < pixels.Length; i++ )
			{
				bytes[i * 4 + 0] = pixels[i].r;
				bytes[i * 4 + 1] = pixels[i].g;
				bytes[i * 4 + 2] = pixels[i].b;
				bytes[i * 4 + 3] = 255;
			}

			MainThread.Queue( () =>
			{
				if ( !IsValid || !_swatches.TryGetValue( material, out var entry ) || entry.Source != texture ) return;

				var swatch = new Pixmap( width, height );
				swatch.UpdateFromPixels( bytes, width, height, ImageFormat.RGBA8888 );
				_swatches[material] = (texture, swatch);
				Update();
			} );
		}, mip );

		return null;
	}

	// Tile layout, top to bottom: square swatch, coverage bar, name, coverage, VRAM
	const float ThumbHeight = 64;
	const float LineHeight = 12;

	protected override void PaintItem( VirtualWidget item )
	{
		if ( item.Object is not TerrainMaterial material )
			return;

		var r = item.Rect;
		var side = MathF.Min( r.Width - 4, ThumbHeight - 4 );
		var thumb = new Rect( r.Left + (r.Width - side) * 0.5f, r.Top + 2, side, side );
		var bar = new Rect( r.Left + 2, r.Top + ThumbHeight, r.Width - 4, 3 );
		Rect Line( int i ) => new( r.Left, bar.Bottom + 1 + i * LineHeight, r.Width, LineHeight );

		if ( item.Selected || Paint.HasMouseOver )
		{
			Paint.SetBrush( Theme.Blue.WithAlpha( item.Selected ? 0.5f : 0.2f ) );
			Paint.ClearPen();
			Paint.DrawRect( r, 4 );
		}

		if ( dragOverIndex == Terrain.Storage.Materials.IndexOf( material ) )
		{
			Paint.SetPen( Theme.TextHighlight, 3f );
			Paint.DrawRect( r.Shrink( 1 ), 4 );
		}

		if ( GetSwatch( material ) is { } swatch )
		{
			Paint.Draw( thumb, swatch );
		}
		else
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.Lighten( 0.3f ) );
			Paint.DrawRect( thumb, 2 );
		}

		Paint.SetDefaultFont( 7 );
		Paint.SetPen( Theme.Text );
		Paint.DrawText( Line( 0 ), material.ResourceName, TextFlag.Center );

		var vram = GetVram( material );
		Paint.SetPen( Theme.TextLight );
		Paint.DrawText( Line( 2 ), vram > 0 ? $"{vram / (1024.0 * 1024.0):0.#} MB" : "? MB", TextFlag.Center );

		var index = Terrain.Storage.Materials.IndexOf( material );
		var coverage = GetCoverage();
		if ( coverage is null || index < 0 || index >= coverage.Length ) return;

		var fraction = coverage[index];
		Paint.ClearPen();
		Paint.SetBrush( Color.Black.WithAlpha( 0.6f ) );
		Paint.DrawRect( bar );
		Paint.SetBrush( fraction <= 0 ? Theme.Red : Theme.Green );
		// Relative to the most used material, with a sliver for anything used at all
		var largest = coverage.Max();
		var filled = largest > 0 ? Math.Max( fraction / largest, fraction > 0 ? 0.03 : 0 ) : 0;
		Paint.DrawRect( bar with { Width = bar.Width * (float)filled } );

		// Terrain units are inches
		var sizeMetres = Terrain.Storage.TerrainSize * 0.0254;
		var km2 = fraction * sizeMetres * sizeMetres / 1e6;
		var text = fraction <= 0 ? "unused" : fraction < 0.001 ? $"{km2 * 1e6:N0} m²" : $"{fraction * 100:0.#}% {km2:0.#}km²";
		Paint.SetPen( fraction <= 0 ? Theme.Red : Theme.TextLight );
		Paint.DrawText( Line( 1 ), text, TextFlag.Center );
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 4 );

		base.OnPaint();
	}
}
