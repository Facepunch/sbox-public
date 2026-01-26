using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Facepunch.AssemblySchema;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class UploadDocumentation( string name ) : Step( name )
{
	protected override ExitCode RunInternal()
	{
		try
		{
			return UploadDocumentationAsync().GetAwaiter().GetResult();
		}
		catch ( Exception ex )
		{
			Log.Error( $"Documentation upload failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	private async Task<ExitCode> UploadDocumentationAsync()
	{
		try
		{
			Log.Info( "Building schema from assemblies..." );
			var schema = BuildSchema();
			schema.StripNonPublic();

			var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
			var jsonSchema = JsonSerializer.Serialize( schema, jsonOptions );

			float sizeInMb = jsonSchema.Length / 1024.0f / 1024.0f;
			Log.Info( $"Uploading Release ({sizeInMb:0.00}mb)" );

			using var http = new HttpClient();
			http.Timeout = TimeSpan.FromMinutes( 5 );
			var url = "https://services.facepunch.com/sbox/release/create";

			var payload = new
			{
				schemaJson = jsonSchema,
				key = Environment.GetEnvironmentVariable( "DOCUMENTATION_UPLOAD_KEY" ),
				sha = Environment.GetEnvironmentVariable( "GITHUB_SHA" ),
				commitmessage = Environment.GetEnvironmentVariable( "COMMIT_MESSAGE" )
			};

			var json = JsonSerializer.Serialize( payload );
			var content = new StringContent( json, Encoding.UTF8, "application/json" );
			using var req = new HttpRequestMessage( HttpMethod.Post, url ) { Content = content };

			var response = await http.SendAsync( req );
			Log.Info( $"Response Was: {response.StatusCode}" );

			if ( !response.IsSuccessStatusCode )
			{
				var responseString = await response.Content.ReadAsStringAsync();
				Log.Error( $"Upload failed with status {response.StatusCode}: {responseString}" );
				return ExitCode.Failure;
			}

			Log.Info( "Documentation upload completed successfully" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Documentation upload error: {ex}" );
			return ExitCode.Failure;
		}
	}

	private Schema BuildSchema()
	{
		Log.Info( "Building documentation schema from assemblies" );

		string[] filesToDocument = new[]
		{
			"game/bin/managed/Sandbox.System.dll",
			"game/bin/managed/Sandbox.Engine.dll",
			"game/bin/managed/Sandbox.Event.dll",
			"game/bin/managed/Sandbox.Bind.dll",
			"game/bin/managed/Sandbox.Reflection.dll",
			"game/bin/managed/Sandbox.Tools.dll",
			"game/bin/managed/Sandbox.Filesystem.dll",
			"game/bin/managed/Sandbox.Compiling.dll",
			"game/bin/managed/Facepunch.ActionGraphs.dll",
			"game/.vs/output/Base Library.dll",
			"game/.vs/output/Base Editor Library.dll",
		};

		using var processor = new Builder();

		foreach ( var file in filesToDocument )
		{
			Log.Info( $"Processing {file}" );

			if ( !File.Exists( file ) )
			{
				Log.Warning( $"File not found: {file}, skipping" );
				continue;
			}

			try
			{
				using ( var ms = File.OpenRead( file ) )
				{
					var pdbFile = file.Replace( ".dll", ".pdb" );
					byte[] pdbBytes = default;

					if ( File.Exists( pdbFile ) )
					{
						pdbBytes = File.ReadAllBytes( pdbFile );
						Log.Info( $"Found PDB file: {pdbFile}" );
					}

					processor.AddAssembly( File.ReadAllBytes( file ), pdbBytes );

					var xmlFile = file.Replace( ".dll", ".xml" );
					if ( File.Exists( xmlFile ) )
					{
						processor.AddDocumentation( File.ReadAllBytes( xmlFile ) );
						Log.Info( $"Found XML documentation: {xmlFile}" );
					}
					else
					{
						Log.Warning( $"Documentation file not found: {xmlFile}" );
					}
				}
			}
			catch ( Exception ex )
			{
				Log.Error( $"Error processing {file}: {ex.Message}" );
			}
		}

		Log.Info( "Schema building completed" );
		return processor.Build();
	}
}
