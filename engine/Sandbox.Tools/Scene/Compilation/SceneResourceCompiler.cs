using Sandbox.Resources;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor;

[Expose]
[ResourceIdentity( "scene" )]
internal class SceneResourceCompiler : ResourceCompiler
{
	protected override Task<bool> Compile()
	{
		if ( Context is not ResourceCompileContextImp context )
			throw new InvalidOperationException( "Scene compilation requires an editor resource context." );

		JsonObject json;
		if ( context.TryReadOverrideInput( out var input ) )
		{
			using var reader = new StreamReader( new MemoryStream( input ) );
			json = JsonNode.Parse( reader.ReadToEnd(), documentOptions: new() { MaxDepth = 512, CommentHandling = JsonCommentHandling.Skip } ) as JsonObject
				?? throw new JsonException( "Scene compile input must be a JSON object." );
		}
		else
		{
			// A compile from source (save, checkout, publish) keeps the bake while the scene is the one it was made from.
			context.AddCompileReference( SceneBake.FilePath( context.AbsolutePath ), optional: true );
			json = SceneBake.TryRead( context.AbsolutePath );
		}

		if ( json is null || !json.Remove( "__blobdata", out var inlineBlob ) )
		{
			AssetSystem.CompileGameResource( context );
			return Task.FromResult( true );
		}

		var blobData = Convert.FromBase64String( inlineBlob?.GetValue<string>() ?? throw new JsonException( "Scene compile input is missing its binary data." ) );
		context.AddCompileReference( context.AbsolutePath );
		context.AddCompileReference( context.AbsolutePath + "_d", optional: true );
		context.Data.Write( context.ScanJson( json.ToJsonString( new JsonSerializerOptions( JsonSerializerOptions.Default ) { MaxDepth = 512 } ) ) );

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

		return Task.FromResult( true );
	}
}
