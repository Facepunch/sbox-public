namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// True when nothing this panel or its descendants draw can reach <paramref name="clip"/>, a layout-space rect
	/// in the parent's coordinates. Transforms, CSS layers and custom drawing that can't report its bounds are never
	/// skipped, so a false negative only costs the drawing it would have saved.
	/// </summary>
	internal bool PaintsOutside( in Rect clip )
	{
		if ( ComputedStyle is null || !IsVisible || IsFixed ) return true;
		if ( LocalMatrix.HasValue || HasPanelLayer ) return false;
		if ( !TryGetContentBounds( out var bounds ) ) return false;

		AddOuterInk( ref bounds );
		if ( bounds.Grow( 1 ).Overlaps( clip ) ) return false;

		// Our own clip sits inside the bounds we just tested, so nothing below can get out to the clip either
		if ( _paintCache.ClipsChildren || !HasChildren ) return true;

		foreach ( var child in _children )
		{
			if ( !child.PaintsOutside( clip ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// Where OnDraw and inline content can draw, in layout space. False when it can't be told, which is any OnDraw
	/// override that doesn't report its own bounds.
	/// </summary>
	internal virtual bool TryGetContentBounds( out Rect bounds )
	{
		bounds = Box.Rect;
		return !_hasDrawCallback && !LayoutTree.HasInlineContent && this is not IPanelDraw;
	}

	/// <summary>
	/// The only custom drawing is <paramref name="type"/>'s OnDraw, so that type can vouch for its bounds.
	/// </summary>
	internal bool IsDrawnBy( Type type ) => _drawCallbackOwner == type && !LayoutTree.HasInlineContent && this is not IPanelDraw;

	/// <summary>
	/// Grow <paramref name="bounds"/> by the outline and outset shadows, which draw outside the border box.
	/// </summary>
	void AddOuterInk( ref Rect bounds )
	{
		if ( _paintCache.OutlineColor.a > 0 && _paintCache.OutlineWidth > 0 )
			bounds.Add( Box.Rect.Grow( MathF.Max( 0, _paintCache.OutlineOffset + _paintCache.OutlineWidth ) ) );

		var shadows = ComputedStyle.BoxShadow;
		for ( int i = 0; i < shadows.Count; i++ )
		{
			var shadow = shadows[i];
			if ( shadow.Inset || shadow.Color.a <= 0 ) continue;

			// Matches UICssBoxBatched.FromShadow
			var offset = new Vector2( shadow.OffsetX, shadow.OffsetY );
			bounds.Add( (Box.Rect + offset).Grow( shadow.Spread ).Grow( MathF.Ceiling( shadow.Blur * 1.5f ) ) );
		}
	}
}
