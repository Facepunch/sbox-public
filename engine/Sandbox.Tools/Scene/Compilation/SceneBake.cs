using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>
/// The baked scene kept beside its source. The content build and the asset system recompile .scene_c from the source
/// (a save, a checkout, a publish), so a bake that only lives in .scene_c is lost. The compiler rebuilds it from this
/// file for as long as the source scene is the one it was made from.
/// File layout: the source hash on the first line, then the JSON given to the scene resource compiler.
/// </summary>
internal static class SceneBake
{
	public static string FilePath( string sourcePath )
	{
		var extension = Path.GetExtension( sourcePath ).Replace( ".", "_" );
		return $"{Path.ChangeExtension( sourcePath, null )}{extension}_data/baked.json".NormalizeFilename( false );
	}

	/// <summary>
	/// What a bake is made from: the source scene and its binary data.
	/// </summary>
	public static string HashSource( string sourcePath )
	{
		using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		hash.AppendData( File.ReadAllBytes( sourcePath ) );

		var data = sourcePath + "_d";
		if ( File.Exists( data ) )
			hash.AppendData( File.ReadAllBytes( data ) );

		return Convert.ToHexString( hash.GetHashAndReset() );
	}

	public static void Write( string sourcePath, string json )
	{
		var path = FilePath( sourcePath );
		Directory.CreateDirectory( Path.GetDirectoryName( path ) );

		var temp = path + ".tmp";
		File.WriteAllText( temp, $"{HashSource( sourcePath )}\n{json}" );
		File.Move( temp, path, true );
	}

	/// <summary>
	/// Whether the bake on disk was made from the scene as it is now.
	/// </summary>
	public static bool IsCurrent( string sourcePath )
	{
		var path = FilePath( sourcePath );
		if ( string.IsNullOrEmpty( sourcePath ) || !File.Exists( sourcePath ) || !File.Exists( path ) )
			return false;

		try
		{
			using var reader = new StreamReader( path );
			return reader.ReadLine() == HashSource( sourcePath );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			return false;
		}
	}

	/// <summary>
	/// The bake JSON when it was made from the scene as it is now, otherwise null.
	/// </summary>
	public static JsonObject TryRead( string sourcePath )
	{
		if ( !IsCurrent( sourcePath ) )
			return null;

		try
		{
			var text = File.ReadAllText( FilePath( sourcePath ) );
			return JsonNode.Parse( text[(text.IndexOf( '\n' ) + 1)..], documentOptions: new() { MaxDepth = 512 } ) as JsonObject;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or JsonException )
		{
			return null;
		}
	}
}
