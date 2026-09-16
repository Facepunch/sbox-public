using Sandbox.UI;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Intersects subsequent drawing with a rectangle, optionally rounded like Painter.Rect.
	/// Captures the current Transform; later transform changes do not move this clip.
	/// Nested clips intersect with each other and the panel's CSS clip. Scope() restores the previous clip.
	/// Empty rectangles or a collapsed transform clip all drawing.
	/// </summary>
	public void Clip( Rect rect, CornerRadii corners = default ) => ClipRect( rect, corners.Resolve( rect ) );

	void ClipRect( Rect rect, BorderRadii radii )
	{
		if ( !rect.Position.IsFinite || !float.IsFinite( rect.Width ) || !float.IsFinite( rect.Height )
			|| !float.IsFinite( rect.Right ) || !float.IsFinite( rect.Bottom ) )
			throw new ArgumentOutOfRangeException( nameof( rect ), "Clip bounds must be finite." );

		var buffer = ActiveContext;
		var matrix = Matrix.Identity;
		if ( buffer.State.HasArea ) matrix = (buffer.State.Transform * buffer.BaseTransform).Inverted;
		else rect = default;
		rect.Size = Vector2.Max( rect.Size, Vector2.Zero );
		var clips = buffer.Batcher.DrawClips;
		clips.Add( new Painter.ClipEntry( rect, radii, matrix, buffer.State.ClipIndex ) );
		buffer.State.ClipIndex = clips.Count - 1;
	}

	/// <summary>
	/// A clip captured in drawing coordinates. Parent indexes an earlier clip in the owning writer.
	/// </summary>
	internal readonly record struct ClipEntry( Rect Rect, BorderRadii Radii, Matrix Matrix, int Parent );

	/// <summary>
	/// Inset subsequent fills without clipping their borders. Insets are left, top, right, bottom in pixels. Replaces a fill mask; restored by Scope().
	/// </summary>
	internal void ClipFill( Vector4 insets )
	{
		for ( int i = 0; i < 4; i++ )
			if ( !float.IsFinite( insets[i] ) || insets[i] < 0 ) throw new ArgumentOutOfRangeException( nameof( insets ) );
		ActiveContext.State.FillInsets = insets;
		ActiveContext.State.FillMask = null;
	}

	/// <summary>
	/// Mask subsequent fills with an image's alpha in drawing coordinates, leaving borders intact. Replaces fill insets; restored by Scope().
	/// </summary>
	internal void MaskFill( Texture texture, Rect rect )
	{
		ArgumentNullException.ThrowIfNull( texture );
		if ( !ValidBounds( rect ) ) throw new ArgumentOutOfRangeException( nameof( rect ) );
		ActiveContext.State.FillMask = texture;
		ActiveContext.State.FillMaskRect = rect;
		ActiveContext.State.FillInsets = default;
	}
}
