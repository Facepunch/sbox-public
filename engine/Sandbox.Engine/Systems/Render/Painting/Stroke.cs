using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// A stroke on a shape's path. Defaults to centered, with half its width on either side.
/// Dimensions and dash phase are in drawing pixels. Defaults to no stroke.
/// </summary>
public readonly record struct Stroke
{
	/// <summary>
	/// Placement relative to a closed shape's boundary. Open paths always use Center.
	/// </summary>
	public enum StrokeAlignment
	{
		/// <summary>
		/// Half the width on each side of the boundary.
		/// </summary>
		Center,
		/// <summary>
		/// The full width inside the filled region.
		/// </summary>
		Inside,
		/// <summary>
		/// The full width outside the filled region.
		/// </summary>
		Outside
	}

	internal bool IsDisabled => Style is BorderStyle.None or BorderStyle.Hidden;

	/// <summary>
	/// Placement on closed shapes. Defaults to Center; ignored by open paths.
	/// Inside and outside strokes clip a double-width centered stroke to the selected side,
	/// including its dash caps and dots. Pattern distances follow the original boundary.
	/// </summary>
	public StrokeAlignment Alignment { get; init; }

	/// <summary>Returns a copy with the specified placement on closed shapes.</summary>
	public Stroke WithAlignment( StrokeAlignment alignment )
	{
		if ( !Enum.IsDefined( alignment ) ) throw new ArgumentOutOfRangeException( nameof( alignment ) );
		return this with { Alignment = alignment };
	}

	/// <summary>
	/// How an open stroke ends.
	/// </summary>
	public enum LineCap
	{
		/// <summary>
		/// Ends at the endpoint.
		/// </summary>
		Butt,

		/// <summary>
		/// Extends half the stroke width beyond the endpoint.
		/// </summary>
		Square,

		/// <summary>
		/// A semicircle extending half the stroke width beyond the endpoint.
		/// </summary>
		Round,

		/// <summary>
		/// A full-stroke-width triangle based at the endpoint, with its tip half a stroke width beyond it.
		/// </summary>
		Triangle,

		/// <summary>
		/// A flared triangle based at the endpoint, twice the stroke width across and one stroke width long.
		/// </summary>
		Arrow
	}

	/// <summary>
	/// How adjacent stroke segments meet.
	/// </summary>
	public enum LineJoin
	{
		/// <summary>
		/// Edges meet at their intersection, falling back to bevel beyond the miter limit.
		/// </summary>
		Miter,

		/// <summary>
		/// Cuts off the outer corner.
		/// </summary>
		Bevel,

		/// <summary>
		/// Rounds the outer corner.
		/// </summary>
		Round
	}

	/// <summary>
	/// A disabled stroke. Equivalent to the default value.
	/// </summary>
	public static Stroke None => default;

	/// <summary>
	/// Creates a solid stroke with butt caps and round joins. Width is in drawing pixels.
	/// </summary>
	public static Stroke Solid( Fill fill, float width = 1 ) => new( fill, width );

	/// <summary>
	/// Creates round dots with diameter equal to the stroke width. Dimensions are in drawing pixels.
	/// </summary>
	/// <param name="fill">Color, gradient or image used to paint the dots.</param>
	/// <param name="width">Diameter of each dot.</param>
	/// <param name="gap">Gap between dot edges. Closed paths fit whole dot periods around their perimeter.</param>
	/// <param name="offset">Phase along the path, in drawing pixels.</param>
	public static Stroke Dotted( Fill fill, float width = 1, float gap = 4, float offset = 0 )
		=> new( fill, width ) { Style = BorderStyle.Dotted, Gap = gap, Offset = offset };

	/// <summary>
	/// Creates a dashed stroke with butt caps and round joins. Dimensions are in drawing pixels.
	/// </summary>
	/// <param name="fill">Color, gradient or image used to paint the dashes.</param>
	/// <param name="width">Centered stroke width.</param>
	/// <param name="dashLength">Length of each dash along the path's centerline.</param>
	/// <param name="gap">Gap between dash runs before extending their caps.</param>
	/// <param name="offset">Phase along the path, in drawing pixels.</param>
	public static Stroke Dashed( Fill fill, float width = 1, float dashLength = 8, float gap = 4, float offset = 0 )
		=> new( fill, width ) { Style = BorderStyle.Dashed, DashLength = dashLength, Gap = gap, Offset = offset };

	/// <summary>
	/// Creates a disabled stroke with an 8px dash length and 4px gap ready for patterned use.
	/// </summary>
	public Stroke()
	{
		DashLength = 8;
		Gap = 4;
	}

	/// <summary>
	/// Creates a solid stroke with the specified caps, round joins and a miter limit of 4. Caps default to butt.
	/// </summary>
	public Stroke( Fill fill, float width = 1, LineCap cap = LineCap.Butt )
	{
		Fill = fill;
		Width = width;
		Cap = cap;
		Join = LineJoin.Round;
		MiterLimit = 4;
		DashLength = 8;
		Gap = 4;
	}

	/// <summary>
	/// Returns a copy with the specified caps at both ends of open paths and dash runs. Dots stay round.
	/// </summary>
	public Stroke WithCap( LineCap cap ) => this with { Cap = cap };

	/// <summary>
	/// Fill mapped across the whole undashed stroke's bounds, shared by all dashes and caps.
	/// </summary>
	public Fill Fill { get; init; }

	/// <summary>
	/// Width in drawing pixels, placed according to Alignment. Nonpositive or non-finite widths disable the stroke.
	/// </summary>
	public float Width { get; init; }

	/// <summary>
	/// Outward-facing caps at both ends of open paths and dash runs, without shortening heads on short runs.
	/// Closed paths have no endpoint caps; dots stay round.
	/// </summary>
	public LineCap Cap { get; init; }

	/// <summary>
	/// Joins between adjacent segments.
	/// </summary>
	public LineJoin Join { get; init; }

	/// <summary>
	/// Maximum miter length divided by half the width, before falling back to bevel. Clamped to at least 1; non-finite values use 4.
	/// </summary>
	public float MiterLimit { get; init; }

	/// <summary>
	/// Style and spacing along the path. Generic paths support solid, dashed and dotted strokes; decorative styles render on inside rectangles with solid-color paint and fall back to solid elsewhere. Pattern geometry is cached; its memory cost grows with the number of dashes or dots. Patterns that exceed GPU buffer capacity throw ArgumentOutOfRangeException.
	/// </summary>
	public BorderStyle Style { get; init; }

	/// <summary>
	/// Dash centerline length in drawing pixels. Nonpositive or non-finite values produce a solid stroke.
	/// </summary>
	public float DashLength { get; init; }

	/// <summary>
	/// Gap between dash runs or dot edges. Negative gaps become zero; non-finite gaps produce a solid stroke.
	/// </summary>
	public float Gap { get; init; }

	/// <summary>
	/// Phase along the path in drawing pixels. Wraps around the period; non-finite values become zero.
	/// </summary>
	public float Offset { get; init; }
}
