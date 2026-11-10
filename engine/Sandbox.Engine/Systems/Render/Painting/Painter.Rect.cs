namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Draws a filled rectangle with independent inside border widths and colors, ordered left, top, right, bottom.
	/// Uses Fill for the background and Stroke.Style for the border, with CSS corner joins and dash/dot spacing.
	/// Stroke.Width, Stroke.Fill, Alignment, Cap and Join do not affect these explicit borders.
	/// Border widths must be finite and non-negative. Corners follow the same rules as Rect.
	/// </summary>
	public void Rect( Rect rect, Vector4 borderWidths, Color colorLeft, Color colorTop, Color colorRight, Color colorBottom, CornerRadii corners = default )
	{
		var border = ResolveBoxStroke( borderWidths, colorLeft, colorTop, colorRight, colorBottom, Stroke.Style );
		if ( !ValidBounds( rect ) ) return;
		var context = ActiveContext;
		if ( context.State.Fill.IsTransparent && !border.HasInk ) return;
		context.State.Fill.CreateDescriptor( rect, context, out var desc );
		desc.Radii = corners.Resolve( rect );
		desc.Stroke = border.WithAlphaMultiplied( context.InheritedOpacity );
		Add( context, desc );
	}

	// Shared by explicit rectangle borders and cached CSS boxes.
	internal static BoxStroke ResolveBoxStroke( Vector4 widths, Color left, Color top, Color right, Color bottom, BorderStyle style )
	{
		for ( int i = 0; i < 4; i++ )
			if ( !float.IsFinite( widths[i] ) || widths[i] < 0 ) throw new ArgumentOutOfRangeException( nameof( widths ) );
		return new BoxStroke
		{
			Size = style is BorderStyle.None or BorderStyle.Hidden ? default : widths,
			Style = style,
			ColorL = left,
			ColorT = top,
			ColorR = right,
			ColorB = bottom
		};
	}

	internal static bool TryGetBoxStroke( in Stroke stroke, out BoxStroke result )
	{
		result = default;
		if ( !HasStroke( stroke ) || stroke.Alignment != Stroke.StrokeAlignment.Inside
			|| stroke.Style is BorderStyle.Dashed or BorderStyle.Dotted
			|| !Stroke.GetFill( in stroke ).TryGetSolidColor( out var color ) ) return false;
		result = ResolveBoxStroke( new Vector4( stroke.Width ), color, color, color, color, stroke.Style );
		return true;
	}
}
