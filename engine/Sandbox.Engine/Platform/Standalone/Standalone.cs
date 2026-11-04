using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Sandbox;

public static class Standalone
{
	/// <summary>
	/// Where an exported game keeps its content, relative to the executable. Only content lives
	/// here - what the game is (its manifest and project config) is in the executable's resources.
	/// </summary>
	internal const string GamePath = "assets/";

	/// <summary>
	/// Name of the RCDATA resource holding the serialized <see cref="StandaloneManifest"/>.
	/// </summary>
	internal const string ManifestResourceName = "STANDALONE_MANIFEST";

	/// <summary>
	/// Name of the RCDATA resource holding the game's project config, the contents of its
	/// <c>.sbproj</c> at export time.
	/// </summary>
	internal const string ProjectConfigResourceName = "STANDALONE_PROJECT";

	/// <summary>
	/// If running in standalone, contains the properties of the standalone game
	/// </summary>
	internal static StandaloneManifest Manifest { get; private set; }

	/// <summary>
	/// If running in standalone, the game's project config as JSON, exactly as it was exported.
	/// </summary>
	internal static string ProjectConfigJson { get; private set; }

	internal static void Init()
	{
		if ( !Application.IsStandalone )
			return;

		//
		// Init Steam
		//
		Steamworks.SteamClient.Init( (int)Application.AppId );
	}

	/// <summary>
	/// Load the manifest and project config the exporter wrote into the game's executable as PE
	/// resources. They're read from this process's own module, so nothing on disk decides what game
	/// this is.
	/// </summary>
	internal static void LoadFromExecutable()
	{
		var manifestJson = ReadResource( ManifestResourceName );
		ProjectConfigJson = ReadResource( ProjectConfigResourceName );

		SetupFromManifest( JsonSerializer.Deserialize<StandaloneManifest>( manifestJson ) );
	}

	[DllImport( "kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode )]
	private static extern IntPtr FindResource( IntPtr hModule, string lpName, IntPtr lpType );

	[DllImport( "kernel32.dll", SetLastError = true )]
	private static extern IntPtr LoadResource( IntPtr hModule, IntPtr hResInfo );

	[DllImport( "kernel32.dll" )]
	private static extern IntPtr LockResource( IntPtr hResData );

	[DllImport( "kernel32.dll", SetLastError = true )]
	private static extern uint SizeofResource( IntPtr hModule, IntPtr hResInfo );

	private static readonly IntPtr RT_RCDATA = new( 10 );

	private static string ReadResource( string name )
	{
		if ( !OperatingSystem.IsWindows() )
			throw new PlatformNotSupportedException( "Standalone games are currently exported for Windows only" );

		// The main module - the exe itself, which is where the exporter put the resources
		var module = IntPtr.Zero;

		var info = FindResource( module, name, RT_RCDATA );
		if ( info == IntPtr.Zero )
			throw new FileNotFoundException( $"'{Environment.ProcessPath}' has no '{name}' resource - it wasn't produced by the standalone exporter", new Win32Exception() );

		var size = SizeofResource( module, info );
		var handle = LoadResource( module, info );
		if ( size == 0 || handle == IntPtr.Zero )
			throw new Win32Exception();

		var bytes = new byte[size];
		Marshal.Copy( LockResource( handle ), bytes, 0, bytes.Length );
		return Encoding.UTF8.GetString( bytes );
	}

	internal static void SetupFromManifest( StandaloneManifest manifest )
	{
		Manifest = manifest;

		_buildDate = Manifest.BuildDate;
	}

	private static DateTime _buildDate = DateTime.UnixEpoch;

	/// <summary>
	/// The date and time at which the current standalone game was built
	/// </summary>
	public static DateTime BuildDate => Application.IsStandalone ? _buildDate : DateTime.UnixEpoch;

	/// <summary>
	/// Is the current standalone game running in development mode?
	/// </summary>
	[Obsolete]
	public static bool IsDevelopmentBuild => false;

	/// <summary>
	/// The date and time at which the current standalone game was built
	/// </summary>
	[Obsolete( "Use BuildDate" )]
	public static DateTime VersionDate => BuildDate;

	/// <summary>
	/// Represents the current standalone game's version, specified by the developer
	/// </summary>
	[Obsolete]
	public static Version Version => new Version( 0, 0, 0 );
}
