using System;
using System.Text.Json.Nodes;

namespace Editor.SpriteSheetEditor;

/// <summary>
/// One slice on the canvas: an outline you can move and resize, and a pivot you can drag onto a
/// joint.
/// <para>
/// What the mouse does is decided by which part of the slice it is nearest - pivot, then corner,
/// then edge, then body - rather than by a mode picked from a toolbar. Borrowed from Unity's sprite
/// editor, where it means resizing never needs a different tool to moving.
/// </para>
/// </summary>
public class SliceItem : GraphicsItem
{
	/// <summary>How near a handle counts as grabbing it, in screen pixels.</summary>
	const float PickRadius = 5f;

	readonly Window _editor;

	public Guid SliceId { get; }

	bool _isSelected;
	public bool IsSelected
	{
		get => _isSelected;
		set { if ( _isSelected == value ) return; _isSelected = value; Update(); }
	}

	enum Zone { None, Body, Pivot, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

	Zone _hoverZone = Zone.None;
	Zone _dragZone = Zone.None;
	Vector2 _dragStart;
	Rect _dragOriginalRect;
	JsonObject _dragUndoState;

	public SliceItem( Window editor, SpriteSheet.Slice slice )
	{
		_editor = editor;
		SliceId = slice.Id;

		HandlePosition = new Vector2( 0, 0 );
		Movable = false;      // dragging is handled here, so Qt must not also move the item
		Selectable = false;   // selection lives on the editor, so it survives an undo
		HoverEvents = true;
		ZIndex = 10;

		SyncFromSlice();
	}

	SpriteSheet.Slice Slice => _editor.Sheet?.GetSlice( SliceId );

	/// <summary>Scene units per screen pixel, so handles stay the same size however far you zoom.</summary>
	float SceneScale
	{
		get
		{
			var zoom = (GraphicsView as SheetCanvas)?.Scale.x ?? 1f;
			return zoom <= 0.0001f ? 1f : 1f / zoom;
		}
	}

	public void SyncFromSlice()
	{
		var slice = Slice;
		if ( slice is null ) return;

		PrepareGeometryChange();
		Position = slice.Rect.Position;
		Size = slice.Rect.Size;
		Update();
	}

	// Handles sit outside the outline, so the item has to claim a little more room than its rect or
	// they get clipped away.
	public override Rect BoundingRect
	{
		get
		{
			var pad = PickRadius * SceneScale * 2f;
			return new Rect( -pad, -pad, Size.x + (pad * 2), Size.y + (pad * 2) );
		}
	}

	public override bool Contains( Vector2 localPos ) => HitTest( localPos ) != Zone.None;

	Zone HitTest( Vector2 local )
	{
		var slice = Slice;
		if ( slice is null ) return Zone.None;

		var pick = PickRadius * SceneScale;

		if ( _isSelected )
		{
			// Pivot first: it sits inside the body, and moving it is the finer operation.
			if ( local.Distance( PivotLocal( slice ) ) <= pick * 1.6f ) return Zone.Pivot;

			var left = MathF.Abs( local.x ) <= pick;
			var right = MathF.Abs( local.x - Size.x ) <= pick;
			var top = MathF.Abs( local.y ) <= pick;
			var bottom = MathF.Abs( local.y - Size.y ) <= pick;

			var withinX = local.x >= -pick && local.x <= Size.x + pick;
			var withinY = local.y >= -pick && local.y <= Size.y + pick;

			// Corners beat edges - they are the smaller target and the more specific intent.
			if ( left && top ) return Zone.TopLeft;
			if ( right && top ) return Zone.TopRight;
			if ( left && bottom ) return Zone.BottomLeft;
			if ( right && bottom ) return Zone.BottomRight;

			if ( left && withinY ) return Zone.Left;
			if ( right && withinY ) return Zone.Right;
			if ( top && withinX ) return Zone.Top;
			if ( bottom && withinX ) return Zone.Bottom;
		}

		if ( local.x >= 0 && local.y >= 0 && local.x <= Size.x && local.y <= Size.y ) return Zone.Body;

		return Zone.None;
	}

	static Vector2 PivotLocal( SpriteSheet.Slice slice )
	{
		// Pivot is normalized and Y-up; the canvas is Y-down image space.
		return new Vector2(
			slice.Pivot.x * slice.Rect.Width,
			(1f - slice.Pivot.y) * slice.Rect.Height );
	}

	static CursorShape CursorFor( Zone zone ) => zone switch
	{
		Zone.Pivot => CursorShape.SizeAll,
		Zone.Left or Zone.Right => CursorShape.SizeH,
		Zone.Top or Zone.Bottom => CursorShape.SizeV,
		Zone.TopLeft or Zone.BottomRight => CursorShape.SizeFDiag,
		Zone.TopRight or Zone.BottomLeft => CursorShape.SizeBDiag,
		Zone.Body => CursorShape.SizeAll,
		_ => CursorShape.None
	};

	//
	// Mouse
	//

	protected override void OnHoverMove( GraphicsHoverEvent e )
	{
		base.OnHoverMove( e );

		var zone = HitTest( e.LocalPosition );
		if ( zone == _hoverZone ) return;

		_hoverZone = zone;
		Cursor = CursorFor( zone );
		Update();
	}

	protected override void OnHoverLeave( GraphicsHoverEvent e )
	{
		base.OnHoverLeave( e );

		_hoverZone = Zone.None;
		Cursor = CursorShape.None;
		Update();
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		// Alt means navigate. The view is panning; this must stay out of the way.
		if ( e.HasAlt ) return;
		if ( !e.LeftMouseButton ) return;

		var slice = Slice;
		if ( slice is null ) return;

		if ( !_isSelected || e.HasCtrl || e.HasShift )
		{
			_editor.Select( slice, add: e.HasCtrl || e.HasShift );
		}

		_dragZone = HitTest( e.LocalPosition );
		if ( _dragZone == Zone.None ) return;

		_dragStart = e.ScenePosition;
		_dragOriginalRect = slice.Rect;

		// One undo entry for the whole drag, not one per mouse-move.
		_dragUndoState = _editor.Sheet.Serialize();

		e.Accepted = true;
	}

	protected override void OnMouseMove( GraphicsMouseEvent e )
	{
		if ( _dragZone == Zone.None ) return;

		var slice = Slice;
		if ( slice is null ) return;

		var delta = e.ScenePosition - _dragStart;

		if ( _dragZone == Zone.Pivot )
		{
			var target = e.ScenePosition;

			// Whole pixels unless asked otherwise - joints land on pixels in the art.
			if ( !e.HasAlt ) target = new Vector2( MathF.Round( target.x ), MathF.Round( target.y ) );

			slice.SetPivotFromImagePixels( target );

			// Ctrl gives up precision for certainty, snapping to the nine usual spots.
			if ( e.HasCtrl ) SnapPivotToNearestPreset( slice );
		}
		else
		{
			slice.Rect = _dragZone == Zone.Body
				? MoveRect( _dragOriginalRect, delta )
				: ResizeRect( _dragOriginalRect, _dragZone, delta );

			SyncFromSlice();
		}

		Update();
		e.Accepted = true;
	}

	protected override void OnMouseReleased( GraphicsMouseEvent e )
	{
		if ( _dragZone == Zone.None ) return;

		var zone = _dragZone;
		_dragZone = Zone.None;

		if ( _dragUndoState is not null )
		{
			_editor.CommitDrag( zone == Zone.Pivot ? "Move Pivot" : zone == Zone.Body ? "Move Slice" : "Resize Slice",
				_dragUndoState );

			_dragUndoState = null;
		}

		e.Accepted = true;
	}

	//
	// Geometry
	//

	Rect MoveRect( Rect rect, Vector2 delta )
	{
		var left = MathF.Round( rect.Left + delta.x );
		var top = MathF.Round( rect.Top + delta.y );

		// Slide along the sheet edge rather than shrinking against it - a rect dragged into the
		// corner should keep its size.
		left = Math.Clamp( left, 0, MathF.Max( 0, _editor.SourceWidth - rect.Width ) );
		top = Math.Clamp( top, 0, MathF.Max( 0, _editor.SourceHeight - rect.Height ) );

		return new Rect( left, top, rect.Width, rect.Height );
	}

	Rect ResizeRect( Rect rect, Zone zone, Vector2 delta )
	{
		var left = rect.Left;
		var top = rect.Top;
		var right = rect.Left + rect.Width;
		var bottom = rect.Top + rect.Height;

		if ( zone is Zone.Left or Zone.TopLeft or Zone.BottomLeft ) left += delta.x;
		if ( zone is Zone.Right or Zone.TopRight or Zone.BottomRight ) right += delta.x;
		if ( zone is Zone.Top or Zone.TopLeft or Zone.TopRight ) top += delta.y;
		if ( zone is Zone.Bottom or Zone.BottomLeft or Zone.BottomRight ) bottom += delta.y;

		left = MathF.Round( left );
		top = MathF.Round( top );
		right = MathF.Round( right );
		bottom = MathF.Round( bottom );

		// Dragging an edge past its opposite flips the rect rather than producing a negative one.
		if ( right < left ) (left, right) = (right, left);
		if ( bottom < top ) (top, bottom) = (bottom, top);

		left = Math.Clamp( left, 0, _editor.SourceWidth );
		top = Math.Clamp( top, 0, _editor.SourceHeight );
		right = Math.Clamp( right, 0, _editor.SourceWidth );
		bottom = Math.Clamp( bottom, 0, _editor.SourceHeight );

		// Never let a slice vanish - a zero-sized rect cannot be grabbed again.
		return new Rect( left, top, MathF.Max( 1, right - left ), MathF.Max( 1, bottom - top ) );
	}

	static void SnapPivotToNearestPreset( SpriteSheet.Slice slice )
	{
		var best = SpriteSheet.PivotAlignment.Center;
		var bestDistance = float.MaxValue;

		foreach ( SpriteSheet.PivotAlignment alignment in Enum.GetValues<SpriteSheet.PivotAlignment>() )
		{
			if ( alignment == SpriteSheet.PivotAlignment.Custom ) continue;

			var distance = slice.Pivot.Distance( SpriteSheet.Slice.GetPivot( alignment ) );
			if ( distance >= bestDistance ) continue;

			bestDistance = distance;
			best = alignment;
		}

		slice.SetAlignment( best );
	}

	//
	// Paint
	//

	protected override void OnPaint()
	{
		var slice = Slice;
		if ( slice is null ) return;

		var scale = SceneScale;
		var rect = new Rect( Vector2.Zero, Size );

		var colour = _isSelected ? Theme.Primary : Color.White.WithAlpha( 0.55f );

		if ( _isSelected )
		{
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.08f ) );
			Paint.SetPen( colour, 1.5f * scale );
		}
		else
		{
			Paint.SetBrushAndPen( Color.Transparent, colour, 1f * scale );
		}

		Paint.DrawRect( rect );

		DrawName( slice, scale );

		if ( !_isSelected ) return;

		DrawResizeHandles( scale );
		DrawPivot( slice, scale );
	}

	void DrawName( SpriteSheet.Slice slice, float scale )
	{
		// Counter-scaled so the label reads at a constant size however far in the view is zoomed.
		var fontSize = 9f * scale;

		// Below about this size the text is unreadable anyway and just muddies the outlines.
		if ( fontSize > Size.y * 0.6f ) return;

		Paint.SetPen( _isSelected ? Theme.Primary : Color.White.WithAlpha( 0.7f ) );
		Paint.SetFont( "Poppins", fontSize );
		Paint.DrawText( new Rect( 2 * scale, 1 * scale, Size.x, fontSize * 1.4f ), slice.Name,
			TextFlag.LeftTop | TextFlag.SingleLine );
	}

	void DrawResizeHandles( float scale )
	{
		var size = 3.5f * scale;

		Paint.SetBrushAndPen( Theme.Primary, Color.Black.WithAlpha( 0.8f ), 1f * scale );

		foreach ( var corner in new[]
		{
			new Vector2( 0, 0 ), new Vector2( Size.x, 0 ),
			new Vector2( 0, Size.y ), new Vector2( Size.x, Size.y )
		} )
		{
			Paint.DrawRect( new Rect( corner.x - size, corner.y - size, size * 2, size * 2 ) );
		}

		// Edge handles are smaller, so the corners stay the easier target.
		var edge = size * 0.75f;

		foreach ( var mid in new[]
		{
			new Vector2( Size.x * 0.5f, 0 ), new Vector2( Size.x * 0.5f, Size.y ),
			new Vector2( 0, Size.y * 0.5f ), new Vector2( Size.x, Size.y * 0.5f )
		} )
		{
			Paint.DrawRect( new Rect( mid.x - edge, mid.y - edge, edge * 2, edge * 2 ) );
		}
	}

	void DrawPivot( SpriteSheet.Slice slice, float scale )
	{
		var pivot = PivotLocal( slice );
		var radius = 4f * scale;

		// Crosshair through the circle, so the exact point is readable even over busy artwork.
		Paint.SetPen( Color.Black.WithAlpha( 0.7f ), 2.5f * scale );
		Paint.DrawLine( new Vector2( pivot.x - radius * 1.8f, pivot.y ), new Vector2( pivot.x + radius * 1.8f, pivot.y ) );
		Paint.DrawLine( new Vector2( pivot.x, pivot.y - radius * 1.8f ), new Vector2( pivot.x, pivot.y + radius * 1.8f ) );

		Paint.SetPen( Theme.Yellow, 1.25f * scale );
		Paint.DrawLine( new Vector2( pivot.x - radius * 1.8f, pivot.y ), new Vector2( pivot.x + radius * 1.8f, pivot.y ) );
		Paint.DrawLine( new Vector2( pivot.x, pivot.y - radius * 1.8f ), new Vector2( pivot.x, pivot.y + radius * 1.8f ) );

		Paint.SetBrushAndPen( Theme.Yellow.WithAlpha( _hoverZone == Zone.Pivot ? 0.9f : 0.35f ), Theme.Yellow, 1f * scale );
		Paint.DrawCircle( pivot, radius * 2 );
	}
}
