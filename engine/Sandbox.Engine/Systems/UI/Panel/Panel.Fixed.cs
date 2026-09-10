namespace Sandbox.UI;

public partial class Panel
{
	internal bool IsFixed => this is not RootPanel && ComputedStyle?.Position == PositionMode.Fixed && ComputedStyle.Display != DisplayMode.Contents;
	internal bool IsOutOfFlow => ComputedStyle?.Position is PositionMode.Absolute or PositionMode.Fixed;

	// Ownership, events and inherited styles remain logical. Only visual ancestry stops here.
	internal Panel VisualParent => IsFixed ? null : Parent;

	internal Panel VisualRoot
	{
		get
		{
			var panel = this;
			while ( panel.VisualParent is { } parent ) panel = parent;
			return panel;
		}
	}

	internal void SortRenderChildren()
	{
		if ( !_renderChildrenDirty || _renderChildren is null ) return;
		_renderChildren.Sort( ( x, y ) =>
		{
			var order = x.GetRenderOrderIndex().CompareTo( y.GetRenderOrderIndex() );
			return order != 0 ? order : x.SiblingIndex.CompareTo( y.SiblingIndex );
		} );
		_renderChildrenDirty = false;
	}

	internal Panel FindVisualPanelAt( Vector2 point, bool visibleOnly, bool needPointerEvents, Func<Panel, bool> match = null )
	{
		if ( IsDeleted || ComputedStyle is null || (visibleOnly && !IsVisible) ) return null;
		point = GetTransformPosition( point );
		var inside = IsInside( point );
		if ( !inside && ComputedStyle.Overflow != OverflowMode.Visible ) return null;
		SortRenderChildren();
		if ( _renderChildren is not null )
		{
			for ( int i = _renderChildren.Count - 1; i >= 0; i-- )
			{
				var child = _renderChildren[i];
				if ( child.IsFixed ) continue;
				var hit = child.FindVisualPanelAt( point, visibleOnly, needPointerEvents, match );
				if ( hit is not null ) return hit;
			}
		}
		return inside && (!needPointerEvents || ComputedStyle.PointerEvents != PointerEvents.None)
			&& (match is null || match( this )) ? this : null;
	}
}

public partial class RootPanel
{
	internal bool FixedOverlaysDirty = true;
	private readonly List<Panel> fixedOverlays = new();

	internal Panel FindFixedPanelAt( Vector2 point, bool visibleOnly = true, bool needPointerEvents = false, Func<Panel, bool> match = null )
	{
		if ( !PanelBounds.IsInside( point ) ) return null;
		var overlays = FixedOverlays;
		for ( int i = overlays.Count - 1; i >= 0; i-- )
		{
			var hit = overlays[i].FindVisualPanelAt( point, visibleOnly, needPointerEvents, match );
			if ( hit is not null ) return hit;
		}
		return null;
	}

	internal List<Panel> FixedOverlays
	{
		get
		{
			if ( !FixedOverlaysDirty ) return fixedOverlays;
			FixedOverlaysDirty = false;
			fixedOverlays.Clear();
			Collect( this );
			if ( fixedOverlays.Count < 2 ) return fixedOverlays;
			// Stable sort: equal z-index uses logical tree order, nested overlays after their owner.
			var sorted = fixedOverlays.OrderBy( p => p.ComputedStyle.ZIndex ?? 0 ).ToArray();
			fixedOverlays.Clear();
			fixedOverlays.AddRange( sorted );
			return fixedOverlays;

			void Collect( Panel panel )
			{
				if ( !panel.IsValid() || panel.LayoutTree?.Node.SubtreeHasFixed != true || panel.ComputedStyle?.Display == DisplayMode.None ) return;
				if ( panel.IsFixed ) fixedOverlays.Add( panel );
				if ( panel._children is null ) return;
				foreach ( var child in panel._children ) Collect( child );
			}
		}
	}
}
