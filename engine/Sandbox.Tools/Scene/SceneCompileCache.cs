using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Sandbox.Resources;

namespace Editor;

/// <summary>
/// Stores the selected scene bake and its generated runtime data.
/// </summary>
internal static class SceneCompileCache
{
	const int Version = 7;
	const string SceneJson = ".scene.json";
	const string SceneBlob = ".scene.blob";
	const string OwnershipFile = ".scene-compile-generation";
	const string Ownership = "sbox-scene-compile:1";
	internal const string DirtyProperty = "sceneCompileDirty";
	const string GenerationProperty = "__scene_compile_generation";
	static readonly JsonSerializerOptions JsonOptions = new( JsonSerializerOptions.Default ) { MaxDepth = 512 };

	sealed class Compilation
	{
		public int Version { get; set; }
		public Guid SceneId { get; set; }
		public string Generation { get; set; }
		public Dictionary<string, string> Outputs { get; set; }
	}

	static string SourcePath( Asset asset )
	{
		if ( asset?.AssetType?.ResourceType != typeof( SceneFile ) )
			return null;

		var source = asset.GetSourceFile( true );
		if ( !string.IsNullOrEmpty( source )
			&& (File.Exists( source ) || File.Exists( ManifestPath( source ) )) )
			return source;

		var compiled = asset.GetCompiledFile( true );
		if ( string.IsNullOrEmpty( compiled ) || !compiled.EndsWith( ".scene_c", StringComparison.OrdinalIgnoreCase ) )
			return null;

		source = compiled[..^2];
		return File.Exists( ManifestPath( source ) ) ? source : null;
	}

	static string DataFolder( string source ) => Path.ChangeExtension( source, null ) + "_scene_data";
	static string ManifestPath( string source ) => Path.Combine( DataFolder( source ), "compiled", ".scene-compile.json" );
	static string MetadataPath( string source ) => source + ".meta";
	static string GenerationFolder( string source, string generation ) => Path.Combine( DataFolder( source ), "compiled", generation );
	static string Error( string source, string reason ) => reason.StartsWith( "Scene compilation for ", StringComparison.Ordinal )
		? reason
		: $"Scene compilation for '{source}' {reason.TrimEnd( '.' )}. Save the scene, then use Scene > Compile Scene to update its runtime data.";

	static bool IsReadError( Exception e ) => e is IOException or InvalidDataException or UnauthorizedAccessException
		or JsonException or InvalidOperationException or ArgumentException or FormatException;

	internal static void BeginGeneration( Asset asset, string generation )
	{
		if ( !Guid.TryParseExact( generation, "N", out _ ) )
			throw new ArgumentException( "Invalid scene compilation generation.", nameof( generation ) );

		var source = SourcePath( asset ) ?? throw new InvalidOperationException( "Scene compilation requires a saved source scene." );
		WriteAtomic( Path.Combine( GenerationFolder( source, generation ), OwnershipFile ), Encoding.UTF8.GetBytes( Ownership ) );
	}

	internal static void DiscardGeneration( string source, string generation )
	{
		if ( !Guid.TryParseExact( generation, "N", out _ ) )
			throw new ArgumentException( "Invalid scene compilation generation.", nameof( generation ) );

		var folder = GenerationFolder( source, generation );

		try
		{
			if ( !Directory.Exists( folder ) )
				return;

			// A failed rollback may still leave this generation selected. Never delete its data.
			if ( ReadManifest( source )?.Generation.Equals( generation, StringComparison.OrdinalIgnoreCase ) == true )
				return;

			DiscardOwnedGeneration( folder );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException )
		{
			Log.Warning( $"Could not remove scene compilation '{folder}': {e.Message}" );
		}
	}

	static void DiscardOwnedGeneration( string folder )
	{
		try
		{
			if ( (File.GetAttributes( folder ) & FileAttributes.ReparsePoint) != 0 )
				return;

			var marker = Path.Combine( folder, OwnershipFile );
			if ( !File.Exists( marker ) || File.ReadAllText( marker ) != Ownership )
				return;

			try
			{
				Directory.Delete( folder, recursive: true );
			}
			finally
			{
				if ( Directory.Exists( folder ) && !File.Exists( marker ) )
					WriteAtomic( marker, Encoding.UTF8.GetBytes( Ownership ) );
			}
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Could not remove scene compilation '{folder}': {e.Message}" );
		}
	}

	internal static void PruneGenerations( string source, bool retired = false )
	{
		var folder = Path.GetDirectoryName( ManifestPath( source ) );

		try
		{
			if ( !Directory.Exists( folder ) )
				return;

			var current = ReadManifest( source );
			if ( current is null && !retired )
				throw new InvalidDataException( "The scene compilation manifest is missing." );

			foreach ( var directory in Directory.EnumerateDirectories( folder ) )
			{
				var generation = Path.GetFileName( directory );
				if ( !Guid.TryParseExact( generation, "N", out _ )
					|| generation.Equals( current?.Generation, StringComparison.OrdinalIgnoreCase ) )
					continue;

				DiscardOwnedGeneration( directory );
			}
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException )
		{
			Log.Warning( $"Could not clean old scene compilations in '{folder}': {e.Message}" );
		}
	}

	static JsonNode ReadSourceJson( string path ) => JsonNode.Parse( SceneSource.ReadJson( path ), default,
		new JsonDocumentOptions { MaxDepth = 512, CommentHandling = JsonCommentHandling.Skip } );

	static string CompiledPath( string source )
	{
		var path = AssetSystem.FindByPath( source )?.GetCompiledFile( true );
		return string.IsNullOrEmpty( path ) ? source + "_c" : path;
	}

	static JsonObject ReadCompiledJson( string source, out byte[] data )
	{
		data = null;
		var path = CompiledPath( source );
		if ( !File.Exists( path ) )
			return null;

		data = File.ReadAllBytes( path );
		if ( data.Length == 0 )
			throw new InvalidDataException( "has an empty compiled resource" );

		var json = Game.Resources.ReadCompiledResourceJson( data );
		return JsonNode.Parse( json, default, new JsonDocumentOptions { MaxDepth = 512 } ) as JsonObject
			?? throw new InvalidDataException( "has invalid compiled scene data" );
	}

	static bool HasHistory( string source )
	{
		if ( File.Exists( ManifestPath( source ) ) )
			return true;

		try
		{
			// Only an actual compiled marker proves history when the data folder has been deleted.
			return ReadCompiledJson( source, out _ )?["__scene_compiled"]?.GetValue<bool>() == true;
		}
		catch ( Exception e ) when ( IsReadError( e ) )
		{
			Log.Warning( $"Cannot read ordinary compiled scene '{source}': {e.Message}. Recompile it from source." );
			return false;
		}
	}

	static byte[] ReadMetadataBytes( string source ) => File.Exists( MetadataPath( source ) ) ? File.ReadAllBytes( MetadataPath( source ) ) : null;

	static JsonObject ReadMetadata( byte[] bytes )
	{
		if ( bytes is null )
			return new JsonObject();

		return JsonNode.Parse( bytes, default, new JsonDocumentOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip
		} ) as JsonObject ?? throw new InvalidDataException( "Scene metadata must contain a JSON object" );
	}

	internal static bool IsDirty( Asset asset ) => ReadSetting( asset, DirtyProperty )?.GetValue<bool>() != false;

	internal static JsonNode ReadSetting( Asset asset, string name )
	{
		var source = SourcePath( asset );
		if ( string.IsNullOrEmpty( source ) )
			throw new InvalidDataException( "Scene compilation settings require a saved source scene." );

		return ReadMetadata( ReadMetadataBytes( source ) )[name];
	}

	internal static void WriteSetting( Asset asset, string name, JsonNode value )
	{
		var source = SourcePath( asset );
		if ( string.IsNullOrEmpty( source ) || !File.Exists( source ) )
			throw new InvalidDataException( "Scene compilation settings require a saved source scene." );

		var metadata = ReadMetadata( ReadMetadataBytes( source ) );
		if ( JsonNode.DeepEquals( metadata[name], value ) )
			return;

		metadata[name] = value?.DeepClone();
		WriteAtomic( MetadataPath( source ), JsonSerializer.SerializeToUtf8Bytes( metadata, JsonOptions ) );
	}

	/// <summary>
	/// True when a selected bake's generated files are present.
	/// Compiled-only packaged scenes do not need their editor cache.
	/// </summary>
	internal static bool HasCompilation( Asset asset )
	{
		var source = SourcePath( asset );
		if ( string.IsNullOrEmpty( source ) )
			return false;

		try
		{
			return ReadCompilation( source ) is not null;
		}
		catch ( Exception e ) when ( IsReadError( e ) )
		{
			// Route unreadable output through ValidateOutput rather than silently loading it.
			Log.Warning( Error( source, e.Message ) );
			return true;
		}
	}

	/// <summary>
	/// Known compilations must have complete, matching output. Authoring changes do not invalidate a bake.
	/// </summary>
	internal static bool ValidateOutput( Asset asset, out string error )
	{
		error = null;
		var source = SourcePath( asset );
		if ( string.IsNullOrEmpty( source ) )
			return true;

		if ( !TryRead( source, out var compilation, out error ) )
			return false;

		try
		{
			JsonObject runtime;
			byte[] data;
			try
			{
				runtime = ReadCompiledJson( source, out data );
			}
			catch ( Exception e ) when ( compilation is null && IsReadError( e ) )
			{
				Log.Warning( $"Rebuilding unreadable runtime scene '{source}': {e.Message}" );
				runtime = null;
				data = null;
			}

			var isCompiled = runtime?["__scene_compiled"]?.GetValue<bool>() == true;
			if ( compilation is null && runtime is not null && !isCompiled )
				return true;

			if ( runtime is null || isCompiled != (compilation is not null) )
			{
				if ( !asset.Compile( true ) || asset.IsCompileFailed )
					throw new InvalidDataException( "could not regenerate its runtime .scene_c" );

				runtime = ReadCompiledJson( source, out data );
			}

			RequireRuntime( source, compilation, runtime, data );
			return true;
		}
		catch ( Exception e ) when ( IsReadError( e ) )
		{
			error = Error( source, e.Message );
			return false;
		}
	}

	static Compilation ReadManifest( string source )
	{
		var manifest = ManifestPath( source );
		if ( !File.Exists( manifest ) )
			return null;

		using var stream = File.OpenRead( manifest );
		var compilation = JsonSerializer.Deserialize<Compilation>( stream, JsonOptions );
		if ( compilation is null || !Guid.TryParseExact( compilation.Generation, "N", out _ ) )
			throw new InvalidDataException( Error( source, "has an invalid manifest" ) );

		return compilation;
	}

	static Compilation ReadCompilation( string source )
	{
		var compilation = ReadManifest( source );
		if ( compilation is null )
			return null;

		var invalidChars = Path.GetInvalidFileNameChars();
		if ( compilation.Version != Version || compilation.SceneId == Guid.Empty
			|| compilation.Outputs is null
			|| !compilation.Outputs.ContainsKey( SceneJson ) || !compilation.Outputs.ContainsKey( SceneBlob )
			|| compilation.Outputs.Any( x => x.Key is "" or "." or ".." || x.Key.IndexOfAny( invalidChars ) >= 0
				|| x.Value is not { Length: 64 } || !x.Value.All( char.IsAsciiHexDigit ) ) )
			throw new InvalidDataException( Error( source, "has an invalid or incompatible manifest" ) );

		var folder = GenerationFolder( source, compilation.Generation );
		if ( compilation.Outputs.Keys.Any( name => !File.Exists( Path.Combine( folder, name ) ) ) )
		{
			if ( !File.Exists( source ) )
				throw new InvalidDataException( Error( source, "is missing generated data and its editable source" ) );

			return null;
		}

		return compilation;
	}

	static bool TryRead( string source, out Compilation compilation, out string error )
	{
		compilation = null;
		error = null;

		try
		{
			compilation = ReadCompilation( source );
			if ( compilation is null )
				return true;

			var folder = GenerationFolder( source, compilation.Generation );
			foreach ( var (name, hash) in compilation.Outputs )
			{
				if ( OutputHash( Path.Combine( folder, name ) ) != hash )
					throw new InvalidDataException( $"is missing or has changed generated data '{name}'" );
			}

			RequireSceneIdentity( source, compilation.SceneId );
			return true;
		}
		catch ( Exception e ) when ( IsReadError( e ) )
		{
			compilation = null;
			error = Error( source, e.Message );
			return false;
		}
	}

	static void RequireRuntime( string source, Compilation compilation, JsonObject runtime, byte[] data )
	{
		if ( runtime is null || !string.IsNullOrEmpty( runtime["__scene_compile_error"]?.GetValue<string>() ) )
			throw new InvalidDataException( Error( source, "has no usable runtime .scene_c" ) );

		var isCompiled = runtime["__scene_compiled"]?.GetValue<bool>() == true;
		if ( compilation is null )
		{
			if ( isCompiled )
				throw new InvalidDataException( Error( source, "could not restore its editable runtime scene" ) );

			RequireSceneIdentity( source, (runtime["__guid"] ?? runtime["Id"])?.GetValue<Guid>() ?? Guid.Empty );
			return;
		}

		if ( !isCompiled || runtime[GenerationProperty]?.GetValue<string>() != compilation.Generation
			|| runtime["__guid"]?.GetValue<Guid>() != compilation.SceneId )
			throw new InvalidDataException( Error( source, "is missing its matching runtime .scene_c" ) );

		var blob = Game.Resources.ReadCompiledResourceBlock( BlobDataSerializer.CompiledBlobName, data ) ?? [];
		if ( Convert.ToHexString( SHA256.HashData( blob ) ) != compilation.Outputs[SceneBlob] )
			throw new InvalidDataException( Error( source, "has runtime binary data that does not match its compilation" ) );
	}

	/// <summary>
	/// Called only by the ordinary resource compiler. Never generates geometry or rewrites source.
	/// </summary>
	internal static bool TryGetRuntimeData( ResourceCompileContext context, ref string json, out byte[] blob, out bool compiled )
	{
		blob = null;
		compiled = false;
		var source = context.AbsolutePath;
		if ( !TryRead( source, out var compilation, out var error ) )
		{
			// Saving must still produce a valid resource container. Runtime loading rejects this
			// explicit unavailable state; failing compilation here causes endless on-demand retries.
			Log.Error( error );
			var sourceJson = ReadSourceJson( source );
			var unavailable = new JsonObject
			{
				["__guid"] = (sourceJson?["__guid"] ?? sourceJson?["Id"])?.DeepClone(),
				["__scene_compiled"] = true,
				["__scene_compile_error"] = error
			};
			json = unavailable.ToJsonString( JsonOptions );
			blob = [];
			compiled = true;
			return true;
		}

		if ( compilation is null )
		{
			if ( HasHistory( source ) )
				Log.Warning( $"Scene compilation data for '{source}' is missing. Using the editable scene." );
			return true;
		}

		var relativeFolder = GenerationFolder( context.RelativePath, compilation.Generation );
		foreach ( var name in compilation.Outputs.Keys )
		{
			// Compiled resources live in GAME, while AddCompileReference records CONTENT inputs.
			// Register them as runtime resources rather than nonexistent source-side binary files.
			if ( name.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) )
			{
				var resource = Path.Combine( relativeFolder, name[..^2] );
				context.AddRuntimeReference( resource.NormalizeFilename( false ) );
			}
		}

		var folder = GenerationFolder( source, compilation.Generation );
		var jsonPath = Path.Combine( folder, SceneJson );
		var blobPath = Path.Combine( folder, SceneBlob );
		context.AddCompileReference( jsonPath );
		context.AddCompileReference( blobPath );
		var jsonBytes = File.ReadAllBytes( jsonPath );
		blob = File.ReadAllBytes( blobPath );
		if ( Convert.ToHexString( SHA256.HashData( jsonBytes ) ) != compilation.Outputs[SceneJson]
			|| Convert.ToHexString( SHA256.HashData( blob ) ) != compilation.Outputs[SceneBlob] )
		{
			Log.Error( Error( source, "changed while its runtime data was being read" ) );
			return false;
		}

		var runtime = JsonNode.Parse( jsonBytes, default, new JsonDocumentOptions { MaxDepth = 512 } ) as JsonObject
			?? throw new InvalidDataException( Error( source, "has invalid baked scene data" ) );
		var assets = new Dictionary<Guid, Asset>();
		foreach ( var asset in AssetSystem.All )
			assets.TryAdd( asset.Guid, asset );
		ResolveRuntimeReferences( runtime, assets );
		json = runtime.ToJsonString( JsonOptions );
		compiled = true;
		return true;
	}

	static Asset ResolveGuidReference( JsonObject obj, Dictionary<Guid, Asset> assets )
	{
		if ( !obj.All( x => x.Key is "Id" or "Path" )
			|| obj["Id"] is not JsonValue idValue || !idValue.TryGetValue<string>( out var idText )
			|| !Guid.TryParse( idText, out var id ) || id == Guid.Empty )
			return null;

		return assets.GetValueOrDefault( id )
			?? (obj["Path"] is JsonValue path && path.TryGetValue<string>( out var filename ) ? AssetSystem.FindByPath( filename ) : null)
			?? throw new InvalidDataException( $"Runtime resource '{id}' could not be resolved; restore it or compile the scene again" );
	}

	static void ResolveRuntimeReferences( JsonNode node, Dictionary<Guid, Asset> assets )
	{
		if ( node is JsonObject obj )
		{
			if ( ResolveGuidReference( obj, assets ) is { } reference )
			{
				// ScanJson discovers runtime dependencies by path, not GUID. Supply the resolved
				// path so GUID-only and moved resources are also included when publishing.
				obj["Path"] = reference.Path;
				return;
			}

			foreach ( var child in obj )
				ResolveRuntimeReferences( child.Value, assets );
		}
		else if ( node is JsonArray array )
		{
			foreach ( var child in array )
				ResolveRuntimeReferences( child, assets );
		}
	}

	static string OutputHash( string path )
	{
		using var stream = File.OpenRead( path );
		return Convert.ToHexString( SHA256.HashData( stream ) );
	}

	static void RequireSceneIdentity( string source, Guid id )
	{
		var json = ReadSourceJson( source );
		if ( id == Guid.Empty || (json?["__guid"] ?? json?["Id"])?.GetValue<Guid>() != id )
			throw new InvalidDataException( Error( source, "does not match the saved scene's identity" ) );
	}

	static void WriteAtomic( string path, byte[] data )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( path ) );
		var temp = path + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		try
		{
			File.WriteAllBytes( temp, data );
			File.Move( temp, path, overwrite: true );
		}
		finally
		{
			if ( File.Exists( temp ) )
				File.Delete( temp );
		}
	}

	static void RestoreSettings( string source, byte[] previous, byte[] written )
	{
		var current = ReadMetadataBytes( source );
		if ( current is null )
			return;

		if ( current.AsSpan().SequenceEqual( written ) )
		{
			RestoreFile( MetadataPath( source ), previous );
			return;
		}

		const string property = SceneCompilerSettings.MetadataProperty;
		var metadata = ReadMetadata( current );
		if ( !JsonNode.DeepEquals( metadata[property], ReadMetadata( written )[property] ) )
			return;

		if ( ReadMetadata( previous ).TryGetPropertyValue( property, out var value ) )
			metadata[property] = value?.DeepClone();
		else
			metadata.Remove( property );
		WriteAtomic( MetadataPath( source ), JsonSerializer.SerializeToUtf8Bytes( metadata, JsonOptions ) );
	}

	static void RestoreOutput( string manifest, byte[] previousManifest, string compiled, byte[] previousCompiled )
	{
		try
		{
			RestoreFile( manifest, previousManifest );
		}
		finally
		{
			RestoreFile( compiled, previousCompiled );
		}
	}

	static void RestoreFile( string path, byte[] previous )
	{
		if ( previous is not null )
			WriteAtomic( path, previous );
		else
			File.Delete( path );
	}

	internal static void ClearCompilation( Asset asset, Guid sceneId )
	{
		var source = SourcePath( asset );
		if ( string.IsNullOrEmpty( source ) )
			return;

		if ( !HasHistory( source ) )
			return;

		RequireSceneIdentity( source, sceneId );
		var manifest = ManifestPath( source );
		var compiled = CompiledPath( source );
		var previous = File.Exists( manifest ) ? File.ReadAllBytes( manifest ) : null;
		var previousCompiled = File.Exists( compiled ) ? File.ReadAllBytes( compiled ) : null;
		var success = false;

		try
		{
			File.Delete( manifest );
			File.Delete( compiled );
			if ( !asset.Compile( true ) || asset.IsCompileFailed )
				throw new InvalidOperationException( $"Could not restore '{asset.Path}' to an ordinary runtime scene. The previous compilation has been preserved." );

			RequireSceneIdentity( source, sceneId );
			var runtime = ReadCompiledJson( source, out var data );
			RequireRuntime( source, null, runtime, data );

			success = true;
		}
		finally
		{
			if ( !success )
			{
				RestoreOutput( manifest, previous, compiled, previousCompiled );
			}
		}

		PruneGenerations( source, retired: true );
	}

	/// <summary>
	/// Publish a completed generation, then force the standard resource compiler to create .scene_c.
	/// Roll back the selector and compiled file if compilation fails; never touch .scene or .scene_d.
	/// </summary>
	internal static void Publish( Asset asset, string source, string generation, SceneFile file, SceneCompilerSettings settings, CancellationToken cancel )
	{
		if ( !Guid.TryParseExact( generation, "N", out _ ) )
			throw new ArgumentException( "Invalid scene compilation generation.", nameof( generation ) );

		settings.Validate();
		void RequireSource()
		{
			cancel.ThrowIfCancellationRequested();
			if ( asset.IsDeleted || !File.Exists( source )
				|| !string.Equals( source, asset.GetSourceFile( true ), StringComparison.OrdinalIgnoreCase ) )
				throw new InvalidDataException( Error( source, "was moved or deleted during compilation" ) );

			RequireSceneIdentity( source, file.Id );
		}

		RequireSource();

		var compilation = new Compilation { Version = Version, SceneId = file.Id, Generation = generation, Outputs = new() };
		var folder = GenerationFolder( source, generation );
		file.IsCompiled = true;
		var jsonObject = file.Serialize();
		jsonObject["__scene_compiled"] = true;
		jsonObject[GenerationProperty] = generation;
		var json = jsonObject.ToJsonString( JsonOptions );
		WriteAtomic( Path.Combine( folder, SceneJson ), Encoding.UTF8.GetBytes( json ) );
		WriteAtomic( Path.Combine( folder, SceneBlob ), file.BinaryData ?? [] );

		foreach ( var output in Directory.EnumerateFiles( folder ) )
			compilation.Outputs.Add( Path.GetFileName( output ), OutputHash( output ) );

		var manifest = ManifestPath( source );
		var previous = File.Exists( manifest ) ? File.ReadAllBytes( manifest ) : null;
		var compiled = CompiledPath( source );
		var previousCompiled = File.Exists( compiled ) ? File.ReadAllBytes( compiled ) : null;
		var previousMetadata = ReadMetadataBytes( source );
		var metadata = ReadMetadata( previousMetadata );
		metadata[SceneCompilerSettings.MetadataProperty] = JsonSerializer.SerializeToNode( settings );
		var updatedMetadata = JsonSerializer.SerializeToUtf8Bytes( metadata, JsonOptions );
		var metadataWritten = false;
		var success = false;

		try
		{
			RequireSource();
			if ( previousMetadata is null || !previousMetadata.AsSpan().SequenceEqual( updatedMetadata ) )
			{
				WriteAtomic( MetadataPath( source ), updatedMetadata );
				metadataWritten = true;
			}

			WriteAtomic( manifest, JsonSerializer.SerializeToUtf8Bytes( compilation, JsonOptions ) );
			if ( !asset.Compile( true ) || asset.IsCompileFailed || !File.Exists( compiled ) )
				throw new InvalidOperationException( $"Could not compile '{asset.Path}' into its runtime .scene_c. See the resource-compiler error in the editor console. The previous compilation has been preserved." );

			RequireSource();
			if ( !TryRead( source, out var published, out var error ) )
				throw new InvalidDataException( error );
			if ( published?.Generation != generation )
				throw new InvalidDataException( Error( source, "does not select the completed generation" ) );
			var runtime = ReadCompiledJson( source, out var data );
			RequireRuntime( source, published, runtime, data );
			success = true;
		}
		finally
		{
			if ( !success )
			{
				try
				{
					RestoreOutput( manifest, previous, compiled, previousCompiled );
				}
				finally
				{
					if ( metadataWritten )
						RestoreSettings( source, previousMetadata, updatedMetadata );
				}
			}
		}

		PruneGenerations( source );
	}
}
