using Native;
using System;

namespace Editor;

public class ColorSampler
{
	public Action<Color> OnPicked;
	public Action<Vector2> OnPositionPreview;
	public Func<Vector2, bool> OnPositionPicked;
	public Action<Vector2, Rect> OnPositionPaint;
	public Action OnCancelled;

	private List<ColorSamplerOverlay> _overlays;

	public ColorSampler()
	{
		_overlays = new List<ColorSamplerOverlay>();
	}

	public void Show()
	{
		for ( int i = 0; i < QApp.ScreenCount(); i++ )
		{
			var overlay = new ColorSamplerOverlay( i );
			AddOverlay( overlay );
		}
	}

	public void ShowPositionPicker( Func<Rect> screenRect )
	{
		AddOverlay( new ColorSamplerOverlay( screenRect )
		{
			OnPositionPreview = position => OnPositionPreview?.Invoke( position ),
			OnPositionPaint = ( position, rect ) => OnPositionPaint?.Invoke( position, rect )
		} );
	}

	public void Hide()
	{
		foreach ( var overlay in _overlays )
		{
			overlay.Destroy();
		}
		_overlays.Clear();
	}

	~ColorSampler()
	{
		Hide();
	}

	private void _OnPicked( Color color )
	{
		OnPicked?.Invoke( color );
		Hide();
	}

	private void _OnCancelled()
	{
		OnCancelled?.Invoke();
		Hide();
	}

	private void AddOverlay( ColorSamplerOverlay overlay )
	{
		overlay.OnPicked += _OnPicked;
		overlay.OnPositionPicked += _OnPositionPicked;
		overlay.OnCancelled += _OnCancelled;
		_overlays.Add( overlay );
	}

	private void _OnPositionPicked( Vector2 position )
	{
		if ( OnPositionPicked?.Invoke( position ) == false )
			return;

		Hide();
	}
}

internal class ColorSamplerOverlay : Widget
{
	public Action<Color> OnPicked;
	public Action<Vector2> OnPositionPreview;
	public Action<Vector2> OnPositionPicked;
	public Action<Vector2, Rect> OnPositionPaint;
	public Action OnCancelled;

	private Pixmap _pixmap;
	private Func<Rect> _screenRect;

	public ColorSamplerOverlay( int screenNumber ) : base()
	{
		//PixmapCursor = Pixmap.FromFile( "toolimages:cursors/eyedropper.png" );
		Cursor = CursorShape.Blank;

		IsFramelessWindow = true;
		DeleteOnClose = true;

		QScreen screen = QApp.GetScreen( screenNumber );

		_pixmap = new Pixmap( screen.getCapture() );

		Show();
		Raise();

		Rect rect = screen.geometry().Rect;
		Position = rect.Position;
		Size = rect.Size;

		MouseTracking = true;
	}

	public ColorSamplerOverlay( Func<Rect> screenRect ) : base()
	{
		ArgumentNullException.ThrowIfNull( screenRect );

		_screenRect = screenRect;

		Cursor = CursorShape.Cross;
		IsFramelessWindow = true;
		TranslucentBackground = true;
		NoSystemBackground = true;
		DeleteOnClose = true;
		MouseTracking = true;

		SyncPosition();
		Show();
		Raise();
		Focus();
	}

	public Color CurrentColor()
	{
		Vector3 curPos = _widget.mapFromGlobal( Native.QApp.CursorPosition() );
		if ( curPos.x < 0 || curPos.y < 0 || curPos.x >= _pixmap.Width || curPos.y >= _pixmap.Height )
		{
			return Color.Black;
		}

		return _pixmap.GetPixel( (int)curPos.x, (int)curPos.y );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( _screenRect is null )
			OnPicked?.Invoke( CurrentColor() );
		else
			OnPositionPicked?.Invoke( e.ScreenPosition );

		e.Accepted = true;
	}

	protected override void OnMouseRightClick( MouseEvent e )
	{
		base.OnMouseRightClick( e );

		OnCancelled?.Invoke();
		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		Update();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		if ( _screenRect is not null )
		{
			OnPositionPaint?.Invoke( Application.CursorPosition, ScreenRect );
			return;
		}

		Paint.Draw( ScreenRect.WithoutPosition, _pixmap );

		Vector3 curPos = _widget.mapFromGlobal( Native.QApp.CursorPosition() );
		Vector3 nativePos = _widget.mapFromGlobal( Native.QApp.NativeCursorPosition() );
		if ( curPos.x < 0 || curPos.y < 0 || curPos.x >= _pixmap.Width || curPos.y >= _pixmap.Height )
		{
			return;
		}

		const int SAMPLE_RADIUS = 6;
		const int PREVIEW_PIXEL_SIZE = 7;

		Paint.Pen = new Color( 128, 128, 128 );
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		Vector3 drawPos = curPos - (new Vector3( PREVIEW_PIXEL_SIZE, PREVIEW_PIXEL_SIZE ) / 2);
		int previewSize = PREVIEW_PIXEL_SIZE * (SAMPLE_RADIUS * 2);

		Color current = CurrentColor();
		Paint.SetBrush( current );
		Rect previreRect = new Rect( drawPos.x - (previewSize / 2), drawPos.y + (previewSize / 2) + 16, previewSize + PREVIEW_PIXEL_SIZE, 32 );
		Paint.DrawRect( previreRect, 4 );
		Paint.DrawText( previreRect, $"{current.Hex}", TextFlag.Center );

		Paint.Antialiasing = false;

		const int Y_OFFSET = 0;
		for ( int dy = -SAMPLE_RADIUS; dy <= SAMPLE_RADIUS; ++dy )
		{
			for ( int dx = -SAMPLE_RADIUS; dx <= SAMPLE_RADIUS; ++dx )
			{
				Vector2 pos = new Vector2( nativePos.x + dx, nativePos.y + dy );
				if ( pos.x < 0 || pos.y < 0 || pos.x >= _pixmap.Width || pos.y >= _pixmap.Height )
				{
					continue;
				}

				Paint.Pen = new Color( 128, 128, 128 );
				Paint.SetBrush( _pixmap.GetPixel( (int)pos.x, (int)pos.y ) );
				Paint.DrawRect( new Rect( drawPos.x + dx * PREVIEW_PIXEL_SIZE, drawPos.y + Y_OFFSET + dy * PREVIEW_PIXEL_SIZE, PREVIEW_PIXEL_SIZE, PREVIEW_PIXEL_SIZE ) );
			}
		}

		Paint.Pen = Color.Black;
		Paint.PenSize = 2;
		Paint.ClearBrush();
		Paint.DrawRect( new Rect( drawPos.x, drawPos.y + Y_OFFSET, PREVIEW_PIXEL_SIZE, PREVIEW_PIXEL_SIZE ) );
		Paint.PenSize = 1;

	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );

		if ( _screenRect is not null && e.Key == KeyCode.Escape )
			OnCancelled?.Invoke();
	}

	[EditorEvent.Frame]
	private void Frame()
	{
		if ( _screenRect is null )
			return;

		SyncPosition();
		OnPositionPreview?.Invoke( Application.CursorPosition );
		Update();
	}

	private void SyncPosition()
	{
		var rect = _screenRect();
		Position = rect.Position;
		Size = rect.Size;
	}

	protected override void OnBlur( FocusChangeReason reason )
	{
		base.OnBlur( reason );
		OnCancelled?.Invoke();
	}
}
