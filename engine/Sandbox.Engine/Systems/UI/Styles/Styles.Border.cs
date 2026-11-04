using Sandbox.Rendering;

namespace Sandbox.UI;

public partial class Styles
{
	// Keep authored widths so switching none/hidden back to a visible style restores the width.
	internal Length? UsedBorderLeftWidth => UsedBorderWidth( BorderLeftWidth );
	internal Length? UsedBorderTopWidth => UsedBorderWidth( BorderTopWidth );
	internal Length? UsedBorderRightWidth => UsedBorderWidth( BorderRightWidth );
	internal Length? UsedBorderBottomWidth => UsedBorderWidth( BorderBottomWidth );

	Length? UsedBorderWidth( Length? width )
		=> BorderStyle is Sandbox.BorderStyle.None or Sandbox.BorderStyle.Hidden ? 0 : width;

	bool SetBorderStyle( string value )
	{
		var p = new Parse( value ).SkipWhitespaceAndNewlines();
		if ( !p.TryReadLineStyle( out var word ) || !p.SkipWhitespaceAndNewlines().IsEnd ) return false;
		BorderStyle = Enum.Parse<BorderStyle>( word, true );
		return true;
	}

	/// <summary>
	/// Border widths in pixels as left, top, right, bottom.
	/// </summary>
	internal Vector4 GetBorderWidths( float size )
	{
		return new Vector4(
			UsedBorderLeftWidth.Value.GetPixels( size ),
			UsedBorderTopWidth.Value.GetPixels( size ),
			UsedBorderRightWidth.Value.GetPixels( size ),
			UsedBorderBottomWidth.Value.GetPixels( size )
		);
	}

	/// <summary>
	/// Resolve a style's border radii against a border box. Percentages resolve against the box
	/// width for the horizontal radius and the height for the vertical one. Radii that would
	/// overlap along a side are all scaled down by the same factor, as CSS does, so corners keep
	/// their shape.
	/// </summary>
	internal BorderRadii GetBorderRadii( in Rect rect )
	{
		var r = new BorderRadii
		{
			TopLeft = ResolveRadius( BorderTopLeftRadius, BorderTopLeftRadiusV, rect ),
			TopRight = ResolveRadius( BorderTopRightRadius, BorderTopRightRadiusV, rect ),
			BottomLeft = ResolveRadius( BorderBottomLeftRadius, BorderBottomLeftRadiusV, rect ),
			BottomRight = ResolveRadius( BorderBottomRightRadius, BorderBottomRightRadiusV, rect ),
		};

		return r.Clamped( rect.Width, rect.Height );
	}

	// A corner with no vertical radius of its own is a circle
	static Vector2 ResolveRadius( Length? horizontal, Length? vertical, in Rect rect )
	{
		if ( horizontal is not Length h ) return Vector2.Zero;

		var x = MathF.Max( 0, h.GetPixels( rect.Width ) );
		var y = MathF.Max( 0, (vertical ?? h).GetPixels( rect.Height ) );
		return new Vector2( x, y );
	}
}
