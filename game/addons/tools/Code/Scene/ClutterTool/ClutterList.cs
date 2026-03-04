using Sandbox;
using Sandbox.Clutter;
using System;
using System.Linq;

namespace Editor;

/// <summary>
/// Grid-based list view for ClutterDefinition resources, similar to TerrainMaterialList.
/// </summary>
public class ClutterList : ListView
{
	public ClutterDefinition SelectedClutter { get; private set; }
	public Action<ClutterDefinition> OnClutterSelected { get; set; }

	public ClutterList( Widget parent ) : base( parent )
	{
		ItemSelected = OnItemClicked;
		ItemActivated = OnItemDoubleClicked;
		ItemContextMenu = ShowItemContext;
		Margin = 8;
		ItemSpacing = 4;
		MinimumHeight = 200;

		ItemSize = new Vector2( 86, 86 + 16 );
		ItemAlign = Sandbox.UI.Align.FlexStart;

		BuildItems();
	}

	protected void OnItemClicked( object value )
	{
		if ( value is null && SelectedClutter != null )
		{
			return;
		}
		if ( value is not ClutterDefinition clutter )
			return;

		SelectedClutter = clutter;
		OnClutterSelected?.Invoke( clutter );
	}

	protected void OnItemDoubleClicked( object obj )
	{
		if ( obj is not ClutterDefinition clutter )
			return;

		var asset = AssetSystem.FindByPath( clutter.ResourcePath );
		asset?.OpenInEditor();
	}

	private void ShowItemContext( object obj )
	{
		if ( obj is not ClutterDefinition clutter )
			return;

		var m = new ContextMenu( this );
		m.AddOption( "Open In Editor", "edit", () =>
		{
			var asset = AssetSystem.FindByPath( clutter.ResourcePath );
			asset?.OpenInEditor();
		} );

		m.AddOption( "Show In Asset Browser", "folder_open", () =>
		{
			var asset = AssetSystem.FindByPath( clutter.ResourcePath );
			if ( asset != null )
			{
				MainAssetBrowser.Instance?.Local.FocusOnAsset( asset );
			}
		} );

		m.OpenAtCursor();
	}

	public void BuildItems()
	{
		var clutters = ResourceLibrary.GetAll<ClutterDefinition>().ToList();
		SetItems( clutters.Cast<object>() );
		// Auto-select first item if nothing is selected
		if ( SelectedClutter == null && clutters.Count > 0 )
		{
			var firstClutter = clutters[0];
			SelectedClutter = firstClutter;
			OnClutterSelected?.Invoke( firstClutter );
		}
	}

	public void Refresh()
	{
		BuildItems();
	}

	protected override void PaintItem( VirtualWidget item )
	{
		var rect = item.Rect.Shrink( 0, 0, 0, 16 );

		if ( item.Object is not ClutterDefinition clutter )
			return;

		var asset = AssetSystem.FindByPath( clutter.ResourcePath );

		// Selection/hover highlight
		var isSelected = clutter == SelectedClutter || item.Selected;
		if ( isSelected || Paint.HasMouseOver )
		{
			Paint.SetBrush( Theme.Blue.WithAlpha( isSelected ? 0.5f : 0.2f ) );
			Paint.ClearPen();
			Paint.DrawRect( item.Rect, 4 );
		}

		// Thumbnail
		var pixmap = asset?.GetAssetThumb();
		if ( pixmap != null )
		{
			Paint.Draw( rect.Shrink( 2 ), pixmap );
		}
		else
		{
			// Fallback: draw background and icon
			Paint.SetBrush( Theme.ControlBackground );
			Paint.ClearPen();
			Paint.DrawRect( rect.Shrink( 2 ), 4 );

			Paint.SetPen( Theme.Green );
			Paint.DrawIcon( rect.Shrink( 12 ), "forest", 32 );
		}

		// Entry count badge
		var entryCount = clutter.Entries?.Count ?? 0;
		if ( entryCount > 0 )
		{
			var badgeRect = new Rect( rect.Right - 18, rect.Top + 4, 16, 16 );
			Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.8f ) );
			Paint.ClearPen();
			Paint.DrawRect( badgeRect, 8 );

			Paint.SetDefaultFont( 9 );
			Paint.SetPen( Color.White );
			Paint.DrawText( badgeRect, entryCount.ToString(), TextFlag.Center );
		}

		// Name label
		Paint.SetDefaultFont();
		Paint.SetPen( Theme.Text );
		Paint.DrawText( item.Rect.Shrink( 2 ), clutter.ResourceName, TextFlag.CenterBottom );
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 4 );

		base.OnPaint();
	}
}
