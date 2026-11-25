namespace Sandbox;

/// <summary>
/// SVG text comes from game code and other players. The renderer fetches any image href it ends up with,
/// files, http and SMB shares included, and an href can come from an attribute or from CSS. So the parsed
/// document is checked, and only embedded raster images survive.
/// </summary>
internal static class SvgSanitizer
{
	static SvgSanitizer()
	{
		global::Svg.SvgDocument.DisableDtdProcessing = true;
		global::Svg.SvgDocument.ResolveExternalXmlEntites = global::Svg.ExternalType.None;
		global::Svg.SvgDocument.ResolveExternalElements = global::Svg.ExternalType.None;
		global::Svg.SvgDocument.ResolveExternalImages = global::Svg.ExternalType.None;
	}

	static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47];
	static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];

	internal static global::Svg.SvgDocument Parse( string contents )
	{
		var document = global::Svg.SvgDocument.FromSvg<global::Svg.SvgDocument>( contents.Trim() );

		foreach ( var element in document.Descendants() )
		{
			if ( element is global::Svg.SvgImage image && !IsEmbeddedImage( image.Href ) )
				image.Href = null;
			else if ( element is global::Svg.FilterEffects.SvgImage filterImage && !IsEmbeddedImage( filterImage.Href ) )
				filterImage.Href = null;
		}

		return document;
	}

	/// <summary>
	/// A base64 data uri holding a real png, jpeg, gif or webp. The renderer loads a gzipped payload
	/// as a nested svg whatever the mime type says, so the bytes are checked, not the prefix.
	/// </summary>
	internal static bool IsEmbeddedImage( string href )
	{
		if ( href is null || !href.StartsWith( "data:image/", StringComparison.OrdinalIgnoreCase ) )
			return false;

		var comma = href.IndexOf( ',' );
		if ( comma < 0 || !href.AsSpan( 0, comma ).EndsWith( ";base64", StringComparison.OrdinalIgnoreCase ) )
			return false;

		var payload = href.AsSpan( comma + 1 );
		var bytes = new byte[(payload.Length * 3 + 3) / 4];
		if ( !Convert.TryFromBase64Chars( payload, bytes, out var length ) )
			return false;

		ReadOnlySpan<byte> data = bytes.AsSpan( 0, length );

		return data.StartsWith( Png )
			|| data.StartsWith( Jpeg )
			|| data.StartsWith( "GIF8"u8 )
			|| (data.Length >= 12 && data.StartsWith( "RIFF"u8 ) && data.Slice( 8, 4 ).SequenceEqual( "WEBP"u8 ));
	}
}
