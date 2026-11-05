using Sandbox.Engine;
using Sandbox;

namespace Microsoft.AspNetCore.Components;

public class RouteAttribute : System.Attribute
{
	/// <summary>
	/// The full url of this route (ie "/home/section/page")
	/// </summary>
	public string Url { get; }

	/// <summary>
	/// The url split into parts (ie "home" "section" "page" )
	/// </summary>
	public string[] Parts { get; }

	public RouteAttribute( string url )
	{
		Url = url;
		Parts = Url.Split( '/', StringSplitOptions.RemoveEmptyEntries );
	}

	/// <summary>
	/// Given a URL, check out TypeLibrary and find a valid target
	/// </summary>
	public static (TypeDescription Type, RouteAttribute Attribute)? FindValidTarget( string url, string parentUrl ) => FindValidTarget( Game.TypeLibrary, url, parentUrl );

	internal static (TypeDescription Type, RouteAttribute Attribute)? FindValidTarget( Sandbox.Internal.TypeLibrary library, string url, string parentUrl )
	{
		//Log.Info( $"FindValidTarget [{url}] [{parentUrl}]" );

		var val = library.GetTypesWithAttribute<RouteAttribute>()
								.Where( x => x.Attribute.IsUrl( url ) && !x.Attribute.IsUrl( parentUrl ) )
								.OrderByDescending( x => x.Attribute.Url.Count( c => c == '*' ) )
								.FirstOrDefault();

		//Log.Info( $"Found [{val.Attribute?.Url}] [{val.Type}]" );

		if ( val.Type == null )
			return null;

		return val;
	}

	bool TestPart( string part, string ours )
	{
		// this is a variable
		if ( ours != null && ours.StartsWith( '{' ) && ours.EndsWith( '}' ) )
			return true;

		return part == ours;
	}

	/// <summary>
	/// True if this matches the passed in url.
	/// Queries are trimmed and ignored <c>( ?query=fff )</c>
	/// Variables are tested (but not type matched or anything)
	/// </summary>
	public bool IsUrl( string url )
	{
		if ( string.IsNullOrEmpty( url ) ) return false;

		if ( url.Contains( '?' ) )
		{
			url = url[..url.IndexOf( '?' )];
		}

		var a = url.Split( '/', StringSplitOptions.RemoveEmptyEntries );

		for ( int i = 0; i < Parts.Length || i < a.Length; i++ )
		{
			var left = i < a.Length ? a[i] : null;
			var right = i < Parts.Length ? Parts[i] : null;

			if ( right == "*" )
				return true;

			if ( !TestPart( left, right ) )
				return false;
		}

		return true;
	}

	/// <summary>
	/// Given a Url, check for {properties} and convert them to key values
	/// </summary>
	public IEnumerable<(string key, string value)> ExtractProperties( string url )
	{
		var a = url.Split( '/', StringSplitOptions.RemoveEmptyEntries );

		for ( int i = 0; i < Parts.Length; i++ )
		{
			if ( !Parts[i].StartsWith( '{' ) ) continue;
			if ( !Parts[i].EndsWith( '}' ) ) continue;

			var key = Parts[i][1..^1].Trim( '?' );

			if ( i < a.Length )
			{
				yield return (key, a[i]);
			}
			else
			{
				yield return (key, null);
			}
		}
	}
}
