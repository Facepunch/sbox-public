using System.IO;
using System.Text.Json.Nodes;

namespace Editor.AnimGraph;

/// <summary>
/// The source files behind compiled resources are kv3. They're read as json and written back
/// through kv3, so a file this tool touches stays in the format the model editor and the
/// compiler write.
/// </summary>
internal static class Kv3Document
{
	/// <summary>
	/// Read one as json. Null if it isn't there or doesn't parse, having said which in the console.
	/// </summary>
	public static JsonObject Read( string filePath )
	{
		if ( !File.Exists( filePath ) )
		{
			Log.Warning( $"Couldn't read {filePath} - there's no file there" );
			return null;
		}

		try
		{
			var text = File.ReadAllText( filePath );
			if ( string.IsNullOrWhiteSpace( text ) )
			{
				Log.Warning( $"Couldn't read {filePath} - the file is empty" );
				return null;
			}

			// Written as json by anything that doesn't have kv3 to hand, which is still valid
			if ( !text.TrimStart().StartsWith( '{' ) )
				text = EditorUtility.KeyValues3ToJson( text );

			if ( string.IsNullOrWhiteSpace( text ) )
			{
				Log.Warning( $"Couldn't read {filePath} - it doesn't parse as kv3" );
				return null;
			}

			return Json.ParseToJsonObject( text );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't read {filePath}" );
			return null;
		}
	}

	/// <summary>
	/// Write one back out, saying so in the console if it couldn't be written. Goes to a file
	/// beside it and is moved over the top once it's whole, so a write that fails part way
	/// through can't leave the source truncated - these are hand made files, not ours to lose.
	/// </summary>
	public static bool Write( string filePath, JsonObject document, bool overwrite = true )
	{
		var temporary = filePath + ".tmp";

		try
		{
			if ( !EditorUtility.JsonToKeyValues3File( temporary, Json.Serialize( document ) ) || !HasContent( temporary ) )
			{
				Log.Warning( $"Couldn't write {filePath}" );
				return false;
			}

			File.Move( temporary, filePath, overwrite );
			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't write {filePath}" );
			return false;
		}
		finally
		{
			Discard( temporary );
		}
	}

	private static bool HasContent( string filePath ) => File.Exists( filePath ) && new FileInfo( filePath ).Length > 0;

	private static void Discard( string filePath )
	{
		try
		{
			if ( File.Exists( filePath ) )
				File.Delete( filePath );
		}
		catch ( Exception )
		{
			// Nothing to be done about it, and nothing worth saying - the write itself is done
		}
	}
}
