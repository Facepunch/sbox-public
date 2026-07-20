using Sandbox.UI;
using System.Text.Json.Serialization;

namespace Sandbox;

public partial class SpriteSheet
{
	/// <summary>
	/// Where a slice's pivot sits, as one of the nine usual spots or somewhere chosen by hand.
	/// </summary>
	public enum PivotAlignment
	{
		[Icon( "filter_center_focus" )] Center,
		[Icon( "north_west" )] TopLeft,
		[Icon( "north" )] Top,
		[Icon( "north_east" )] TopRight,
		[Icon( "west" )] Left,
		[Icon( "east" )] Right,
		[Icon( "south_west" )] BottomLeft,
		[Icon( "south" )] Bottom,
		[Icon( "south_east" )] BottomRight,
		[Icon( "edit_location" )] Custom
	}

	/// <summary>
	/// One rectangle cut out of the sheet, with the pivot it turns around.
	/// </summary>
	public class Slice
	{
		/// <summary>
		/// Stable identity for this slice, generated once and never reused.
		/// </summary>
		/// <remarks>
		/// Everything that points at a slice points at this, <b>not</b> the name and not the index.
		/// Renaming is a required step of rigging a character - parts get labelled <c>L_Thigh</c>,
		/// <c>R_Calf</c> and so on after they are cut - and re-slicing reorders things freely. If
		/// references were by name, the rename that makes a sheet usable would break every sprite
		/// already built from it.
		/// </remarks>
		[Hide]
		public Guid Id { get; set; } = Guid.NewGuid();

		/// <summary>
		/// What this slice is called. Names are for humans and for matching slices back up across a
		/// re-slice; they are not identity. Keep them unique with
		/// <see cref="SpriteSheet.GetUniqueName"/> so the list stays readable.
		/// </summary>
		[KeyProperty]
		public string Name { get; set; } = "slice";

		/// <summary>
		/// The area of the source image this slice covers, in pixels.
		/// <para>
		/// Image space: the origin is the <b>top-left</b> and Y increases <b>downwards</b>, matching
		/// how the existing spritesheet importer lays out its grid and how <c>Bitmap.Crop</c> reads
		/// its rect. Note this is the opposite of <see cref="Pivot"/> - see the warning there.
		/// </para>
		/// </summary>
		public Rect Rect { get; set; }

		/// <summary>
		/// Where the slice turns around, normalized within <see cref="Rect"/>. This is copied
		/// straight into <c>Sprite.Animation.Origin</c>, so it is stored in that property's units.
		/// </summary>
		/// <remarks>
		/// <b>Y points up here.</b> (0,0) is the bottom-left of the slice and (1,1) the top-right,
		/// which is Unity's convention and the one a default <c>SpriteRenderer</c> (Billboard =
		/// Always) actually renders. <see cref="Rect"/> is the other way up, so converting between
		/// the two needs a flip - use <see cref="PivotToImagePixels"/> rather than doing it by hand.
		/// <para>
		/// Worth knowing: the sprite shader's no-billboard path applies this offset with the opposite
		/// sign to its billboard paths, so a non-billboarded sprite mirrors its pivot vertically.
		/// That is pre-existing engine behaviour, not something this asset introduces.
		/// </para>
		/// </remarks>
		[Range( 0, 1 )]
		public Vector2 Pivot { get; set; } = new Vector2( 0.5f, 0.5f );

		/// <summary>
		/// Which of the nine standard spots the pivot is locked to. <see cref="PivotAlignment.Custom"/>
		/// means it was placed by hand and should be left alone.
		/// </summary>
		public PivotAlignment Alignment { get; set; } = PivotAlignment.Center;

		/// <summary>
		/// Edge widths for nine-slice scaling. Authored and saved, but nothing consumes it yet.
		/// </summary>
		public Margin Border { get; set; }

		/// <summary>
		/// The slice this one hangs off when the sheet is built into a rig - a calf's parent is a
		/// thigh. <see cref="Guid.Empty"/> means it is a root.
		/// </summary>
		/// <remarks>
		/// Kept on the sheet rather than worked out at build time so that a rig only has to be
		/// described once. Re-slicing, rebuilding after moving a pivot, or building the same character
		/// into a second scene all reuse it.
		/// </remarks>
		[Hide]
		public Guid ParentId { get; set; }

		/// <summary>
		/// The pivot as a position in source-image pixels, flipping Y from the pivot's bottom-up
		/// space into the image's top-down space.
		/// </summary>
		[JsonIgnore, Hide]
		public Vector2 PivotToImagePixels => new(
			Rect.Left + (Pivot.x * Rect.Width),
			Rect.Top + ((1f - Pivot.y) * Rect.Height) );

		/// <summary>
		/// The normalized pivot for one of the nine standard spots.
		/// </summary>
		/// <param name="alignment">The spot to resolve. <see cref="PivotAlignment.Custom"/> returns the centre.</param>
		public static Vector2 GetPivot( PivotAlignment alignment ) => alignment switch
		{
			PivotAlignment.TopLeft => new Vector2( 0f, 1f ),
			PivotAlignment.Top => new Vector2( 0.5f, 1f ),
			PivotAlignment.TopRight => new Vector2( 1f, 1f ),
			PivotAlignment.Left => new Vector2( 0f, 0.5f ),
			PivotAlignment.Right => new Vector2( 1f, 0.5f ),
			PivotAlignment.BottomLeft => new Vector2( 0f, 0f ),
			PivotAlignment.Bottom => new Vector2( 0.5f, 0f ),
			PivotAlignment.BottomRight => new Vector2( 1f, 0f ),
			_ => new Vector2( 0.5f, 0.5f ),
		};

		/// <summary>
		/// Move the pivot onto one of the nine standard spots. Passing
		/// <see cref="PivotAlignment.Custom"/> records that it was placed by hand and leaves it where
		/// it is.
		/// </summary>
		/// <param name="alignment">The spot to snap to</param>
		public void SetAlignment( PivotAlignment alignment )
		{
			Alignment = alignment;
			if ( alignment == PivotAlignment.Custom ) return;

			Pivot = GetPivot( alignment );
		}

		/// <summary>
		/// Place the pivot by hand, given a position in source-image pixels. Flips Y back out of
		/// image space and marks the slice as custom.
		/// </summary>
		/// <param name="imagePixels">The desired pivot position, in source-image pixels</param>
		public void SetPivotFromImagePixels( Vector2 imagePixels )
		{
			if ( Rect.Width <= 0 || Rect.Height <= 0 ) return;

			Pivot = new Vector2(
				(imagePixels.x - Rect.Left) / Rect.Width,
				1f - ((imagePixels.y - Rect.Top) / Rect.Height) );

			Alignment = PivotAlignment.Custom;
		}

		public override string ToString() => Name;
	}
}
