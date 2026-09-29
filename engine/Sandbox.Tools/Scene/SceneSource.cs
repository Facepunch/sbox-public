using System.IO;

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

		if ( asset is not null && file.IsSourceSnapshot && file.ResourcePath != asset.Path )
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

	internal static SceneFile ResolveRuntime( SceneFile file )
	{
		if ( string.IsNullOrEmpty( file.ResourcePath ) )
			return file;

		var asset = FindAsset( file );
		if ( asset is null )
			return file;

		var editor = SceneEditorSession.Resolve( file );
		if ( !SceneCompileCache.HasCompilation( asset ) )
		{
			if ( !file.IsCompiled || !File.Exists( asset.GetSourceFile( true ) ) )
				return file;

			Log.Warning( $"Scene compilation data for '{asset.Path}' is missing. Using the editable scene." );
			return editor is not null ? editor.Scene.CreateSceneFile() : LoadForEditing( asset );
		}

		if ( editor?.CompilationDirty == true || SceneCompileCache.IsDirty( asset ) )
			return editor is not null ? editor.Scene.CreateSceneFile() : LoadForEditing( asset );

		if ( !SceneCompileCache.ValidateOutput( asset, out var error ) )
		{
			Log.Error( error );
			return null;
		}

		var compiledPath = asset.GetCompiledFile( true );
		if ( string.IsNullOrEmpty( compiledPath ) )
			compiledPath = asset.GetSourceFile( true ) + "_c";

		return SceneFile.FromCompiled( asset.Path, asset.Guid, File.ReadAllBytes( compiledPath ) );
	}

	internal static bool PreparePlay( SceneEditorSession session, out SceneLoadOptions options )
	{
		options = null;
		if ( session.CompilationDirty )
			return true;

		var file = session.Scene.Source as SceneFile;
		var asset = FindAsset( file );
		if ( asset is null || !SceneCompileCache.HasCompilation( asset ) )
			return true;

		options = new SceneLoadOptions();
		options.SetScene( file );
		return options.PrepareRuntime();
	}
}
