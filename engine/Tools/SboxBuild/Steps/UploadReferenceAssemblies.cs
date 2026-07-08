using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using JetBrains.Refasmer;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Packages the managed assemblies that are required to compile a game with
/// <c>Sandbox.Compiler</c> into a single zip and POSTs it to the Facepunch
/// backend, which stores them as the reference assemblies used for server-side
/// compilation of user code.
/// </summary>
internal class UploadReferenceAssemblies( BuildTarget target = BuildTarget.Staging )
{
	private const string Endpoint = "https://public.facepunch.com/sbox/internal/reference-assemblies";
	private readonly BuildTarget _target = target;

	private static readonly string[] ReferenceAssemblies =
	{
		"game/bin/managed/Sandbox.System.dll",
		"game/bin/managed/Sandbox.Engine.dll",
		"game/bin/managed/Sandbox.Filesystem.dll",
		"game/bin/managed/Sandbox.Reflection.dll",
		"game/bin/managed/Sandbox.Mounting.dll",
		"game/bin/managed/Sandbox.Bind.dll",
		"game/bin/managed/Sandbox.Event.dll",
		"game/bin/managed/Facepunch.ActionGraphs.dll",
		"game/bin/managed/SkiaSharp.dll",
		"game/bin/managed/Microsoft.AspNetCore.Components.dll",
	};

	internal ExitCode Run()
	{
		return UploadAsync().GetAwaiter().GetResult();
	}

	private async Task<ExitCode> UploadAsync()
	{
		var key = Environment.GetEnvironmentVariable( "REFERENCE_ASSEMBLY_UPLOAD_KEY" );
		if ( string.IsNullOrWhiteSpace( key ) )
		{
			Log.Error( "REFERENCE_ASSEMBLY_UPLOAD_KEY is not set; cannot upload reference assemblies." );
			return ExitCode.Failure;
		}

		// The backend keys compiled UGC DLLs on Protocol.Api, so refuse to upload refs without it.
		if ( !TryReadProtocolApi( out var protocolApi ) )
			return ExitCode.Failure;

		// Collect the assemblies we want to ship as references.
		if ( !TryCollectAssemblies( out var files ) )
			return ExitCode.Failure;

		// Zip them up into a single archive.
		var zipPath = Path.Combine( Path.GetTempPath(), $"reference-assemblies-{Guid.NewGuid():N}.zip" );

		try
		{
			CreateArchive( files, zipPath );

			var zipBytes = await File.ReadAllBytesAsync( zipPath );
			Log.Info( $"Packaged {files.Count} assemblies into archive ({Utility.FormatSize( zipBytes.Length )})" );

			var channel = BuildTargetToSteamBranch( _target );
			var gitRef = GetRef();

			var requestUri = $"{Endpoint}?channel={Uri.EscapeDataString( channel )}&ref={Uri.EscapeDataString( gitRef )}";

			using var http = new HttpClient { Timeout = TimeSpan.FromMinutes( 10 ) };

			using var content = new ByteArrayContent( zipBytes );
			content.Headers.ContentType = new MediaTypeHeaderValue( "application/zip" );

			using var request = new HttpRequestMessage( HttpMethod.Post, requestUri ) { Content = content };
			request.Headers.TryAddWithoutValidation( "Authorization", $"Bearer {key}" );
			request.Headers.TryAddWithoutValidation( "X-Engine-Version", $"{protocolApi}" );

			Log.Info( $"Uploading reference assemblies (channel: {channel}, ref: {gitRef}, protocol: {protocolApi})..." );

			using var response = await http.SendAsync( request );
			var body = await response.Content.ReadAsStringAsync();

			if ( !response.IsSuccessStatusCode )
			{
				Log.Error( $"Reference assembly upload failed with status {response.StatusCode}: {body}" );
				return ExitCode.Failure;
			}

			Log.Info( "Reference assembly upload completed successfully" );
			return ExitCode.Success;
		}
		finally
		{
			try { if ( File.Exists( zipPath ) ) File.Delete( zipPath ); } catch { }
		}
	}

	/// <summary>
	/// Reads <c>Protocol.Api</c> out of the engine source - the packaged assemblies are refasmed
	/// (bodies stripped), so it can't be read from the DLLs. Fails loudly so a Protocol.cs
	/// refactor breaks CI instead of silently shipping unversioned refs.
	/// </summary>
	private static bool TryReadProtocolApi( out int api )
	{
		api = 0;

		const string source = "engine/Sandbox.Engine/Protocol.cs";

		if ( !File.Exists( source ) )
		{
			Log.Error( $"Cannot determine Protocol.Api: {source} not found." );
			return false;
		}

		var match = Regex.Match( File.ReadAllText( source ), @"public\s+static\s+int\s+Api\s*=>\s*(\d+)" );

		if ( !match.Success || !int.TryParse( match.Groups[1].Value, out api ) )
		{
			Log.Error( $"Cannot determine Protocol.Api: pattern not found in {source}." );
			return false;
		}

		return true;
	}

	/// <summary>
	/// Resolves the list of assemblies to package. Returns false if a required
	/// assembly is missing.
	/// </summary>
	private bool TryCollectAssemblies( out List<string> files )
	{
		files = new List<string>();

		foreach ( var file in ReferenceAssemblies )
		{
			if ( !File.Exists( file ) )
			{
				Log.Error( $"Required reference assembly not found: {file}" );
				return false;
			}

			files.Add( file );
		}

		return true;
	}

	/// <summary>
	/// The identifier this build is stored under. Prefers the git tag name when the
	/// build was triggered by a tag, otherwise the short commit hash. Falls back to a
	/// timestamp for local runs with no git context.
	/// </summary>
	private static string GetRef()
	{
		if ( Environment.GetEnvironmentVariable( "GITHUB_REF" )?.StartsWith( "refs/tags/" ) == true )
		{
			var tag = Environment.GetEnvironmentVariable( "GITHUB_REF_NAME" );
			if ( !string.IsNullOrWhiteSpace( tag ) )
				return tag;
		}

		var sha = Environment.GetEnvironmentVariable( "GITHUB_SHA" );
		if ( !string.IsNullOrWhiteSpace( sha ) )
			return sha[..Math.Min( sha.Length, 7 )];

		return $"local-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
	}

	private static void CreateArchive( IReadOnlyList<string> files, string zipPath )
	{
		if ( File.Exists( zipPath ) )
			File.Delete( zipPath );

		var refDir = Path.Combine( Path.GetTempPath(), $"refasm-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( refDir );

		try
		{
			var logger = new LoggerBase( new RefasmerLogger() );

			using var archive = ZipFile.Open( zipPath, ZipArchiveMode.Create );

			foreach ( var file in files )
			{
				var name = Path.GetFileName( file );
				var refPath = Path.Combine( refDir, name );

				// Turn them into actual ref assemblies (smaller, more portable)
				MetadataImporter.MakeRefasm( file, refPath, logger, omitNonApiMembers: true, filter: null, makeMock: false );

				archive.CreateEntryFromFile( refPath, name, CompressionLevel.Optimal );
			}
		}
		finally
		{
			try { Directory.Delete( refDir, true ); } catch { }
		}
	}

	private sealed class RefasmerLogger : JetBrains.Refasmer.ILogger
	{
		public bool IsEnabled( JetBrains.Refasmer.LogLevel logLevel ) => logLevel >= JetBrains.Refasmer.LogLevel.Warning;

		public void Log( JetBrains.Refasmer.LogLevel logLevel, string message )
		{
			if ( logLevel >= JetBrains.Refasmer.LogLevel.Error )
				Facepunch.Log.Error( $"refasmer: {message}" );
			else if ( logLevel >= JetBrains.Refasmer.LogLevel.Warning )
				Facepunch.Log.Info( $"refasmer: {message}" );
		}
	}
}
