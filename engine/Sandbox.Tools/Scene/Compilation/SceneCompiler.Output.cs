using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Editor;

internal static partial class SceneCompiler
{
	readonly record struct SourceRevision( long Length, DateTime LastWriteTimeUtc, byte[] Hash );

	static SourceRevision CaptureSourceRevision( string sourcePath )
	{
		var info = new FileInfo( sourcePath );
		return new SourceRevision( info.Length, info.LastWriteTimeUtc, SceneResourceCompiler.GetSourceHash( sourcePath ) );
	}

	static bool HasSourceRevision( string sourcePath, SourceRevision revision )
	{
		var before = new FileInfo( sourcePath );
		if ( !before.Exists || before.Length != revision.Length || before.LastWriteTimeUtc != revision.LastWriteTimeUtc )
			return false;

		var hash = SceneResourceCompiler.GetSourceHash( sourcePath );
		var after = new FileInfo( sourcePath );
		return after.Exists && after.Length == revision.Length && after.LastWriteTimeUtc == revision.LastWriteTimeUtc
			&& System.Security.Cryptography.CryptographicOperations.FixedTimeEquals( revision.Hash, hash );
	}

	/// <summary>
	/// Where a generation's resources end up on disk, laid out the same way as the scene folder that writes them.
	/// </summary>
	internal static string GetGenerationPath( string sourcePath, string generation )
	{
		var extension = Path.GetExtension( sourcePath ).Replace( ".", "_" );
		var dataFolder = $"{Path.ChangeExtension( sourcePath, null )}{extension}_data";

		// Normalized so it compares equal to the paths Directory hands back during cleanup.
		return Path.GetFullPath( Path.Combine( dataFolder, "compiled", generation ) );
	}

	internal static void CleanupGeneration( string path )
	{
		try
		{
			if ( Directory.Exists( path ) )
				Directory.Delete( path, true );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Could not clean up staged scene resources at '{path}': {e.Message}" );
		}
	}

	internal static void CleanupPreviousGenerations( string currentPath, object generationGate, HashSet<string> activeGenerations )
	{
		var compiledFolder = Path.GetDirectoryName( currentPath );
		if ( !Directory.Exists( compiledFolder ) )
			return;

		HashSet<string> active;

		lock ( generationGate )
			active = new HashSet<string>( activeGenerations, StringComparer.OrdinalIgnoreCase );

		try
		{
			foreach ( var path in Directory.GetDirectories( compiledFolder ) )
			{
				if ( path.Equals( currentPath, StringComparison.OrdinalIgnoreCase ) || active.Contains( path ) )
					continue;

				CleanupGeneration( path );
			}

			foreach ( var path in Directory.GetFiles( compiledFolder ) )
			{
				try
				{
					File.Delete( path );
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

	static void WriteBake( string path, byte[] data )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( path ) );
		var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllBytes( temporaryPath, data );
			File.Move( temporaryPath, path, overwrite: true );
		}
		finally
		{
			File.Delete( temporaryPath );
		}
	}

	static void WriteScene( Asset asset, string sourcePath, SceneEditorSession editor, SourceRevision sourceRevision, SceneFile file, SceneCompilerSettings settings, string generation )
	{
		if ( asset.IsDeleted || !File.Exists( sourcePath ) || sourcePath != asset.GetSourceFile( true ) )
			throw new InvalidOperationException( "The source scene was moved or deleted during compilation." );

		if ( editor.HasUnsavedChanges || !HasSourceRevision( sourcePath, sourceRevision ) )
			throw new InvalidOperationException( "The source scene changed during compilation. Save it and compile again." );

		var json = file.Serialize();
		json["__blobdata"] = System.Convert.ToBase64String( file.BinaryData ?? [] );
		var jsonText = json.ToJsonString( new JsonSerializerOptions( JsonSerializerOptions.Default ) { MaxDepth = 512 } );
		var bakePath = SceneResourceCompiler.GetBakePath( sourcePath );
		var previousBake = File.Exists( bakePath ) ? File.ReadAllBytes( bakePath ) : null;
		var previousOutput = asset.GetCompiledFile( true );
		var previousBytes = !string.IsNullOrEmpty( previousOutput ) && File.Exists( previousOutput ) ? File.ReadAllBytes( previousOutput ) : null;

		try
		{
			WriteBake( bakePath, System.Text.Encoding.UTF8.GetBytes( $"{System.Convert.ToHexString( sourceRevision.Hash )} {generation}\n{jsonText}" ) );
			if ( !IResourceCompilerSystem.GenerateResourceFileForced( sourcePath, jsonText ) )
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
				if ( previousBake is not null )
					WriteBake( bakePath, previousBake );
				else
					File.Delete( bakePath );

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
