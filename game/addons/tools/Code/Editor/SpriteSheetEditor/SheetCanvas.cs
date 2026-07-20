using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.SpriteSheetEditor;

/// <summary>
/// The sheet, and the slice rectangles laid over it.
/// <para>
/// Scene space is image space: one scene unit is one source pixel, with the image's top-left at the
/// origin. Slice rects then need no conversion at all, which removes an entire category of
/// off-by-a-pixel bug from everything that draws or drags them.
/// </para>
/// </summary>
public class SheetCanvas : GraphicsView
{
	readonly Window _editor;

	SheetImageItem _image;
	readonly List<SliceItem> _sliceItems = new();

	RectPreviewItem _preview;
	bool _creating;
	Vector2 _createStart;

	bool _panning;
	Vector2 _panStart;

	public SheetCanvas( Window editor )
	{
		_editor = editor;

		Name = "Sheet";
		WindowTitle = "Sheet";
		SetWindowIcon( "grid_view" );

		Antialiasing = true;
		TextAntialiasing = true;

		// Pixel art is most of what gets sliced, and smoothing it while zoomed in makes it
		// impossible to see where the artwork actually ends.
		BilinearFiltering = false;

		MinZoom = 0.02f;
		MaxZoom = 50f;

		HorizontalScrollbar = ScrollbarMode.Auto;
		VerticalScrollbar = ScrollbarMode.Auto;

		SetBackgroundImage( BuildCheckerboard() );

		FocusMode = FocusMode.Click;

		_editor.OnSheetLoaded += Rebuild;
		_editor.OnSheetModified += Rebuild;
		_editor.OnSelectionChanged += SyncSelection;

		Rebuild();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		_editor.OnSheetLoaded -= Rebuild;
		_editor.OnSheetModified -= Rebuild;
		_editor.OnSelectionChanged -= SyncSelection;
	}

	/// <summary>
	/// The usual grey chequer that means "nothing here". Tiled by the view, so it stays the same
	/// size on screen however far in you zoom.
	/// </summary>
	static Pixmap BuildCheckerboard()
	{
		const int cell = 8;
		using var bitmap = new Bitmap( cell * 2, cell * 2 );

		var dark = new Color( 0.16f, 0.16f, 0.16f );
		var light = new Color( 0.22f, 0.22f, 0.22f );

		for ( int y = 0; y < cell * 2; y++ )
		{
			for ( int x = 0; x < cell * 2; x++ )
			{
				var odd = ((x / cell) + (y / cell)) % 2 == 1;
				bitmap.SetPixel( x, y, odd ? light : dark );
			}
		}

		return Pixmap.FromBitmap( bitmap );
	}

	//
	// Building
	//

	public void Rebuild()
	{
		DeleteAllItems();
		_sliceItems.Clear();
		_image = null;
		_preview = null;

		var pixmap = _editor.SourcePixmap;
		var width = _editor.SourceWidth;
		var height = _editor.SourceHeight;

		if ( pixmap is null || width <= 0 || height <= 0 )
		{
			SceneRect = new Rect( -100, -100, 200, 200 );
			Update();
			return;
		}

		_image = new SheetImageItem( pixmap, new Vector2( width, height ) );
		Add( _image );

		foreach ( var slice in _editor.Sheet?.Slices ?? new List<SpriteSheet.Slice>() )
		{
			var item = new SliceItem( _editor, slice );
			Add( item );
			_sliceItems.Add( item );
		}

		// Room to pan the sheet's edges away from the window frame, so a slice tight to one corner
		// is still reachable.
		var margin = new Vector2( width, height ) * 0.5f;
		SceneRect = new Rect( -margin.x, -margin.y, width + (margin.x * 2), height + (margin.y * 2) );

		SyncSelection();
		Update();
	}

	void SyncSelection()
	{
		foreach ( var item in _sliceItems )
		{
			item.IsSelected = _editor.Selection.Contains( item.SliceId );
		}
	}

	//
	// Navigation
	//

	/// <summary>
	/// Scale the sheet to fit, keeping it the shape it actually is.
	/// </summary>
	/// <remarks>
	/// Deliberately not <c>FitInView</c>. Qt's <c>fitInView</c> defaults to <c>IgnoreAspectRatio</c>
	/// and the binding takes no aspect argument, so it stretches the sheet to fill the window. It also
	/// writes the view transform directly, which leaves <c>GraphicsView.Scale</c> holding a stale
	/// value - and the slice handles size themselves from that, so they would come out wrong too.
	/// </remarks>
	public void FitToWindow()
	{
		var width = _editor.SourceWidth;
		var height = _editor.SourceHeight;
		if ( width <= 0 || height <= 0 ) return;

		var view = Size;
		if ( view.x <= 1 || view.y <= 1 ) return;

		// One scale for both axes - that is the whole point.
		const float padding = 1.06f;
		var scale = MathF.Min( view.x / (width * padding), view.y / (height * padding) );

		Scale = new Vector2( scale, scale ).Clamp( MinZoom, MaxZoom );
		CenterOn( new Vector2( width, height ) * 0.5f );
	}

	public void ZoomToActualSize()
	{
		var centre = ToScene( Size * 0.5f );
		Scale = 1f;
		CenterOn( centre );
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		// Anchored on the cursor: the pixel under the pointer stays put, which is the difference
		// between zooming feeling like a magnifier and feeling like a slider.
		Zoom( e.Delta > 0 ? 1.1f : 1f / 1.1f, e.Position );
		e.Accepted = true;
	}

	//
	// Mouse
	//

	protected override void OnMousePress( MouseEvent e )
	{
		// Alt is navigation, everywhere, always. Holding it must never edit anything.
		if ( e.MiddleMouseButton || (e.LeftMouseButton && e.HasAlt) )
		{
			_panning = true;
			_panStart = e.LocalPosition;
			Cursor = CursorShape.ClosedHand;
			e.Accepted = true;
			return;
		}

		base.OnMousePress( e );

		if ( !e.LeftMouseButton ) return;

		var scenePosition = ToScene( e.LocalPosition );

		// Something under the cursor deals with its own dragging.
		if ( GetItemAt( scenePosition ) is not null ) return;

		if ( !e.HasCtrl && !e.HasShift ) _editor.ClearSelection();

		// Dragging empty space draws out a new slice.
		if ( _editor.SourceWidth > 0 )
		{
			_creating = true;
			_createStart = Snap( scenePosition );

			_preview = new RectPreviewItem();
			Add( _preview );
		}
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( _panning )
		{
			Translate( e.LocalPosition - _panStart );
			_panStart = e.LocalPosition;
			e.Accepted = true;
			return;
		}

		if ( _creating && _preview is not null )
		{
			_preview.SetRect( BuildRect( _createStart, Snap( ToScene( e.LocalPosition ) ) ) );
			e.Accepted = true;
			return;
		}

		base.OnMouseMove( e );
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( _panning )
		{
			_panning = false;
			Cursor = CursorShape.None;
			e.Accepted = true;
			return;
		}

		if ( _creating )
		{
			_creating = false;

			var rect = _preview?.Rect ?? default;
			_preview?.Destroy();
			_preview = null;

			// A click rather than a drag - the user was deselecting, not drawing.
			if ( rect.Width >= 1 && rect.Height >= 1 )
			{
				_editor.AddSlice( ClampToSheet( rect ) );
			}

			e.Accepted = true;
			return;
		}

		base.OnMouseReleased( e );
	}

	//
	// Keys
	//
	// These live on the canvas rather than on the window so they cannot fire while someone is
	// renaming a slice in the list below - "T" is a letter long before it is a shortcut.
	//

	protected override void OnKeyPress( KeyEvent e )
	{
		switch ( e.Key )
		{
			case KeyCode.F:
				if ( e.HasShift ) ZoomToActualSize();
				else FitToWindow();
				e.Accepted = true;
				return;

			case KeyCode.T:
				_editor.TrimSelected();
				e.Accepted = true;
				return;

			case KeyCode.Delete:
			case KeyCode.Backspace:
				_editor.DeleteSelected();
				e.Accepted = true;
				return;

			case KeyCode.D:
				if ( e.HasCtrl )
				{
					_editor.DuplicateSelected();
					e.Accepted = true;
					return;
				}
				break;

			case KeyCode.A:
				if ( e.HasCtrl )
				{
					_editor.SelectAll();
					e.Accepted = true;
					return;
				}
				break;

			case KeyCode.Escape:
				_editor.ClearSelection();
				e.Accepted = true;
				return;
		}

		base.OnKeyPress( e );
	}

	//
	// Helpers
	//

	/// <summary>Slices are whole pixels, always. A rect that lands on a half pixel is a bug later.</summary>
	static Vector2 Snap( Vector2 scenePosition )
		=> new( MathF.Round( scenePosition.x ), MathF.Round( scenePosition.y ) );

	static Rect BuildRect( Vector2 a, Vector2 b )
	{
		// Built from corners rather than a direction, so dragging up or left works as well as down
		// and right.
		var left = MathF.Min( a.x, b.x );
		var top = MathF.Min( a.y, b.y );

		return new Rect( left, top, MathF.Abs( a.x - b.x ), MathF.Abs( a.y - b.y ) );
	}

	Rect ClampToSheet( Rect rect )
	{
		var left = Math.Clamp( rect.Left, 0, _editor.SourceWidth );
		var top = Math.Clamp( rect.Top, 0, _editor.SourceHeight );
		var right = Math.Clamp( rect.Left + rect.Width, 0, _editor.SourceWidth );
		var bottom = Math.Clamp( rect.Top + rect.Height, 0, _editor.SourceHeight );

		return new Rect( left, top, right - left, bottom - top );
	}
}

/// <summary>
/// The sheet image itself. Sits under everything and never takes a click, so dragging across it
/// draws a new slice rather than shoving the picture around.
/// </summary>
internal class SheetImageItem : GraphicsItem
{
	readonly Pixmap _pixmap;

	public SheetImageItem( Pixmap pixmap, Vector2 size )
	{
		_pixmap = pixmap;

		Size = size;
		HandlePosition = new Vector2( 0, 0 );
		Position = Vector2.Zero;
		ZIndex = -100;

		Movable = false;
		Selectable = false;
		HoverEvents = false;
	}

	public override bool Contains( Vector2 localPos ) => false;

	protected override void OnPaint()
	{
		Paint.Draw( new Rect( Vector2.Zero, Size ), _pixmap );
	}
}

/// <summary>
/// The dashed outline shown while a new slice is being dragged out.
/// </summary>
internal class RectPreviewItem : GraphicsItem
{
	public Rect Rect { get; private set; }

	public RectPreviewItem()
	{
		HandlePosition = new Vector2( 0, 0 );
		ZIndex = 500;
		Movable = false;
		Selectable = false;
		HoverEvents = false;
	}

	public void SetRect( Rect rect )
	{
		PrepareGeometryChange();

		Rect = rect;
		Position = rect.Position;
		Size = rect.Size;

		Update();
	}

	public override bool Contains( Vector2 localPos ) => false;

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Theme.Primary.WithAlpha( 0.15f ), Theme.Primary, 1f );
		Paint.DrawRect( new Rect( Vector2.Zero, Size ) );
	}
}
