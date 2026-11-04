using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Editor;

internal static partial class SceneCompiler
{
	readonly record struct SourceRevision( long Length, DateTime LastWriteTimeUtc, byte[] Hash );

	static SourceRevision CaptureSourceRevision( string sourcePath )
	{
		var content = File.ReadAllBytes( sourcePath );
		var info = new FileInfo( sourcePath );
		return new SourceRevision( content.LongLength, info.LastWriteTimeUtc, System.Security.Cryptography.SHA256.HashData( content ) );
	}

	static bool HasSourceRevision( string sourcePath, SourceRevision revision )
	{
		var before = new FileInfo( sourcePath );
		if ( !before.Exists || before.Length != revision.Length || before.LastWriteTimeUtc != revision.LastWriteTimeUtc )
			return false;

		var content = File.ReadAllBytes( sourcePath );
		var after = new FileInfo( sourcePath );
		return after.Exists && after.Length == revision.Length && after.LastWriteTimeUtc == revision.LastWriteTimeUtc
			&& System.Security.Cryptography.CryptographicOperations.FixedTimeEquals( revision.Hash, System.Security.Cryptography.SHA256.HashData( content ) );
	}

	static string GetGenerationPath( Asset asset, string generation )
	{
		var sourcePath = asset.GetSourceFile( false );
		var extension = Path.GetExtension( sourcePath ).Replace( ".", "_" );
		var dataFolder = $"{Path.ChangeExtension( sourcePath, null )}{extension}_data";
		return $"{dataFolder}/compiled/{generation}".NormalizeFilename( false );
	}

	static void CleanupGeneration( string path )
	{
		try
		{
			if ( FileSystem.Mounted.DirectoryExists( path ) )
				FileSystem.Mounted.DeleteDirectory( path, true );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Could not clean up staged scene resources at '{path}': {e.Message}" );
		}
	}

	static void CleanupPreviousGenerations( string currentPath, object generationGate, HashSet<string> activeGenerations )
	{
		var compiledFolder = currentPath[..currentPath.LastIndexOf( '/' )];
		HashSet<string> active;

		lock ( generationGate )
			active = new HashSet<string>( activeGenerations, StringComparer.OrdinalIgnoreCase );

		try
		{
			foreach ( var directory in FileSystem.Mounted.FindDirectory( compiledFolder ).ToArray() )
			{
				var path = $"{compiledFolder}/{directory}".NormalizeFilename( false );
				if ( path.Equals( currentPath, StringComparison.OrdinalIgnoreCase ) || active.Contains( path ) )
					continue;

				CleanupGeneration( path );
			}

			foreach ( var file in FileSystem.Mounted.FindFile( compiledFolder ).ToArray() )
			{
				var path = $"{compiledFolder}/{file}".NormalizeFilename( false );
				try
				{
					FileSystem.Mounted.DeleteFile( path );
				}
				catch ( Exception e )
				{
					Log.Warning( $"Could not clean up old scene resource '{path}': {e.Message}" );
				}
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"Could not clean up old compiled scene resources in '{compiledFolder}': {e.Message}" );
		}
	}

	static void WriteScene( Asset asset, string sourcePath, SceneEditorSession editor, SourceRevision sourceRevision, SceneFile file, SceneCompilerSettings settings )
	{
		if ( asset.IsDeleted || !File.Exists( sourcePath ) || sourcePath != asset.GetSourceFile( true ) )
			throw new InvalidOperationException( "The source scene was moved or deleted during compilation." );

		if ( editor.HasUnsavedChanges || !HasSourceRevision( sourcePath, sourceRevision ) )
			throw new InvalidOperationException( "The source scene changed during compilation. Save it and compile again." );

		var json = file.Serialize();
		json["__blobdata"] = System.Convert.ToBase64String( file.BinaryData ?? [] );
		var previousOutput = asset.GetCompiledFile( true );
		var previousBytes = !string.IsNullOrEmpty( previousOutput ) && File.Exists( previousOutput ) ? File.ReadAllBytes( previousOutput ) : null;

		try
		{
			if ( !IResourceCompilerSystem.GenerateResourceFileForced( sourcePath, json.ToJsonString( new JsonSerializerOptions( JsonSerializerOptions.Default ) { MaxDepth = 512 } ) ) )
				throw new InvalidOperationException( $"Could not write compiled scene '{asset.Path}'." );

			var outputPath = asset.GetCompiledFile( true );
			if ( string.IsNullOrEmpty( outputPath ) )
				throw new InvalidOperationException( $"Could not find the compiled output path for '{asset.Path}'." );

			var bytes = File.ReadAllBytes( outputPath );
			using var compiledJson = JsonDocument.Parse( Game.Resources.ReadCompiledResourceJson( bytes ), new JsonDocumentOptions { MaxDepth = 512 } );
			if ( compiledJson.RootElement.TryGetProperty( "__blobdata", out _ ) )
				throw new InvalidOperationException( $"The scene resource compiler did not process '{asset.Path}'." );

			if ( !asset.Compile( false ) )
				throw new InvalidOperationException( $"Could not refresh compiled scene '{asset.Path}'." );

			if ( editor.HasUnsavedChanges || !HasSourceRevision( sourcePath, sourceRevision ) )
				throw new InvalidOperationException( "The source scene changed during compilation. Save it and compile again." );

			NativeEngine.g_pResourceSystem.ReloadResource( asset.Path );
			asset.MetaData.Set( SceneCompilerSettings.MetadataProperty, settings );
		}
		catch
		{
			try
			{
				var outputPath = asset.GetCompiledFile( true );
				if ( string.IsNullOrEmpty( outputPath ) )
					outputPath = previousOutput;
				if ( !string.IsNullOrEmpty( outputPath ) )
				{
					if ( previousBytes is not null )
						File.WriteAllBytes( outputPath, previousBytes );
					else if ( File.Exists( outputPath ) )
						File.Delete( outputPath );

					NativeEngine.g_pResourceSystem.ReloadResource( asset.Path );
				}
			}
			catch ( Exception e )
			{
				Log.Warning( $"Could not restore the last compiled scene '{asset.Path}': {e.Message}" );
			}

			throw;
		}
	}
}
