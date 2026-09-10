using System;
using System.IO;

namespace Sandbox.TextureLoader;

internal static class ImageDataUri
{
	private const string InlinePrefixPng = "data:image/png;base64,";
	private const string InlinePrefixJpeg = "data:image/jpeg;base64,";

	internal static bool IsAppropriate( string uri )
	{
		return uri.StartsWith( InlinePrefixPng, StringComparison.OrdinalIgnoreCase ) || uri.StartsWith( InlinePrefixJpeg, StringComparison.OrdinalIgnoreCase );
	}

	internal static Texture Load( string uri, bool warnOnMissing )
	{
		try
		{
			if ( TryParseDataImage( InlinePrefixPng, uri, out var data ) ||
				 TryParseDataImage( InlinePrefixJpeg, uri, out data ) )
			{
				using var ms = new MemoryStream( data );
				return Image.Load( ms, uri );
			}

			Log.Warning( $"{nameof( ImageDataUri )} does not support loading: {uri}" );
			return null;
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Couldn't Load from data URI: {uri} ({e.Message})" );
			return null;
		}
	}

	private static bool TryParseDataImage( string prefix, string image, out byte[] data )
	{
		if ( !image.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
		{
			data = null;
			return false;
		}

		var dataStr = image.Substring( prefix.Length );
		data = Convert.FromBase64String( dataStr );
		return true;
	}
}
