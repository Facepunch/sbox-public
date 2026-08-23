namespace Facepunch.Native;

/// <summary>
/// Dependencies downloaded from Facepunch/sbox-thirdparty during the build. To bump one,
/// run its workflow there and change Version here.
/// </summary>
public static class RemoteDeps
{
	/// <summary>
	/// Releases are tagged "name-version" with assets "name-version-platform.zip|tar.gz".
	/// </summary>
	public sealed record Dep( string Name, string Version, string Dir )
	{
		public string Tag => $"{Name}-{Version}";

		/// <summary>Platforms this publishes, or null for all of them.</summary>
		public string[] Platforms { get; init; }

		/// <summary>Where the run time binaries go as well as beside the headers. More than one
		/// where a build time tool loads them from its own directory too.</summary>
		public string[] RuntimeDir { get; init; } = ["../game/bin"];

		/// <summary>Whether RuntimeDir takes executables, not just shared libraries.</summary>
		public bool RuntimeExecutables { get; init; }

		/// <summary>An archive directory already laid out as it should appear under RuntimeDir,
		/// for a release that ships more than we hand to the engine.</summary>
		public string RuntimeTree { get; init; }

		public bool SupportsPlatform( string platform ) =>
			Platforms is null || Platforms.Contains( platform, StringComparer.OrdinalIgnoreCase );
	}

	/// <summary>The platforms that build everything. The others are console or arm only.</summary>
	private static readonly string[] DESKTOP = ["win64", "linuxsteamrt64"];
	private static readonly string[] WIN = ["win64"];

	public static readonly Dep[] All =
	[
		// schemacompiler and the other devtools link tier0, which needs SDL3 beside them.
		new( "sdl3", "release-3.4.14", "thirdparty/sdl3" )
			{ RuntimeDir = ["../game/bin", "devtools/bin"] },
		new( "dav1d", "1.5.3", "thirdparty/dav1d" ),
		new( "libcurl", "8.12.1", "thirdparty/libcurl" ),
		new( "libopus", "v1.5.2", "thirdparty/libopus" ),
		new( "libvpx", "v1.16.0", "thirdparty/libvpx" ),
		new( "libwebp", "v1.5.0", "thirdparty/libwebp" ),
		new( "libwebm", "1.0.0.32", "thirdparty/libwebm" ),
		new( "libyuv", "4afb965", "thirdparty/libyuv" ),
		new( "svtav1", "v4.1.0", "thirdparty/svtav1" ),
		new( "slang", "v2026.14", "thirdparty/slang" ),
		new( "glslang", "14.3.0", "thirdparty/glslang" ),
		new( "dxc", "v1.9.2607", "thirdparty/dxc" ),
		new( "openexr", "v2.5.8", "thirdparty/openexr" ),
		new( "alembic", "1.7.16", "thirdparty/alembic" ),
		new( "lame", "3.100", "thirdparty/lame" ),
		new( "oidn", "v1.4.3", "thirdparty/oidn" ) { Platforms = DESKTOP },
		new( "embree", "v3.13.5", "thirdparty/embree" ) { Platforms = DESKTOP },
		new( "bc7enc", "main", "thirdparty/bc7enc" ) { Platforms = DESKTOP },
		new( "ispc-texcomp", "master", "thirdparty/ispc-texcomp" ) { Platforms = DESKTOP },
		new( "openxr-loader", "release-1.1.43", "thirdparty/openxr" ) { Platforms = DESKTOP },
		// Qt builds far more than the editor loads. Its release names the shipped set in a
		// runtime tree, already laid out the way it lands beside the engine.
		new( "qt5", "master", "thirdparty/qt5" ) { Platforms = DESKTOP, RuntimeTree = "runtime" },
		// The crash handler is an executable, and only Windows builds minidump.cpp.
		new( "sentry-native", "0.11.3", "thirdparty/sentry" )
			{ Platforms = WIN, RuntimeExecutables = true },
	];
}
