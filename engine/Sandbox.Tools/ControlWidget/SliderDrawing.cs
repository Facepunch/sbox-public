using System;

namespace Editor;

/// <summary>
/// Shared dimensions and painting for single-value and interval sliders.
/// </summary>
public static class SliderDrawing
{
	/// <summary>
	/// Width reserved for a slider's numeric entry.
	/// </summary>
	public const float ValueWidth = 60;

	/// <summary>
	/// Width of a slider handle, also used when mapping values to track positions.
	/// </summary>
	public const float HandleWidth = 7;

	/// <summary>
	/// Height of a slider handle.
	/// </summary>
	public const float HandleHeight = HandleWidth;

	/// <summary>
	/// Height of the track and its highlighted interval.
	/// </summary>
	public const float TrackHeight = 2;

	/// <summary>
	/// Draws the track and selected interval. Endpoint positions are measured from the left of rect,
	/// at the centers of the handles.
	/// </summary>
	public static void DrawTrack( Rect rect, float start, float end, Color highlight, bool disabled = false )
	{
		var top = rect.Top + (rect.Height - TrackHeight) * 0.5f;
		var color = highlight.WithAlpha( disabled ? 0.3f : 0.8f );

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( color.WithAlphaMultiplied( 0.25f ) );
		Paint.DrawRect( new Rect( rect.Left + HandleWidth * 0.5f, top, rect.Width - HandleWidth, TrackHeight ), TrackHeight * 0.5f );
		Paint.SetBrush( color );
		Paint.DrawRect( new Rect( rect.Left + MathF.Min( start, end ), top, MathF.Abs( end - start ), TrackHeight ), TrackHeight * 0.5f );
	}

	/// <summary>
	/// Draws a linear gradient across the entire track, without a selected interval.
	/// Color stop positions run from zero to one; endpoint colors extend to the track edges.
	/// </summary>
	internal static void DrawGradientTrack( Rect rect, Gradient gradient, bool disabled = false )
	{
		if ( gradient.Colors is not { Count: > 0 } colors ) return;

		var track = new Rect( rect.Left + HandleWidth * 0.5f, rect.Top + (rect.Height - TrackHeight) * 0.5f, rect.Width - HandleWidth, TrackHeight );
		if ( track.Width <= 0 ) return;

		// Adjacent segments share edges. Avoid antialiasing those edges into visible seams.
		Paint.Antialiasing = false;
		Paint.ClearPen();

		var previousPosition = 0.0f;
		var previousColor = colors[0].Value;

		foreach ( var stop in colors )
		{
			DrawSegment( previousPosition, stop.Time, previousColor, stop.Value );
			previousPosition = stop.Time;
			previousColor = stop.Value;
		}

		DrawSegment( previousPosition, 1.0f, previousColor, previousColor );
		Paint.Antialiasing = true;

		void DrawSegment( float start, float end, Color startColor, Color endColor )
		{
			if ( end <= start ) return;

			var left = track.Left + start * track.Width;
			var right = track.Left + end * track.Width;
			var opacity = disabled ? 0.3f : 1.0f;
			Paint.SetBrushLinear( new Vector2( left, track.Top ), new Vector2( right, track.Top ), startColor.WithAlphaMultiplied( opacity ), endColor.WithAlphaMultiplied( opacity ) );
			Paint.DrawRect( new Rect( left, track.Top, right - left, track.Height ) );
		}
	}

	/// <summary>
	/// Draws a handle centered at the given position, measured from the left of rect.
	/// Uses the owning widget's cursor and enabled state for hover highlighting.
	/// </summary>
	public static void DrawHandle( Widget widget, Rect rect, float position )
	{
		var handle = new Rect( rect.Left + position - HandleWidth * 0.5f, rect.Top + (rect.Height - HandleHeight) * 0.5f, HandleWidth, HandleHeight );
		var disabled = !widget.Enabled || widget.ReadOnly;
		var hovered = !disabled && widget.IsUnderMouse && handle.IsInside( widget.FromScreen( Application.CursorPosition ) );
		var color = hovered ? ControlWidget.ControlHighlightPrimary : Theme.TextControl;

		Paint.Antialiasing = true;
		Paint.SetBrush( color.WithAlpha( disabled ? 0.3f : 1 ) );
		Paint.ClearPen();
		Paint.DrawCircle( handle );
	}
}
