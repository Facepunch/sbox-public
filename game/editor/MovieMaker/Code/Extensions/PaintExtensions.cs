using Sandbox.MovieMaker;
using Sandbox.UI;

namespace Editor.MovieMaker;

#nullable enable

internal static class PaintExtensions
{
	/// <summary>
	/// A bookmark shape, pointing down. Reminds me of those things you move for snooker scores
	/// </summary>
	public static void PaintBookmarkDown( float x, float bottom, float width, float arrowheight, float totalheight )
	{
		Paint.DrawPolygon( new Vector2( x, bottom ), new Vector2( x + width, bottom - arrowheight ), new Vector2( x + width, bottom - totalheight ), new Vector2( x - width, bottom - totalheight ), new Vector2( x - width, bottom - arrowheight ) );
	}

	/// <summary>
	/// A bookmark shape, pointing up. Reminds me of those things you move for snooker scores
	/// </summary>
	public static void PaintBookmarkUp( float x, float top, float width, float arrowheight, float totalheight )
	{
		Paint.DrawPolygon( new Vector2( x, top ), new Vector2( x + width, top + arrowheight ), new Vector2( x + width, top + totalheight ), new Vector2( x - width, top + totalheight ), new Vector2( x - width, top + arrowheight ) );
	}

	/// <summary>
	/// A triangle shape
	/// </summary>
	public static void PaintTriangle( Vector2 center, Vector2 size )
	{
		var x = new Vector2( size.x * 0.5f, 0 );
		var y = new Vector2( 0, size.y * 0.5f );

		Paint.DrawPolygon( center - x, center - y, center + x, center + y );
	}

	public static Color PaintSelectColor( Color normal, Color hover, Color selected )
	{
		if ( Paint.HasSelected || Paint.HasFocus ) return selected;
		if ( Paint.HasMouseOver ) return hover;
		return normal;
	}

	[ThreadStatic] private static List<Vector2>? CurvePoints;

	public static void PaintCurve( Func<float, float> func, Rect rect, bool flipX, bool flipY )
	{
		var points = CurvePoints ??= new List<Vector2>();

		points.Clear();

		var x0 = flipX ? rect.Right : rect.Left;
		var x1 = flipX ? rect.Left : rect.Right;

		var y0 = flipY ? rect.Top : rect.Bottom;
		var y1 = flipY ? rect.Bottom : rect.Top;

		AddPoints( points, func, new Vector2( x0, y0 ), new Vector2( x1, y1 ), flipX ^ flipY );

		points.Add( new Vector2( x1, y0 ) );
		points.Add( new Vector2( x0, y0 ) );

		Paint.DrawPolygon( points );
	}

	public static void PaintMirroredCurve( Func<float, float> func, Rect rect, float curveHeight, bool flipX )
	{
		var points = CurvePoints ??= new List<Vector2>();

		points.Clear();

		var x0 = flipX ? rect.Right : rect.Left;
		var x1 = flipX ? rect.Left : rect.Right;

		AddPoints( points, func, new Vector2( x0, rect.Top + curveHeight ), new Vector2( x1, rect.Top ), false );
		AddPoints( points, func, new Vector2( x1, rect.Bottom ), new Vector2( x0, rect.Bottom - curveHeight ), true );

		Paint.DrawPolygon( points );
	}

	private static void AddPoints( List<Vector2> points, Func<float, float> func, Vector2 start, Vector2 end, bool flip )
	{
		var width = Math.Abs( end.x - start.x );
		var pointCount = (int)Math.Clamp( width / 4f, 2f, 32f );

		for ( var i = 0; i <= pointCount; ++i )
		{
			var t = (float)i / pointCount;
			var v = flip ? 1f - func( 1f - t ) : func( t );

			points.Add( start + new Vector2( t * (end.x - start.x), v * (end.y - start.y) ) );
		}
	}

	[SkipHotload]
	private static Pixmap? _filmStripImage;

	public static Pixmap FilmStripImage => _filmStripImage ??= GenerateFilmStripImage();

	public static void PaintFilmStrip( Rect rect, Color color )
	{
		if ( rect.Height > 30f )
		{
			rect = rect.Contain( new Vector2( rect.Width, 30f ) );
		}

		Paint.ClearPen();
		Paint.SetBrush( FilmStripImage );
		Paint.Translate( rect.TopLeft );
		Paint.DrawRect( new Rect( 0f, 0f, rect.Width, rect.Height ), 2 );
		Paint.Translate( -rect.TopLeft );

		Paint.RenderMode = RenderMode.Multiply;
		Paint.SetBrush( color );
		Paint.DrawRect( rect, 2 );

		Paint.RenderMode = RenderMode.Normal;
	}

	private static Pixmap GenerateFilmStripImage()
	{
		var image = new Pixmap( 3, 30 );

		image.Clear( Color.White );

		using ( Paint.ToPixmap( image ) )
		{
			Paint.SetBrushAndPen( Color.White.Darken( 0.5f ) );
			Paint.DrawRect( new Rect( 1f, 1f, 2f, 3f ) );
			Paint.DrawRect( new Rect( 1f, 30 - 4f, 2f, 3f ) );
		}

		return image;
	}

	public static int NextPowerOfTwo( this float value )
	{
		var po2 = 1;

		while ( po2 < value )
		{
			po2 <<= 1;
		}

		return po2;
	}

	public static int NearestPowerOfTwo( this float value ) =>
		NextPowerOfTwo( value * 0.75f );

	/// <summary>
	/// Workaround because GraphicsItem.SceneRect isn't actually the scene rect.
	/// </summary>
	public static Rect GetRealSceneRect( this GraphicsItem item )
	{
		var sceneMin = item.ToScene( 0f );
		var sceneMax = item.ToScene( item.Size );

		return new Rect( sceneMin, sceneMax - sceneMin );
	}

	/// <summary>
	/// Tints <paramref name="baseColor"/> in the same way that <paramref name="tintedColor"/> was tinted
	/// related to <paramref name="defaultColor"/>.
	/// </summary>
	public static Color TintRelative( this Color baseColor, Color defaultColor, Color tintedColor )
	{
		var diff = tintedColor - defaultColor;

		return baseColor + diff;
	}
}

public sealed class SmoothDeltaFloat
{
	private float _value;
	private float _velocity;

	public float SmoothTime { get; }

	public float Value
	{
		get => _value;
		set
		{
			_value = value;
			Target = value;
			_velocity = 0f;
		}
	}

	public float Velocity => _velocity;
	public float Target { get; set; }

	public SmoothDeltaFloat( float smoothTime, float initial = 0f )
	{
		SmoothTime = smoothTime;
		Value = Target = initial;
	}

	public bool Update( float delta )
	{
		if ( Math.Abs( _value - Target ) <= 0.001f )
		{
			var exact = _value.Equals( Target );

			_value = Target;
			return !exact;
		}

		_value = MathX.SmoothDamp( _value, Target, ref _velocity, SmoothTime, delta );
		return true;
	}
}
