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

		ItemSize = new Vector2( 68, 68 + 20 );
		ItemAlign = Sandbox.UI.Align.FlexStart;
		Margin = 8;

		Terrain = terrain;

		BuildItems();
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

	protected override void PaintItem( VirtualWidget item )
	{
		var rect = item.Rect.Shrink( 0, 0, 0, 20 );

		if ( item.Object is not TerrainMaterial material )
			return;

		var asset = AssetSystem.FindByPath( material.ResourcePath );

		if ( asset is null )
		{
			Paint.SetDefaultFont();
			Paint.SetPen( Color.Red );
			Paint.DrawText( item.Rect.Shrink( 2 ), "<ERROR>", TextFlag.Center );
			return;
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

		var pixmap = asset.GetAssetThumb();
		Paint.Draw( rect.Shrink( 2 ), pixmap );

		Paint.SetDefaultFont();
		Paint.SetPen( Theme.Text );
		Paint.DrawText( item.Rect.Shrink( 2 ), material.ResourceName, TextFlag.CenterBottom );
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 4 );

		base.OnPaint();
	}
}
