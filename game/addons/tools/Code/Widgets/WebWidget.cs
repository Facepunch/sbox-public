using System;

namespace Editor.Widgets;

/// <summary>
/// A widget that shows a web page.
/// </summary>
public class WebWidget : Widget
{
	/// <summary>
	/// Access to the HTML surface to change URL, etc.
	/// </summary>
	public WebSurface Surface { get; private set; }

	Pixmap background;

	public WebWidget( Widget parent ) : base( parent )
	{
		MinimumSize = 50;
		MouseTracking = true;
		FocusMode = FocusMode.TabOrClick;

		Surface = EditorUtility.CreateWebSurface();
		Surface.OnTexture = TextureChanged;
		Surface.Size = Size * Application.DpiScale;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		Surface?.Dispose();
		Surface = null;
	}

	protected override void OnResize()
	{
		Surface.Size = Size * Application.DpiScale;
	}


	/// <summary>
	/// Called whenever the texture needs redrawing
	/// </summary>
	private void TextureChanged( ReadOnlySpan<byte> span, Vector2 size )
	{
		if ( background == null || background.Size != size )
		{
			background = new Pixmap( size );
		}

		background.UpdateFromPixels( span, size );

		Update();
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		Surface.TellMouseMove( e.LocalPosition * Application.DpiScale );

		// convert html cursor name into a at cursor
		if ( Surface.Cursor == "pointer" ) Cursor = CursorShape.Finger;
		else if ( Surface.Cursor == "text" ) Cursor = CursorShape.IBeam;
		else Cursor = CursorShape.Arrow;
	}

	protected override void OnMouseWheel( WheelEvent e ) => Surface?.TellMouseWheel( (int)e.Delta );
	protected override void OnMousePress( MouseEvent e ) => Surface.TellMouseButton( e.Button, true );
	protected override void OnMouseReleased( MouseEvent e ) => Surface.TellMouseButton( e.Button, false );
	protected override void OnFocus( FocusChangeReason reason ) => Surface.HasKeyFocus = true;
	protected override void OnBlur( FocusChangeReason reason ) => Surface.HasKeyFocus = false;
	protected override void OnKeyRelease( KeyEvent e ) => Surface?.TellKey( e.NativeKeyCode, e.KeyboardModifiers, false );

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( !string.IsNullOrEmpty( e.Text ) && e.Key != KeyCode.Return )
		{
			Surface.TellChar( e.Text[0], e.KeyboardModifiers );
		}

		Surface.TellKey( e.NativeKeyCode, e.KeyboardModifiers, true );
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		if ( background == null )
			return;

		var r = LocalRect;

		Paint.Draw( r, background );
	}

	[WidgetGallery]
	[Title( "WebBrowser" )]
	[Icon( "web" )]
	internal static Widget WidgetGallery()
	{
		var canvas = new WebWidget( null );

		canvas.Surface.Url = "https://www.google.com/";

		return canvas;
	}

}
