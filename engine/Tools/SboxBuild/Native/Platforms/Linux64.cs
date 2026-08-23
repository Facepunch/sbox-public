namespace Facepunch.Native;

/// <summary>
/// x86_64 Linux (the steam runtime), generating one GNU makefile.
/// </summary>
public sealed class Linux64 : NativePlatform
{
	// gcc is what the steam runtime ships. CC and CXX override it, which is how clang is asked for.
	public const string Compiler = "g++";
	public const string CCompiler = "gcc";
	public const string Archiver = "ar";
	public const string ObjCopy = "objcopy";
	public const string Strip = "strip";

	public override string Name => "linux64";

	// The shipped layout, the prebuilt libraries and the schema compiler all spell it the long way.
	public override string DirectoryName => "linuxsteamrt64";
	public override bool IsWindows => false;

	public override string LibPublic => $"lib/public/{DirectoryName}";
	public override string LibCommon => $"lib/common/{DirectoryName}";

	/// <summary>Nothing here builds a Windows only module, whatever it names.</summary>
	public override bool Skips( Module module ) => module.WindowsOnly;

	public override string OutputDir( Module module ) => module.Publish switch
	{
		Publish.Lib => LibPublic,
		Publish.Tools => Paths.ToolsDir,
		Publish.DevTools => Paths.DevToolsDir,
		_ => Paths.BinDir
	};

	public override string OutputFile( Module module ) => module.Kind switch
	{
		ModuleKind.Lib => $"lib{module.OutputName}.a",
		ModuleKind.Exe or ModuleKind.ConsoleExe => module.OutputName,
		_ => $"lib{module.OutputName}.so"
	};

	public override void Generate( List<Module> modules, Options options )
	{
		foreach ( var module in modules ) SchemaCompiler.WriteInfo( module );

		Makefile.Write( EverythingSolution( options ), modules, options );
		Log.Info( $"Generated a makefile for {modules.Count} native modules." );
	}

	/// <summary>One makefile carries the whole dependency graph, schema compiler included.</summary>
	public override IEnumerable<(string Name, bool AlwaysRebuild)> Solutions( Options options ) =>
		[(EverythingSolution( options ), false)];

	public override bool Build( string name, bool forceRebuild = false )
	{
		var makefile = $"-f {name}.mak SHELL=/bin/bash";

		if ( forceRebuild && !Utility.RunProcess( "make", $"{makefile} clean", "src" ) )
			return false;

		// -Otarget so a parallel build's output stays one message per module.
		return Utility.RunProcess( "make", $"{makefile} -j{Environment.ProcessorCount} -Otarget", "src" );
	}

	/// <summary>One makefile covers every module, so a single module is not generated on its own.</summary>
	public override void Generate( Module module, Options options ) => SchemaCompiler.WriteInfo( module );

	public override void Apply( Module module, Options options )
	{
		foreach ( var config in module.Configs )
		{
			module.Clang.Apply( config );

			bool debug = config.Name == "Debug";
			bool lib = module.Kind == ModuleKind.Lib;
			bool exe = module.Kind is ModuleKind.Exe or ModuleKind.ConsoleExe;

			config.Define(
				$"IS_{module.Name.ToUpperInvariant()}",
				$"PROJECTNAME={module.Name}",
				"SBOX=1",
				"GNUC", "POSIX=1", "_POSIX=1", "COMPILER_GCC",
				"LINUX=1", "_LINUX=1", "LINUXSTEAMRT64=1", "_LINUXSTEAMRT64=1",
				"PLATFORM_64BITS",
				"_FILE_OFFSET_BITS=64",
				"FRAME_POINTER_OMISSION_DISABLED",
				"PARTNER_BRANCH", "BRANCH_MAIN", "LANG_CXX11",
				"ALLOW_FLAT_VR_MODES=1",
				"_DLL_EXT=.so", "_DLL_PREFIX=lib", "_EXTERNAL_DLL_EXT=.so" );

			if ( !options.Buildbot ) config.Define( "DEV_BUILD" );
			if ( options.Retail ) config.Define( "RETAIL", "_RETAIL" );
			if ( module.Strict ) config.Define( "STRICT_TYPE_CONVERSION_WARNINGS_ACTIVE=1" );
			if ( module.StrictHandles ) config.Define( "REQUIRE_SPECIFIC_RESOURCE_HANDLE_VALID_METHOD=1" );
			if ( lib ) config.Define( "_LIB", $"LIBNAME={module.OutputName}" );
			else config.Define( $"DLLNAME={module.OutputName}" );

			config.Define( debug ? "_DEBUG" : "NDEBUG" );

			config.Include( ".", "common", "public", "public/tier0", "thirdparty/sdl3/include" );

			// There is no precompiled header here, but the sources still include it by name and it can sit
			// in a different directory than they do.
			if ( module.PrecompiledHeader is not null )
			{
				var root = Conventions.PchRoot( module );
				if ( root is not null )
				{
					config.Include( root );
					// MSVC force includes the precompiled header into every source (/FI). The sources lean
					// on that rather than including it themselves, so gcc has to be told the same with
					// -include, or everything the header pulls in goes missing.
					config.ForceInclude( module.PrecompiledHeader.Replace( '\\', '/' ) );
				}
			}

			// The engine reads one type through a pointer to another, which -O2 is otherwise free to
			// miscompile.
			config.Option( "-fno-strict-aliasing" );

			// avx is the instruction set baseline, and what mathlib's CanCompileSSE3/SSE4/AVX resolve
			// against. cx16 is not implied by it, and threadinterlocks.h needs cmpxchg16b for its 128 bit
			// interlocks: without it the compiler calls out to libatomic instead.
			config.Option( "-m64", "-fPIC", "-fvisibility=hidden", "-mavx", "-mcx16",
				"-gdwarf-2", "-g2", "-fdiagnostics-show-option",
				"-Usprintf", "-Ustrncpy", "-UPROTECTED_THINGS_ENABLE" );

			// What MSVC's /fp:fast licenses. Not -ffast-math: it implies -ffinite-math-only, and mathlib
			// validates floats through IsFinite.
			config.Option( "-ffp-contract=fast", "-fno-math-errno", "-fno-signed-zeros",
				"-fno-trapping-math", "-fassociative-math", "-freciprocal-math" );

			config.Option( debug ? "-O0" : "-O2" );
			config.Option( "-w" );

			if ( lib ) continue;

			if ( !exe ) config.LinkOptions.AddRange( ["-shared", "-Wl,--no-undefined"] );
			config.LinkOptions.Add( "-Wl,-rpath,$ORIGIN" );

			// SDL3 ships beside the engine, but a build time tool runs from devtools and still needs it.
			var sdl = Paths.Relative( OutputDir( module ), SdlDir ).Replace( '\\', '/' );
			config.LinkOptions.Add( $"-Wl,-rpath,$ORIGIN/{sdl}" );

			// atomic: the 128 bit interlocks in threadinterlocks.h have no inline instruction.
			config.LinkLibs.AddRange( ["dl", "pthread", "m", "rt", "atomic", "uuid", "SDL3"] );
			config.LibDirs.AddRange( [SdlDir, Paths.BinDir] );
		}
	}

	/// <summary>The vendored SDL3, which tier0 links and every binary then needs at runtime.</summary>
	public string SdlDir => $"thirdparty/sdl3/lib/{DirectoryName}";

}
