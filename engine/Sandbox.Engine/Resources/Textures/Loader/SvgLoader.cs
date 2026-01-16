using System.IO;
using System.Web;

namespace Sandbox.TextureLoader;

internal static class SvgLoader
{

	internal static bool IsAppropriate( string path )
	{
		var split = path.Split( '?' )[0];
		var extension = Path.GetExtension( split );

		return extension == ".svg";
	}

	internal static Texture Load( BaseFileSystem filesystem, string url, bool warnOnMissing )
	{
		var split = url.Split( '?' );
		int? width = null;
		int? height = null;
		Color? color = null;

		if ( split.Length > 1 )
		{
			var query = HttpUtility.ParseQueryString( split[1] );

			if ( int.TryParse( query.Get( "w" ), out var parsedWidth ) ) width = parsedWidth;
			if ( int.TryParse( query.Get( "h" ), out var parsedHeight ) ) height = parsedHeight;

			var qColor = query.Get( "color" );
			if ( qColor != null )
			{
				color = Color.Parse( qColor );
			}
		}
		try
		{
			var filePath = split[0];
			var svg = filesystem.ReadAllText( filePath );

			if ( string.IsNullOrEmpty( svg ) )
			{
				if ( warnOnMissing )
				{
					Log.Warning( $"Error loading SVG '{filePath}': file does not exist or is empty." );
				}
				return null;
			}

			var tex = Texture.CreateFromSvgSource( svg, width, height, color );
			tex?.SetIdFromResourcePath( url );

			return tex;
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error when painting SVG: {url} ({e.Message})" );
			return null;
		}
	}
}
