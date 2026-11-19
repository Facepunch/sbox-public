using Sandbox.Resources;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor;

[Expose]
[ResourceIdentity( "scene" )]
internal class SceneResourceCompiler : ResourceCompiler
{
	internal static string GetBakePath( string sourcePath ) => $"{Path.ChangeExtension( sourcePath, null )}_scene_data/baked.input";

	internal static byte[] GetSourceHash( string sourcePath )
	{
		using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		var source = File.ReadAllText( sourcePath ).Replace( "\r\n", "\n" );
		hash.AppendData( SHA256.HashData( Encoding.UTF8.GetBytes( source ) ) );

		if ( File.Exists( sourcePath + "_d" ) )
		{
			using var binary = File.OpenRead( sourcePath + "_d" );
			hash.AppendData( SHA256.HashData( binary ) );
		}

		return hash.GetHashAndReset();
	}

	internal static bool IsBakeCurrent( string sourcePath )
	{
		var path = GetBakePath( sourcePath );
		if ( !File.Exists( sourcePath ) || !File.Exists( path ) )
			return false;

		using var reader = File.OpenText( path );
		return HasCurrentHeader( sourcePath, reader.ReadLine() );
	}

	static bool HasCurrentHeader( string sourcePath, string header, bool warn = false )
	{
		var parts = header?.Split( ' ' );
		Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
		if ( parts?.Length != 2 || parts[0].Length != hash.Length * 2
			|| Convert.FromHexString( parts[0], hash, out _, out _ ) != System.Buffers.OperationStatus.Done
			|| (parts[1] != "-" && !Guid.TryParseExact( parts[1], "N", out _ )) )
		{
			if ( warn )
				Log.Warning( $"Ignoring scene bake '{GetBakePath( sourcePath )}': invalid bake header. Compile the scene again to replace it." );
			return false;
		}

		if ( !CryptographicOperations.FixedTimeEquals( hash, GetSourceHash( sourcePath ) ) )
			return false;

		if ( parts[1] == "-" || Directory.Exists( Path.Combine( Path.GetDirectoryName( GetBakePath( sourcePath ) ), "compiled", parts[1] ) ) )
			return true;

		if ( warn )
			Log.Warning( $"Ignoring scene bake '{GetBakePath( sourcePath )}': compiled geometry is missing. Compile the scene again to replace it." );
		return false;
	}

	static string ReadBake( string sourcePath )
	{
		var path = GetBakePath( sourcePath );
		if ( !File.Exists( path ) )
			return null;

		using var reader = File.OpenText( path );
		return HasCurrentHeader( sourcePath, reader.ReadLine(), warn: true ) ? reader.ReadToEnd() : null;
	}

	protected override async Task<bool> Compile()
	{
		if ( Context is not ResourceCompileContextImp context )
			throw new InvalidOperationException( "Scene compilation requires an editor resource context." );

		context.AddCompileReference( context.AbsolutePath + "_d", optional: true );
		context.AddCompileReference( GetBakePath( context.AbsolutePath ), optional: true );
		var hasOverride = context.TryReadOverrideInput( out var input );
		var jsonText = hasOverride ? Encoding.UTF8.GetString( input ) : ReadBake( context.AbsolutePath );
		if ( jsonText is null )
		{
			await AssetSystem.CompileGameResource( context );
			return true;
		}

		var json = JsonNode.Parse( jsonText, documentOptions: new() { MaxDepth = 512, CommentHandling = JsonCommentHandling.Skip } ) as JsonObject
			?? throw new JsonException( "Scene compile input must be a JSON object." );
		if ( !json.Remove( "__blobdata", out var inlineBlob ) )
		{
			if ( !hasOverride )
				throw new JsonException( "Baked scene is missing its binary data." );

			await AssetSystem.CompileGameResource( context );
			return true;
		}

		var blobData = Convert.FromBase64String( inlineBlob?.GetValue<string>() ?? throw new JsonException( "Scene compile input is missing its binary data." ) );
		context.AddCompileReference( context.AbsolutePath );
		context.Data.Write( await context.ScanJsonAsync( json.ToJsonString( new JsonSerializerOptions( JsonSerializerOptions.Default ) { MaxDepth = 512 } ) ) );

		if ( blobData.Length > 0 )
		{
			unsafe
			{
				fixed ( byte* ptr = blobData )
				{
					context.WriteBlock( BlobDataSerializer.CompiledBlobName, (IntPtr)ptr, blobData.Length );
				}
			}
		}

		return true;
	}
}
