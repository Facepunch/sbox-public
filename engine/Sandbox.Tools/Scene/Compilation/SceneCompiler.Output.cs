using System;
using System.IO;
using System.Text.Json;

namespace Editor;

internal static partial class SceneCompiler
{
	static void WriteScene( Asset asset, string sourcePath, SceneFile file, SceneCompilerSettings settings )
	{
		if ( asset.IsDeleted || !File.Exists( sourcePath ) || sourcePath != asset.GetSourceFile( true ) )
			throw new InvalidOperationException( "The source scene was moved or deleted during compilation." );

		var json = file.Serialize();
		json["__blobdata"] = System.Convert.ToBase64String( file.BinaryData ?? [] );
		asset.MetaData.Set( SceneCompilerSettings.MetadataProperty, settings );

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

		NativeEngine.g_pResourceSystem.ReloadResource( asset.Path );
	}
}
