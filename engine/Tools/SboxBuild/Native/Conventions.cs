namespace Facepunch.Native;

/// <summary>
/// What every module gets regardless of platform: the standard sources, the shared schema anchor, and
/// where a precompiled header resolves from. Toolchain settings live on the <see cref="NativePlatform"/>.
/// </summary>
public static class Conventions
{
	/// <summary>
	/// The anchor includes every schematised module shares. Pre-includes and global types are not shared:
	/// the schema compiler treats a type listed as global differently from one it has to look at, so each
	/// module states its own.
	/// </summary>
	public static void Schema( Module module )
	{
		if ( !module.Schema.Any ) return;

		module.Schema.AnchorIncludes.InsertRange( 0, [
			"tier0/keyvalues.h", "resourcefile/resourcestream.h", "resourcesystem/resourcehandletypes.h",
			"animationsystem/animation_constants.h", "animationsystem/activity_handle.h",
			"soundsystem/soundflags.h", "engine2/engineevents.h", "vphysics2/interface.h",
			"scenesystem/isceneutils.h", "tier0/basetypes_schema.h", "tier0/interpolatedvar_wrapped.h",
			"scenesystem/sceneobject.h"] );
	}

	/// <summary>Sources every engine module gets.</summary>
	public static void Standard( Module module )
	{
		// The .cpp matching the precompiled header is the one that builds it. Matched by file name: the
		// header is named the way sources include it, which may carry a directory.
		if ( module.PrecompiledHeader is not null )
		{
			var creator = Path.GetFileNameWithoutExtension( module.PrecompiledHeader ) + ".cpp";
			var existing = module.ResolvedFiles.FirstOrDefault( f => Path.GetFileName( f.Path ).Equals( creator, StringComparison.OrdinalIgnoreCase ) );

			if ( existing is not null ) existing.CreatePch = true;
			else
			{
				var found = Paths.Glob( module.Dir, $"**/{creator}" ).FirstOrDefault();
				if ( found is not null ) module.File( $"/{found}", createPch: true );
			}
		}

		if ( module.Standalone ) return;

		if ( !module.StaticLink ) module.File( "/public/tier0/memoverride.cpp", noPch: true );
		module.File( "/common/all_projects_common_code.cpp", noPch: true );

		if ( module.Kind != ModuleKind.Lib && !module.StaticLink )
			module.File( "/public/tier0/crtoverride.cpp", noPch: true );
	}

	/// <summary>
	/// The directory that makes the precompiled header's include name resolve. Schema generated code sits
	/// outside the module and includes the header by that name, so it has to be searchable.
	/// </summary>
	public static string PchRoot( Module module )
	{
		var pch = module.PrecompiledHeader.Replace( '\\', '/' );

		for ( var dir = module.Dir; dir is not null; dir = Parent( dir ) )
		{
			var candidate = dir.Length == 0 ? pch : $"{dir}/{pch}";
			if ( File.Exists( Path.Combine( Paths.SrcDir, candidate ) ) ) return dir.Length == 0 ? "." : dir;
		}

		return module.Dir.Length > 0 ? module.Dir : null;
	}

	private static string Parent( string dir )
	{
		if ( dir.Length == 0 ) return null;
		var slash = dir.LastIndexOf( '/' );
		return slash < 0 ? "" : dir[..slash];
	}
}

/// <summary>Generation options, from the SboxBuild command line.</summary>
public sealed class Options
{
	/// <summary>Target platform. Decides both the settings and which project files come out.</summary>
	public string Platform = "win64";

	public bool Retail;
	public bool MemoryDebug;
	public bool Buildbot;
}
