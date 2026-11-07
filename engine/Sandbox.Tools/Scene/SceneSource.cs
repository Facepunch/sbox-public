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

	internal static string ReadJson( string path, out int sourceHash )
	{
		var json = File.ReadAllText( path );
		sourceHash = json.FastHash();
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
		var json = ReadJson( path, out var sourceHash );
		var blobPath = path + "_d";
		var blobs = File.Exists( blobPath ) ? File.ReadAllBytes( blobPath ) : [];
		var file = SceneFile.FromSource( asset.Path, asset.Guid, json, blobs );

		// Hash the file as the resource library does, which isn't the same as hashing the json for KV3 sources
		file.LastSavedSourceHash = sourceHash;
		return file;
	}

	internal static bool Save( Asset asset, SceneFile file )
	{
		file.InitializeSource( asset.Path, asset.Guid );
		if ( !asset.SaveSource( file ) )
			return false;

		if ( asset.LoadResource<SceneFile>() is null )
			return false;

		TrackSource( file );
		return true;
	}

	/// <summary>
	/// Scenes are edited from their own copy of the source, so the resource library's copy never sees the source the
	/// editor loads or saves. Give it that source's hash, or it reports the source as an external change when it's recompiled.
	/// </summary>
	internal static void TrackSource( SceneFile file )
	{
		if ( file.LastSavedSourceHash != 0 && ResourceLibrary.TryGet<SceneFile>( file.ResourcePath, out var resource ) )
			resource.LastSavedSourceHash = file.LastSavedSourceHash;
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

				// Saving doesn't compile the scene, the content build does on a worker. A scene saved since is compiled
				// here; only a failed compile plays it from memory. The compile's result is what counts: an unchanged
				// output keeps its old time, so the file can still look older than the source.
				if ( IsStale( asset, path ) )
				{
					path = asset.Compile( false ) ? asset.GetCompiledFile( true ) : null;
					if ( string.IsNullOrEmpty( path ) )
						return editor.Scene.CreateSceneFile();
				}

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

	/// <summary>
	/// The saved scene hasn't been compiled yet and a content build is running, so it can't be compiled now: on-demand
	/// compiles are suspended until the build ends. Playing should wait for it rather than play without the bake.
	/// </summary>
	internal static bool WaitsForBuild( SceneEditorSession editor ) =>
		EditorContentBuild.IsBuilding && !editor.HasUnsavedChanges && !editor.IsMounted
		&& editor.Scene.Source is SceneFile { ResourcePath: { Length: > 0 } } source
		&& FindAsset( source ) is { } asset && IsStale( asset, asset.GetCompiledFile( true ) );

	/// <summary>
	/// The compiled scene is older than its source: saved since it was last compiled.
	/// </summary>
	static bool IsStale( Asset asset, string compiled ) =>
		string.IsNullOrEmpty( compiled ) || !File.Exists( compiled ) || File.GetLastWriteTimeUtc( compiled ) < File.GetLastWriteTimeUtc( asset.GetSourceFile( true ) );
}
