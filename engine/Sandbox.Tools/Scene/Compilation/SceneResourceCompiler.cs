using Sandbox.Resources;
using System;
using System.Globalization;
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

	internal static string GetCompiledFolder( string sourcePath ) => Path.Combine( Path.GetDirectoryName( GetBakePath( sourcePath ) ), "compiled" );

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

	internal static bool IsBakeCurrent( string sourcePath, out string outputFolder )
		=> TryReadBake( sourcePath, out outputFolder, out _, readContents: false );

	internal static string ReadBake( string sourcePath )
		=> TryReadBake( sourcePath, out _, out var json, readContents: true ) ? json : null;

	static bool TryReadBake( string sourcePath, out string folder, out string json, bool readContents )
	{
		folder = GetCompiledFolder( sourcePath );
		json = null;
		var path = GetBakePath( sourcePath );
		if ( !File.Exists( sourcePath ) || !File.Exists( path ) )
			return false;

		using var reader = File.OpenText( path );
		var parts = reader.ReadLine()?.Split( ' ' );
		Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
		int aggregates = 0, meshes = 0, collisions = 0;
		var counted = parts?.Length == 4 && Count( parts[1], out aggregates ) && Count( parts[2], out meshes ) && Count( parts[3], out collisions );
		var legacy = parts?.Length == 2 && (parts[1] is "-" or "." || Guid.TryParseExact( parts[1], "N", out _ ));
		if ( (!counted && !legacy) || parts[0].Length != hash.Length * 2
			|| Convert.FromHexString( parts[0], hash, out _, out _ ) != System.Buffers.OperationStatus.Done )
		{
			if ( readContents )
				Log.Warning( $"Ignoring scene bake '{path}': invalid bake header. Compile the scene again to replace it." );
			return false;
		}

		if ( !CryptographicOperations.FixedTimeEquals( hash, GetSourceHash( sourcePath ) ) )
			return false;

		bool complete;
		if ( counted )
		{
			complete = HasFiles( folder, "aggregate_", ".vmdl_c", aggregates )
				&& HasFiles( folder, "mesh_", ".vmdl_c", meshes )
				&& HasFiles( folder, "collision_", ".vphys_c", collisions );
		}
		else if ( parts[1] == "-" )
		{
			complete = true;
		}
		else
		{
			if ( parts[1] != "." )
				folder = Path.Combine( folder, parts[1] );

			complete = false;
			if ( Directory.Exists( folder ) )
			{
				json = reader.ReadToEnd();
				try
				{
					complete = HasLegacyOutputs( folder, json );
				}
				catch ( JsonException )
				{
					if ( readContents )
						Log.Warning( $"Ignoring scene bake '{path}': invalid scene JSON. Compile the scene again to replace it." );
					return false;
				}
			}
		}

		if ( !complete )
		{
			if ( readContents )
				Log.Warning( $"Ignoring scene bake '{path}': compiled geometry is missing. Compile the scene again to replace it." );
			return false;
		}

		if ( readContents )
			json ??= reader.ReadToEnd();
		return true;

		static bool Count( string text, out int count ) => int.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out count );
	}

	static bool HasFiles( string folder, string prefix, string extension, int count )
	{
		for ( int i = 0; i < count; i++ )
		{
			if ( !File.Exists( Path.Combine( folder, $"{prefix}{i}{extension}" ) ) )
				return false;
		}

		return true;
	}

	// Bakes made before the header carried counts store the generated paths on the scene components.
	static bool HasLegacyOutputs( string folder, string json )
	{
		var reader = new Utf8JsonReader( Encoding.UTF8.GetBytes( json ), new JsonReaderOptions { MaxDepth = 512 } );
		var absoluteFolder = Path.GetFullPath( folder ).Replace( '\\', '/' );
		var found = false;
		while ( reader.Read() )
		{
			if ( reader.TokenType != JsonTokenType.PropertyName || !(reader.ValueTextEquals( "Path" ) || reader.ValueTextEquals( "Model" ) || reader.ValueTextEquals( "Physics" )) )
				continue;

			if ( !reader.Read() || reader.TokenType != JsonTokenType.String )
				continue;

			var path = reader.GetString();
			var slash = path.LastIndexOf( '/' );
			if ( slash < 0 || !absoluteFolder.EndsWith( "/" + path[..slash].TrimStart( '/' ), StringComparison.OrdinalIgnoreCase )
				|| !(path.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) || path.EndsWith( ".vphys", StringComparison.OrdinalIgnoreCase )) )
				continue;

			found = true;
			if ( !File.Exists( Path.Combine( folder, path[(slash + 1)..] + "_c" ) ) )
				return false;
		}

		return found;
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
