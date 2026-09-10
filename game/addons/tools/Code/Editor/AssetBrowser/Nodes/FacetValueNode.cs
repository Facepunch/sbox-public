using Sandbox.UI;

namespace Editor.AssetBrowsing.Nodes;

/// <summary>
/// A leaf node representing one facet value (e.g. "Nature" under Category).
/// Renders as a checkbox that can be included or excluded. Multiple values
/// can be active at once — the selection state lives in CloudLocations.ActiveFilters.
/// </summary>
class FacetValueNode : TreeNode
{
	readonly string _facetName;
	readonly string _value;
	readonly string _label;
	readonly string _icon;
	readonly int _count;

	// Resolved lazily — works whether this node lives in the nav tree or the filter panel.
	ActiveFilterSet Filters => (TreeView as IFilterHost)?.ActiveFilters;

	bool IsIncluded => Filters?.IsIncluded( _facetName, _value ) ?? false;
	bool IsExcluded => Filters?.IsExcluded( _facetName, _value ) ?? false;

	public FacetValueNode( Package.Facet.Entry entry, string facetName )
	{
		_facetName = facetName;
		_value = entry.Name;
		_label = entry.Title;
		_icon = string.IsNullOrEmpty( entry.Icon ) ? "label" : entry.Icon;
		_count = entry.Count;
	}

	public void ToggleInclude() => Filters?.Toggle( _facetName, _value, false );
	public void ToggleExclude() => Filters?.Toggle( _facetName, _value, true );

	public override void OnPaint( VirtualWidget item )
	{
		// Hover highlight only — selection is communicated through the checkbox, not row highlight
		if ( item.Hovered )
		{
			var r = new Rect( 0, item.Rect.Top, item.Rect.Right, item.Rect.Height );
			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.08f ) );
			Paint.DrawRect( r, 1 );
		}

		var rect = item.Rect;
		var checkRect = new Rect( rect.Left + 6, rect.Center.y - 6, 12, 12 );

		if ( IsIncluded )
		{
			Paint.SetPen( Theme.Blue );
			Paint.SetBrush( Theme.Blue.WithAlpha( 0.25f ) );
			Paint.DrawRect( checkRect, 2 );
			Paint.SetPen( Theme.Blue );
			Paint.DrawIcon( checkRect.Grow( 2 ), "check", 10, TextFlag.Center );
		}
		else if ( IsExcluded )
		{
			Paint.SetPen( Theme.Red );
			Paint.SetBrush( Theme.Red.WithAlpha( 0.15f ) );
			Paint.DrawRect( checkRect, 2 );
			Paint.SetPen( Theme.Red );
			Paint.DrawIcon( checkRect.Grow( 2 ), "remove", 10, TextFlag.Center );
		}
		else
		{
			Paint.SetPen( Theme.TextLight.WithAlpha( 0.2f ) );
			Paint.ClearBrush();
			Paint.DrawRect( checkRect, 2 );
		}

		var color = IsExcluded ? Theme.TextLight.WithAlpha( 0.4f ) : Theme.Text;

		var iconRect = rect;
		iconRect.Left += 24;
		Paint.SetPen( color );
		Paint.DrawIcon( iconRect, _icon, 16, TextFlag.LeftCenter );

		var textRect = rect.Shrink( 44, 0 );
		Paint.SetPen( color );
		Paint.SetDefaultFont();
		var nameRect = Paint.DrawText( textRect, _label, TextFlag.LeftCenter );

		if ( _count > 0 )
		{
			var countRect = item.Rect;
			countRect.Left = nameRect.Right + 10;
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.4f ) );
			Paint.SetDefaultFont( 7 );
			Paint.DrawTextBox( countRect, $"{_count:n0}", Theme.Text.WithAlpha( 0.6f ), new Margin( 4, 1 ), 3.0f, TextFlag.LeftCenter );
		}
	}

	public override bool OnContextMenu()
	{
		var menu = new ContextMenu( null );

		if ( IsIncluded )
		{
			menu.AddOption( "Remove Filter", "check_box_outline_blank", () => Filters?.Toggle( _facetName, _value ) );
			menu.AddOption( "Exclude", "block", () => Filters?.Toggle( _facetName, _value, true ) );
		}
		else if ( IsExcluded )
		{
			menu.AddOption( "Remove Exclusion", "check_box_outline_blank", () => Filters?.Toggle( _facetName, _value, true ) );
			menu.AddOption( "Include", "check_circle", () => Filters?.Toggle( _facetName, _value, false ) );
		}
		else
		{
			menu.AddOption( "Include", "check_circle", () => Filters?.Toggle( _facetName, _value, false ) );
			menu.AddOption( "Exclude", "block", () => Filters?.Toggle( _facetName, _value, true ) );
		}

		menu.OpenAtCursor();
		return true;
	}
}
