using SkiaSharp;

namespace Editor.Utility;

/// <summary>
/// Simple utility class to create placeholder thumbnail icons.
/// </summary>
public static class PlaceholderIcon
{
	// taken from sbox.web 
	static (Color start, Color end) GetGradientColors()
	{
		var i = Random.Shared.Next( 10 );

		// Use smoother, more harmonious color gradients
		return i switch
		{
			1 => new( "#374785", "#a8d0e6" ), // Blue gradient
			2 => new( "#fe5f55", "#f4d35e" ), // Soft coral to yellow
			3 => new( "#00a8e8", "#007ea7" ), // Aqua blue gradient
			4 => new( "#f76c6c", "#ffb3b3" ), // Light red to pink
			5 => new( "#2a9d8f", "#e9c46a" ), // Green to soft yellow
			6 => new( "#8d99ae", "#edf2f4" ), // Grey to light grey
			7 => new( "#ef476f", "#ffd166" ), // Pink to orange
			8 => new( "#06d6a0", "#118ab2" ), // Light green to blue
			9 => new( "#b56576", "#e56b6f" ), // Muted red to pink
			_ => new( "#ffbe0b", "#fb5607" ),  // Yellow to orange
		};
	}

	private static string GetInitials( string name )
	{
		// Normalize the name by replacing "_" and "-" with spaces
		var normalizedName = name.Replace( '_', ' ' ).Replace( '-', ' ' );

		// Split into words and take the first letter of each
		var words = normalizedName.Split( ' ', StringSplitOptions.RemoveEmptyEntries );

		if ( words.Length == 0 )
			return string.Empty;

		// Get initials from each word, limit to 3 characters
		var initials = string.Join( "", words.Take( 3 ).Select( w => char.ToUpper( w.First() ) ) );

		return initials;
	}

	/// <summary>
	/// Generates a placeholder icon at a set size, using text -- will be abbreviated to fit the image
	/// </summary>
	/// <param name="name"></param>
	/// <param name="size"></param>
	/// <param name="fontFamily"></param>
	/// <returns></returns>
	public static Pixmap Generate( string name, int size, string fontFamily = "Verdana" )
	{
		var initials = GetInitials( name );
		var (start, end) = GetGradientColors();

		using var bitmap = new Bitmap( size, size, false );

		bitmap.SetLinearGradient( 0, size, Gradient.FromColors( start, end ) );
		bitmap.DrawRect( bitmap.Rect );

		var typeface = SKTypeface.FromFamilyName( fontFamily, SKFontStyle.Bold );
		using var font = new SKFont( typeface, size * 0.15f );

		// Dynamically adjust font size based on text measurement
		float fontSize = size * 0.5f; // Start with a larger font size for initials
		font.Size = fontSize;
		var textBounds = new SKRect();
		font.MeasureText( initials, out textBounds );

		// Adjust font size until the text fits within the size constraints
		while ( (textBounds.Width > size * 0.8f || textBounds.Height > size * 0.8f) && fontSize > 10 )
		{
			fontSize -= 1;
			font.Size = fontSize;
			font.MeasureText( initials, out textBounds );
		}

		var outline = new TextRendering.Outline() { Enabled = true, Color = Color.Black.WithAlpha( 0.2f ), Size = 8 };
		bitmap.DrawText( new TextRendering.Scope( initials, Color.White, fontSize, fontFamily, 900 ) { Outline = outline }, bitmap.Rect, TextFlag.Center | TextFlag.DontClip );

		return Pixmap.FromBitmap( bitmap );
	}
}
