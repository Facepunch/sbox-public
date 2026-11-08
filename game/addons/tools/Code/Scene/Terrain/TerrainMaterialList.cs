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
		ItemSpacing = 8;
		AcceptDrops = true;
		SetSizeMode( SizeMode.Expand, SizeMode.Flexible );

		ItemSize = new Vector2( 68, 68 + 36 );
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

		m.AddOption( "Remove", "delete", () =>
		{
			Terrain.Storage.Materials.Remove( entry );
			Terrain.UpdateMaterialsBuffer();
			BuildItems();
		} );

		m.OpenAtCursor();
	}

	public void BuildItems()
	{
		SetItems( Terrain.Storage.Materials.Cast<object>() );
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

	protected override void PaintItem( VirtualWidget item )
	{
		var rect = item.Rect.Shrink( 0, 0, 0, 36 );

		if ( item.Object is not TerrainMaterial material )
			return;

		var asset = AssetSystem.FindByPath( material.ResourcePath );

		if ( asset is null )
		{
			// Not a local asset (e.g. from a package): no thumbnail, but name and coverage still matter
			Paint.SetDefaultFont();
			Paint.SetPen( Color.Red );
			Paint.DrawText( rect.Shrink( 2 ), "<ERROR>", TextFlag.Center );
		}

		if ( item.Selected || Paint.HasMouseOver )
		{
			Paint.SetBrush( Theme.Blue.WithAlpha( item.Selected ? 0.5f : 0.2f ) );
			Paint.ClearPen();
			Paint.DrawRect( item.Rect, 4 );
		}

		if ( item.Object is TerrainMaterial mat )
		{
			var itemIndex = Terrain.Storage.Materials.IndexOf( mat );
			if ( dragOverIndex == itemIndex )
			{
				Paint.SetPen( Theme.TextHighlight, 3f );
				Paint.DrawRect( item.Rect.Shrink( 1 ), 4 );
			}
		}

		if ( asset is not null )
			Paint.Draw( rect.Shrink( 2 ), asset.GetAssetThumb() );

		Paint.SetDefaultFont();
		Paint.SetPen( Theme.Text );
		Paint.DrawText( item.Rect.Shrink( 2, 0, 2, 18 ), material.ResourceName, TextFlag.CenterBottom );

		var index = Terrain.Storage.Materials.IndexOf( material );
		var coverage = GetCoverage();
		if ( coverage is null || index < 0 || index >= coverage.Length ) return;

		var fraction = coverage[index];
		var bar = new Rect( rect.Left + 2, rect.Bottom - 6, rect.Width - 4, 4 );
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
		var text = fraction <= 0 ? "unused" : fraction < 0.001 ? $"<0.1% {km2 * 1e6:N0} m²" : $"{fraction * 100:0.#}% {km2:0.##} km²";
		Paint.SetDefaultFont( 7 );
		Paint.SetPen( fraction <= 0 ? Theme.Red : Theme.TextLight );
		Paint.DrawText( item.Rect.Shrink( 0, 0, 0, 2 ), text, TextFlag.CenterBottom );
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 4 );

		base.OnPaint();
	}
}
