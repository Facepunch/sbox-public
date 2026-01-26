using Sandbox.UI;
using Sandbox.Utility;
using System;

namespace Editor
{
	public enum PenStyle
	{
		None,
		Solid,
		Dash,
		Dot,
		DashDot,
		DashDotDot
	}

	public enum ElideMode
	{
		Left,
		Right,
		Middle,
		None
	}

	public enum RenderMode
	{
		Normal = CompositionMode.CompositionMode_SourceOver,
		Plus = CompositionMode.CompositionMode_Plus,
		Multiply = CompositionMode.CompositionMode_Multiply,
		Screen = CompositionMode.CompositionMode_Screen,
		Overlay = CompositionMode.CompositionMode_Overlay,
		Darken = CompositionMode.CompositionMode_Darken,
		Lighten = CompositionMode.CompositionMode_Lighten,
		ColorDodge = CompositionMode.CompositionMode_ColorDodge,
		ColorBurn = CompositionMode.CompositionMode_ColorBurn,
		HardLight = CompositionMode.CompositionMode_HardLight,
		SoftLight = CompositionMode.CompositionMode_SoftLight,
		Difference = CompositionMode.CompositionMode_Difference,
		Exclusion = CompositionMode.CompositionMode_Exclusion,
	}

	public static class Paint
	{
		internal static QPainter _painter;
		internal static StateFlag state;

		struct FontInfo
		{
			public string FontName;
			public float FontSize;
			public int FontWeight;

			public void Reset()
			{
				FontName = null;
				FontSize = 8;
				FontWeight = 400;
			}
		}

		static FontInfo fontInfo;


		internal static QPainter Current
		{
			get
			{
				ThreadSafe.AssertIsMainThread();

				return _painter;
			}
		}

		public static Rect LocalRect { get; set; }

		public static bool Antialiasing
		{
			set => Current.setRenderHint( RenderHints.Antialiasing, value );
		}

		public static bool TextAntialiasing
		{
			set => Current.setRenderHint( RenderHints.TextAntialiasing, value );
		}

		public static bool BilinearFiltering
		{
			set => Current.setRenderHint( RenderHints.SmoothPixmapTransform, value );
		}

		public static void Translate( Vector2 tx )
		{
			Current.translate( tx );
		}
		public static void Scale( float x, float y )
		{
			Current.scale( x, y );
		}
		public static void Rotate( float scale )
		{
			Current.rotate( scale );
		}

		public static void Rotate( float scale, Vector2 center )
		{
			Translate( center );
			Rotate( scale );
			Translate( -center );
		}

		public static void ResetTransform()
		{
			Current.resetTransform();
		}

		public static void DrawRect( in Rect rect, float borderRadius )
		{
			if ( borderRadius <= 0.0f )
			{
				DrawRect( rect );
				return;
			}

			Current.drawRoundedRect( rect, borderRadius, borderRadius );
		}

		public static void DrawRect( in Rect rect )
		{
			Current.drawRect( rect );
		}

		public static void DrawCircle( in Rect rect )
		{
			Current.drawEllipse( rect );
		}

		public static void DrawCircle( Vector2 position, Vector2 scale )
		{
			DrawCircle( new Rect( position - scale * 0.5f, scale ) );
		}

		/// <summary>
		/// Draws an arc (line). Angles are clockwise, 0 is north.
		/// </summary>
		/// <param name="center">The center of the circle</param>
		/// <param name="radius">The radius of the circle</param>
		/// <param name="angle">The center of the arc, in degrees</param>
		/// <param name="angleSize">The size of the arc, in degrees</param>
		public static void DrawArc( Vector2 center, Vector2 radius, float angle, float angleSize )
		{
			var rect = new Rect( center - radius, radius * 2.0f );
			angle = angle - 90;
			angle = angle * -16;
			angleSize = angleSize * -16;
			angle = angle - angleSize * 0.5f;

			Current.drawArc( rect, angle, angleSize );
		}

		/// <summary>
		/// Draws a pie. Angles are clockwise, 0 is north.
		/// </summary>
		/// <param name="center">The center of the circle</param>
		/// <param name="radius">The radius of the circle</param>
		/// <param name="angle">The center of the pie, in degrees</param>
		/// <param name="angleSize">The size of the pie, in degrees</param>
		public static void DrawPie( Vector2 center, Vector2 radius, float angle, float angleSize )
		{
			var rect = new Rect( center - radius, radius * 2.0f );
			angle = angle - 90;
			angle = angle * -16;
			angleSize = angleSize * -16;
			angle = angle - angleSize * 0.5f;

			Current.drawPie( rect, angle, angleSize );
		}

		public static void DrawSquare( Vector2 position, Vector2 scale )
		{
			DrawRect( new Rect( position - scale * 0.5f, scale ) );
		}

		static List<Vector3> pointBuffer = new();

		public unsafe static void DrawPolygon( IEnumerable<Vector2> points )
		{
			pointBuffer.Clear();
			pointBuffer.AddRange( points.Select( x => (Vector3)x ) );

			var a = pointBuffer.ToArray();
			fixed ( Vector3* ptr = a )
			{
				Current.DrawPoly( ptr, points.Count() );
			}
		}

		public unsafe static void DrawLine( IEnumerable<Vector2> points )
		{
			pointBuffer.Clear();
			pointBuffer.AddRange( points.Select( x => (Vector3)x ) );

			var a = pointBuffer.ToArray();
			fixed ( Vector3* ptr = a )
			{
				Current.DrawPolyLine( ptr, points.Count() );
			}
		}

		public unsafe static void DrawPoints( IEnumerable<Vector2> points )
		{
			pointBuffer.Clear();
			pointBuffer.AddRange( points.Select( x => (Vector3)x ) );

			var a = pointBuffer.ToArray();
			fixed ( Vector3* ptr = a )
			{
				Current.DrawPoints( ptr, points.Count() );
			}
		}

		public static void DrawPolygon( params Vector2[] points )
		{
			DrawPolygon( points.AsEnumerable() );
		}

		public static void DrawArrow( Vector2 p1, Vector2 p2, float width )
		{
			var delta = (p1 - p2);
			var left = delta.Perpendicular.Normal;

			var a = p1 + left * width * 0.5f;
			var b = p1 - left * width * 0.5f;
			var c = p2;

			DrawPolygon( a, b, c );
		}

		public static Rect DrawText( in Vector2 position, string text )
		{
			return DrawText( new Rect( position ) { Width = 1000, Height = 1000 }, text, TextFlag.Left | TextFlag.Top );
		}

		public static void DrawLine( in Vector2 from, in Vector2 to )
		{
			Current.drawLine( from, to );
		}

		public static Rect DrawText( in Rect position, string text, TextFlag flags = TextFlag.Center )
		{
			Current.drawText( position, (int)flags, text, out var rect );
			return rect.Rect;
		}

		/// <summary>
		/// Adds required ellipses to a string if it doesn't fit within the width
		/// </summary>
		public static string GetElidedText( string text, float width, ElideMode mode = ElideMode.Right, TextFlag flags = TextFlag.Center )
		{
			return WidgetUtil.ElidedText( text, (int)width, (int)mode, (int)flags );
		}

		public static Rect MeasureText( in Rect position, string text, TextFlag flags = TextFlag.Center )
		{
			if ( Current.IsNull )
			{
				var v = WidgetUtil.MeasureText( position, (int)flags, text );
				return new Rect( v.x, v.y, v.z, v.w );
			}

			return Current.MeasureText( position, (int)flags, text ).Rect;
		}

		public static Vector2 MeasureText( string text )
		{
			var position = new Rect( 0, 4096 );

			if ( Current.IsNull )
			{
				var v = WidgetUtil.MeasureText( position, (int)TextFlag.LeftTop, text );
				return new Vector2( v.z, v.w );
			}

			return Current.MeasureText( position, (int)TextFlag.LeftTop, text ).Rect.Size;
		}


		public static void SetFont( string name, float size = 8, int weight = 400, bool italic = false, bool sizeInPixels = false )
		{
			// alex: Qt doesn't like applying font weights properly - so we have to
			// work around that
			weight = 400 + (weight / 10);

			fontInfo.FontName = name;
			fontInfo.FontSize = size;
			fontInfo.FontWeight = weight;

			WidgetUtil.PaintSetFont( Current, name, (int)size, weight, italic, sizeInPixels );
		}

		public static void SetDefaultFont( float size = 8, int weight = 400, bool italic = false, bool sizeInPixels = false )
		{
			SetFont( Theme.DefaultFont, size, weight, italic, sizeInPixels );
		}

		public static void SetHeadingFont( float size = 15, int weight = 400, bool italic = false, bool sizeInPixels = false )
		{
			SetFont( Theme.DefaultFont, size, weight, italic, sizeInPixels );
		}

		public static void ClearPen()
		{
			Pen = Color.Transparent;
		}

		public static void ClearBrush()
		{
			Current.clearBrush();
		}

		static float _dpiScale = 1.0f;
		static Color _pen = Color.White;

		public static Color Pen
		{
			get => _pen;
			set
			{
				if ( _pen == value ) return;
				_pen = value;
				SetPen();
			}
		}

		static float _penSize = 0.0f;

		public static float PenSize
		{
			get => _penSize;
			set
			{
				if ( _penSize == value ) return;
				_penSize = value;
				SetPen();
			}
		}

		static PenStyle _penStyle = PenStyle.Solid;

		public static PenStyle PenStyle
		{
			get => _penStyle;
			set
			{
				if ( _penStyle == value ) return;
				_penStyle = value;
				SetPen();
			}
		}

		static void SetPen()
		{
			if ( Pen.a <= 0.001f )
			{
				Current.clearPen();
				return;
			}

			Current.setPen( Pen, PenSize, (int)PenStyle );
		}

		/// <summary>
		/// Set the pen and font style from a style
		/// </summary>
		public static void SetFont( in Styles style )
		{
			Paint.SetBrushAndPen( Color.Transparent, style.FontColor ?? Color.White );
			Paint.SetFont( style.FontFamily ?? Theme.DefaultFont, style.FontSize?.GetPixels( 0 ) ?? 8, style.FontWeight ?? 400 );
		}

		/// <summary>
		/// Draw a rectangle using the background of a style
		/// </summary>
		public static void Rect( in Styles styles, in Rect rect )
		{
			Paint.Antialiasing = true;

			if ( styles.HasBorder )
			{
				Paint.SetPen( styles.BorderTopColor ?? Color.White, styles.BorderTopWidth?.GetPixels( 1024 ) ?? 0 );
			}
			else
			{
				Paint.ClearPen();
			}

			if ( styles.BackgroundColor?.a > 0 )
			{
				var bgRect = rect.Shrink( Paint.PenSize * 0.5f );
				Paint.SetBrush( styles.BackgroundColor.Value );
				Paint.DrawRect( bgRect );
			}
		}

		public static void SetPen( in Color color, in float size = 0.0f, in PenStyle style = PenStyle.Solid )
		{
			ThreadSafe.AssertIsMainThread();

			if ( color.a <= 0 )
			{
				ClearPen();
				return;
			}

			Pen = color;
			PenSize = size;
			PenStyle = style;
		}

		public static void SetBrush( in Color color )
		{
			ThreadSafe.AssertIsMainThread();

			if ( color.a <= 0 )
			{
				ClearBrush();
				return;
			}

			Current.setBrush( color );
		}

		public static void SetBrushAndPen( Color brushColor, Color penColor, float penSize = 1.0f, PenStyle style = PenStyle.Solid )
		{
			if ( _painter.IsNull )
				return;

			ThreadSafe.AssertIsMainThread();
			SetBrush( brushColor );
			SetPen( penColor, penSize, style );
		}

		public static void SetBrushAndPen( Color brushColor )
		{
			ThreadSafe.AssertIsMainThread();
			SetBrush( brushColor );
			ClearPen();
		}

		public static void SetBrushLinear( Vector2 a_pos, Vector2 b_pos, Color a_color, Color b_color )
		{
			Current.setBrushLinear( a_pos, b_pos, 0, a_color, 1, b_color );
		}

		public static void SetBrushRadial( Vector2 center, float radius, Color a_color, Color b_color )
		{
			Current.setBrushRadial( center, radius, 0, a_color, 1, b_color );
		}

		public static void SetBrushRadial( Vector2 center, float radius, float a, Color a_color, float b, Color b_color )
		{
			Current.setBrushRadial( center, radius, a, a_color, b, b_color );
		}

		// clear this on png jpg etc file change?
		static Dictionary<string, Pixmap> Images = new();

		public static Pixmap LoadImage( string filename )
		{
			if ( string.IsNullOrWhiteSpace( filename ) )
				filename = "missing.png";

			if ( Images.TryGetValue( filename, out var pixmap ) )
				return pixmap;

			if ( filename.StartsWith( "https://" ) || filename.StartsWith( "http://" ) )
			{
				var t = DownloadToPixmap( filename );
				if ( t.IsCompletedSuccessfully )
				{
					pixmap = t.Result;
				}
				else
				{
					pixmap = new Pixmap( 1, 1 );
					pixmap.Clear( Color.Transparent );
				}

				Images[filename] = pixmap;
			}
			else
			{
				pixmap = Pixmap.FromFile( filename );
				Images[filename] = pixmap;
			}

			return pixmap;
		}

		public static Pixmap LoadImage( string filename, int x, int y )
		{
			var cachedName = $"{filename};{x};{y}";

			// Try to get saved resized one
			if ( Images.TryGetValue( cachedName, out var pixmap ) )
				return pixmap;

			// Get the unresized version
			pixmap = LoadImage( filename );
			if ( x == 0 || y == 0 ) return pixmap;
			if ( pixmap.Width <= 1 || pixmap.Height <= 1 ) return pixmap;

			// If it was bigger than 1x1 (is a temporary), resize it and save in cache
			pixmap = pixmap.Resize( x, y );
			Images[cachedName] = pixmap;
			return pixmap;
		}

		static async ValueTask<Pixmap> DownloadToPixmap( string filename )
		{
			var crcName = Sandbox.Utility.Crc64.FromString( filename.ToLower() ).ToString( "x" );
			var targetPath = FileSystem.WebCache.GetFullPath( "/" ) + $"/{crcName}";

			if ( !FileSystem.WebCache.FileExists( $"/{crcName}" ) )
			{
				await EditorUtility.DownloadAsync( filename, targetPath, default );

				// We downloaded an image successfully, lets redraw the window next frame so it gets drawn
				EditorUtility.RedrawActiveWindow();
			}

			var pixmap = Pixmap.FromFile( targetPath );
			Images[filename] = pixmap;
			return pixmap;
		}

		public static void SetBrush( string image )
		{
			var pixmap = LoadImage( image );

			if ( pixmap != null )
				SetBrush( pixmap );
		}

		public static void SetBrush( Pixmap pixmap )
		{
			Assert.NotNull( pixmap );
			Assert.True( pixmap.ptr.IsValid );

			Current.setBrush( pixmap.ptr );
		}

		public static bool HasSelected => state.Contains( StateFlag.Selected );
		public static bool HasMouseOver => state.Contains( StateFlag.MouseOver );
		public static bool HasPressed => state.Contains( StateFlag.Sunken );
		public static bool HasFocus => state.Contains( StateFlag.HasFocus );
		public static bool HasEnabled => state.Contains( StateFlag.Enabled );

		public static void SetFlags( bool selected, bool mouseOver, bool pressed, bool focused, bool enabled )
		{
			state = 0;
			if ( selected ) state |= StateFlag.Selected;
			if ( mouseOver ) state |= StateFlag.MouseOver;
			if ( pressed ) state |= StateFlag.Sunken;
			if ( focused ) state |= StateFlag.HasFocus;
			if ( enabled ) state |= StateFlag.Enabled;
		}


		internal static IDisposable Start( QPainter e, StateFlag flags = StateFlag.None, float scale = 1.0f )
		{
			ThreadSafe.AssertIsMainThread();

			var oldPtr = _painter;
			var oldstate = state;
			var oldPen = _pen;
			var oldDpiScale = _dpiScale;

			_painter = e;
			state = flags;
			_pen = Color.Red;
			_dpiScale = scale;

			fontInfo.Reset();

			return DisposeAction.Create( () =>
			{
				ThreadSafe.AssertIsMainThread();

				_painter = oldPtr;
				state = oldstate;
				_pen = oldPen;
				_dpiScale = oldDpiScale;
			} );
		}

		public static Rect DrawIcon( Rect rect, string iconName, float pixelHeight, TextFlag alignment = TextFlag.Center )
		{
			if ( string.IsNullOrEmpty( iconName ) )
				return rect;

			if ( iconName.Contains( '.' ) )
			{
				var innerRect = rect.Align( new Vector2( pixelHeight, pixelHeight ), alignment );

				Draw( innerRect, iconName, Pen.a );
				return innerRect;
			}

			// save and restore the font
			var of = fontInfo;

			try
			{
				Paint.SetFont( "Material Icons", pixelHeight, 400, sizeInPixels: true );
				return Paint.DrawText( rect, iconName?.ToLower(), alignment );
			}
			finally
			{
				Paint.SetFont( of.FontName, of.FontSize, of.FontWeight );
			}
		}

		public static void Draw( Rect r, Pixmap pixmap, float alpha = 1.0f )
		{
			var src = new Rect( 0, 0, pixmap.Width, pixmap.Height );
			Current.drawPixmap( r, pixmap.ptr, src, alpha );
		}

		public static void Draw( Rect r, string image, float alpha = 1.0f )
		{
			// find the image, and resize it to this size to make it nice
			var pixmap = LoadImage( image, (int)(r.Size.x * _dpiScale), (int)(r.Size.y * _dpiScale) );

			Draw( r, pixmap, alpha );
		}

		public static IDisposable ToPixmap( Pixmap pixmap )
		{
			ThreadSafe.AssertIsMainThread();

			var oldPainter = _painter;
			var oldstate = state;
			var oldPen = _pen;
			var oldDpiScale = _dpiScale;

			var createdPainter = QPainter.Create( pixmap.ptr );

			_painter = createdPainter;
			LocalRect = new Rect( 0, pixmap.Size );

			state = StateFlag.None;
			_pen = Color.Red;
			_dpiScale = 1;

			return DisposeAction.Create( () =>
			{
				ThreadSafe.AssertIsMainThread();

				createdPainter.DeleteThis();

				_painter = oldPainter;
				state = oldstate;
				_pen = oldPen;
				_dpiScale = oldDpiScale;
			} );
		}

		public static RenderMode RenderMode
		{
			set
			{
				Current.setCompositionMode( (CompositionMode)value );
			}
		}

		public static Rect DrawTextBox( Rect position, string text, Color textColor, Margin padding, float borderRadius, TextFlag flag )
		{
			// invalid
			if ( position.Left >= position.Right )
				return position;

			var textRect = Paint.MeasureText( position, text, flag );

			Paint.DrawRect( textRect.Grow( padding ), borderRadius );

			Paint.SetPen( textColor );
			Paint.DrawText( position, text, flag );

			return textRect;
		}
	}

	internal enum StateFlag
	{
		None = 0x00000000,
		Enabled = 0x00000001,
		Raised = 0x00000002,
		Sunken = 0x00000004,
		Off = 0x00000008,
		NoChange = 0x00000010,
		On = 0x00000020,
		DownArrow = 0x00000040,
		Horizontal = 0x00000080,
		HasFocus = 0x00000100,
		Top = 0x00000200,
		Bottom = 0x00000400,
		FocusAtBorder = 0x00000800,
		AutoRaise = 0x00001000,
		MouseOver = 0x00002000,
		UpArrow = 0x00004000,
		Selected = 0x00008000,
		Active = 0x00010000,
		Window = 0x00020000,
		Open = 0x00040000,
		Children = 0x00080000,
		Item = 0x00100000,
		Sibling = 0x00200000,
		Editing = 0x00400000,
		KeyboardFocusChange = 0x00800000,
		HasEditFocus = 0x01000000,
		ReadOnly = 0x02000000,
		Small = 0x04000000,
		Mini = 0x08000000
	};

}

internal enum CompositionMode
{
	CompositionMode_SourceOver,
	CompositionMode_DestinationOver,
	CompositionMode_Clear,
	CompositionMode_Source,
	CompositionMode_Destination,
	CompositionMode_SourceIn,
	CompositionMode_DestinationIn,
	CompositionMode_SourceOut,
	CompositionMode_DestinationOut,
	CompositionMode_SourceAtop,
	CompositionMode_DestinationAtop,
	CompositionMode_Xor,

	//svg 1.2 blend modes
	CompositionMode_Plus,
	CompositionMode_Multiply,
	CompositionMode_Screen,
	CompositionMode_Overlay,
	CompositionMode_Darken,
	CompositionMode_Lighten,
	CompositionMode_ColorDodge,
	CompositionMode_ColorBurn,
	CompositionMode_HardLight,
	CompositionMode_SoftLight,
	CompositionMode_Difference,
	CompositionMode_Exclusion,

	// ROPs
	RasterOp_SourceOrDestination,
	RasterOp_SourceAndDestination,
	RasterOp_SourceXorDestination,
	RasterOp_NotSourceAndNotDestination,
	RasterOp_NotSourceOrNotDestination,
	RasterOp_NotSourceXorDestination,
	RasterOp_NotSource,
	RasterOp_NotSourceAndDestination,
	RasterOp_SourceAndNotDestination,
	RasterOp_NotSourceOrDestination,
	RasterOp_SourceOrNotDestination,
	RasterOp_ClearDestination,
	RasterOp_SetDestination,
	RasterOp_NotDestination
}
