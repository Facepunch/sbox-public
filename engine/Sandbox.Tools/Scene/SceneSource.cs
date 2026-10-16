using System.IO;
using System.Text.Json;

namespace Editor;

internal static class SceneSource
{
	internal static Asset FindAsset( SceneFile file )
	{
		if ( file is null )
			return null;

		var asset = AssetSystem.FindByPath( file.ResourcePath );
		if ( file.Guid != System.Guid.Empty && (asset is null || asset.Guid != file.Guid) )
			asset = AssetSystem.All.FirstOrDefault( x => x.Guid == file.Guid );

		if ( asset is not null && file.ResourcePath != asset.Path )
			file.InitializeSource( asset.Path, asset.Guid );

		return asset;
	}

	internal static string ReadJson( string path )
	{
		var json = File.ReadAllText( path );
		if ( !json.StartsWith( '<' ) )
			return json;

		var kv = NativeEngine.EngineGlue.LoadKeyValues3( json );
		try
		{
			return NativeEngine.EngineGlue.KeyValues3ToJson( kv.FindOrCreateMember( "data" ) );
		}
		finally
		{
			kv.DeleteThis();
		}
	}

	internal static SceneFile LoadForEditing( Asset asset )
	{
		var path = asset.GetSourceFile( true );
		var json = ReadJson( path );
		var blobPath = path + "_d";
		var blobs = File.Exists( blobPath ) ? File.ReadAllBytes( blobPath ) : [];
		return SceneFile.FromSource( asset.Path, asset.Guid, json, blobs );
	}

	internal static bool Save( Asset asset, SceneFile file )
	{
		file.InitializeSource( asset.Path, asset.Guid );
		if ( !asset.SaveSource( file ) )
			return false;

		return asset.LoadResource<SceneFile>() is not null;
	}

	internal static SceneFile LoadForPlay( SceneEditorSession editor )
	{
		if ( editor.HasUnsavedChanges || editor.Scene.Source is not SceneFile { ResourcePath: { Length: > 0 } } source )
			return editor.Scene.CreateSceneFile();

		try
		{
			if ( !editor.IsMounted && FindAsset( source ) is { } asset )
			{
				var path = asset.GetCompiledFile( true );
				if ( string.IsNullOrEmpty( path ) )
					throw new FileNotFoundException( $"No compiled file exists for '{asset.Path}'." );
				return SceneFile.FromCompiled( asset.Path, asset.Guid, File.ReadAllBytes( path ) );
			}

			var file = SceneFile.Load( source.ResourcePath );
			if ( file is null )
				Log.Warning( $"Could not load scene '{source.ResourcePath}'." );
			return file;
		}
		catch ( System.Exception e ) when ( e is IOException or InvalidDataException or System.UnauthorizedAccessException or JsonException )
		{
			Log.Error( e, $"Could not load compiled scene '{source.ResourcePath}'." );
			return null;
		}
	}
}
