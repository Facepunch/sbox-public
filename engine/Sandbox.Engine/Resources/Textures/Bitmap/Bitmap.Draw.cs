using Sandbox.UI;
using SkiaSharp;

namespace Sandbox;

public partial class Bitmap
{
	/// <summary>
	/// Draws a rectangle using the current pen settings.
	/// </summary>
	/// <param name="rect">The rectangle to draw.</param>
	public void DrawRect( Rect rect )
	{
		var skRect = rect.ToSk();
		_canvas.DrawRect( skRect, GetPen() );
	}

	/// <summary>
	/// Draws a rectangle using the current pen settings.
	/// </summary>
	/// <param name="x">The x-coordinate of the top-left corner.</param>
	/// <param name="y">The y-coordinate of the top-left corner.</param>
	/// <param name="width">The width of the rectangle.</param>
	/// <param name="height">The height of the rectangle.</param>
	public void DrawRect( float x, float y, float width, float height )
	{
		DrawRect( new Rect( x, y, x + width, y + height ) );
	}

	/// <summary>
	/// Draws a rectangle using the current pen settings.
	/// </summary>
	public void DrawRoundRect( Rect rect, Margin margins )
	{
		using var rounded = new SKRoundRect( rect.ToSk() );
		rounded.SetRectRadii( rect.ToSk(), new[] { new SKPoint( margins.Left, margins.Left ), new SKPoint( margins.Top, margins.Top ), new SKPoint( margins.Right, margins.Right ), new SKPoint( margins.Bottom, margins.Bottom ) } );

		var skRect = rect.ToSk();
		_canvas.DrawRoundRect( rounded, GetPen() );
	}

	/// <summary>
	/// Draws a circle using the current pen settings.
	/// </summary>
	/// <param name="center">The center of the circle.</param>
	/// <param name="radius">The radius of the circle.</param>
	public void DrawCircle( Vector2 center, float radius )
	{
		_canvas.DrawCircle( center.ToSk(), radius, GetPen() );
	}

	/// <summary>
	/// Draws a circle using the current pen settings.
	/// </summary>
	/// <param name="x">The x-coordinate of the circle's center.</param>
	/// <param name="y">The y-coordinate of the circle's center.</param>
	/// <param name="radius">The radius of the circle.</param>
	public void DrawCircle( float x, float y, float radius )
	{
		DrawCircle( new Vector2( x, y ), radius );
	}

	/// <summary>
	/// Draws a polygon using the current pen settings.
	/// </summary>
	/// <param name="points">The points of the polygon.</param>
	public void DrawPolygon( Vector2[] points )
	{
		using var path = new SKPath();
		path.MoveTo( points[0].ToSk() );

		for ( int i = 1; i < points.Length; i++ )
		{
			path.LineTo( points[i].ToSk() );
		}

		path.Close();
		_canvas.DrawPath( path, GetPen() );
	}

	/// <summary>
	/// Draws an arc using the current pen settings.
	/// </summary>
	/// <param name="rect">The bounding rectangle of the arc.</param>
	/// <param name="startAngle">The starting angle of the arc, in degrees.</param>
	/// <param name="sweepAngle">The sweep angle of the arc, in degrees.</param>
	public void DrawArc( Rect rect, float startAngle, float sweepAngle )
	{
		_canvas.DrawArc( rect.ToSk(), startAngle, sweepAngle, false, GetPen() );
	}

	/// <summary>
	/// Draws an arc using the current pen settings, with an option to connect to the center.
	/// </summary>
	/// <param name="rect">The bounding rectangle of the arc.</param>
	/// <param name="startAngle">The starting angle of the arc, in degrees.</param>
	/// <param name="sweepAngle">The sweep angle of the arc, in degrees.</param>
	/// <param name="useCenter">If true, connects the arc endpoints to the center point, forming a pie shape.</param>
	public void DrawArc( Rect rect, float startAngle, float sweepAngle, bool useCenter )
	{
		_canvas.DrawArc( rect.ToSk(), startAngle, sweepAngle, useCenter, GetPen() );
	}

	/// <summary>
	/// Draws another bitmap onto this bitmap.
	/// </summary>
	/// <param name="bitmap">The bitmap to draw.</param>
	/// <param name="destRect">The destination rectangle for the drawn bitmap.</param>
	public void DrawBitmap( Bitmap bitmap, Rect destRect )
	{
		_canvas.DrawBitmap( bitmap._bitmap, destRect.ToSk(), GetPen() );
	}

	/// <summary>
	/// Draws a line using the current pen settings.
	/// </summary>
	/// <param name="start">The starting point of the line.</param>
	/// <param name="end">The ending point of the line.</param>
	public void DrawLine( Vector2 start, Vector2 end )
	{
		_canvas.DrawLine( start.ToSk(), end.ToSk(), GetPen() );
	}

	/// <summary>
	/// Draws connected lines through a series of points using the current pen settings.
	/// </summary>
	/// <param name="points">The points to connect with lines.</param>
	public void DrawLines( params Vector2[] points )
	{
		if ( points == null || points.Length < 2 )
			throw new ArgumentException( "At least two points are required to draw lines.", nameof( points ) );

		var pen = GetPen();

		using var path = new SKPath();
		path.MoveTo( points[0].ToSk() );

		for ( int i = 1; i < points.Length; i++ )
		{
			path.LineTo( points[i].ToSk() );
		}

		_canvas.DrawPath( path, pen );
	}
}
