using System;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>
/// When saving a game resource, we inject information about packages used into it. We do that 
/// here in this special callback because GameResource is in engine.dll, so we can't do it in Serialize()
/// </summary>
internal static class GameResourceProcessor
{
	public static void Initialize()
	{
		GameResource.ProcessSerializedObject += ProcessGameResource;
	}

	internal static IEnumerable<string> GetCloudReferences( JsonObject jso )
	{
		HashSet<string> cloudPackages = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		Sandbox.Json.WalkJsonTree( jso, ( k, v ) =>
		{
			if ( !v.TryGetValue<string>( out var path ) )
				return v;

			// not a path if it has any of these characters in it
			if ( path.Contains( ',' ) ) return v;
			if ( path.Contains( ':' ) ) return v;
			if ( path.Contains( '\"' ) ) return v;
			if ( path.Contains( '\'' ) ) return v;

			var lastPeriod = path.LastIndexOf( '.' );
			if ( lastPeriod < 0 ) return v;
			if ( lastPeriod < path.Length - 9 ) return v;

			var asset = AssetSystem.FindByPath( path );
			if ( asset is null ) return v;
			if ( asset.Package is null ) return v;

			cloudPackages.Add( asset.Package.GetIdent( false, true ) );

			return v;
		} );

		return cloudPackages.OrderBy( x => x );
	}

	private static void ProcessGameResource( object target, JsonObject jso )
	{
		jso["__references"] = JsonValue.Create( GetCloudReferences( jso ) );
	}
}
