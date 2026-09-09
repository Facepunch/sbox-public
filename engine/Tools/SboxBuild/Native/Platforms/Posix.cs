namespace Facepunch.Native;

/// <summary>
/// What the posix platforms share: one GNU makefile for everything, gcc-style toolchains, and the
/// lib&lt;name&gt; output naming. Linux and macOS differ only in toolchain names, shared library
/// format and a handful of defines and linker switches, which live behind the virtuals here.
/// </summary>
public abstract class Posix : NativePlatform
{
	public override bool IsWindows => false;

	public override string LibPublic => $"lib/public/{DirectoryName}";
	public override string LibCommon => $"lib/common/{DirectoryName}";

	// CC and CXX override these, which is how another compiler is asked for.
	public virtual string Compiler => "g++";
	public virtual string CCompiler => "gcc";
	public virtual string Archiver => "ar";
	public virtual string ObjCopy => "objcopy";
	public virtual string StripTool => "strip";

	/// <summary>What a shared library ends in: so or dylib.</summary>
	public abstract string SharedExtension { get; }

	/// <summary>-fpermissive, for gcc's conformance errors. clang does not raise them.</summary>
	public virtual bool Permissive => true;

	/// <summary>GNU ld: --start-group/--end-group around mutually dependent static libraries.</summary>
	public virtual bool GnuLd => true;

	/// <summary>objcopy the debug info out to a .dbg beside the binary. ld64 has no objcopy.</summary>
	public virtual bool SplitDebugSymbols => true;

	/// <summary>How the dynamic loader spells "the directory of the thing loading": $ORIGIN or @loader_path.</summary>
	public virtual string RpathOrigin => "$ORIGIN";

	/// <summary>Defines naming the OS, sitting between the shared posix and 64 bit blocks.</summary>
	protected abstract string[] OsDefines { get; }

	/// <summary>Architecture and debug info switches, which is where the OSes and arches diverge.</summary>
	protected abstract string[] MachineOptions { get; }

	/// <summary>How a shared library is linked, install name and all.</summary>
	protected abstract string[] SharedLinkOptions( Module module );

	/// <summary>Libraries every binary links.</summary>
	protected abstract string[] SystemLibs { get; }

	/// <summary>Include directories papering over what the platform's libc spells differently.</summary>
	protected virtual string[] CompatIncludes => [];

	/// <summary>System frameworks every binary links, for the platform that has frameworks.</summary>
	protected virtual string[] Frameworks => [];

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
		_ => $"lib{module.OutputName}.{SharedExtension}"
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

		// -Otarget so a parallel build's output stays one message per module. Apple still ships
		// GNU make 3.81, which predates -O.
		var output = IsOsx ? "" : " -Otarget";
		return Utility.RunProcess( "make", $"{makefile} -j{Environment.ProcessorCount}{output}", "src" );
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
				"GNUC", "POSIX=1", "_POSIX=1", "COMPILER_GCC" );
			config.Define( OsDefines );
			config.Define(
				"PLATFORM_64BITS",
				"_FILE_OFFSET_BITS=64",
				"FRAME_POINTER_OMISSION_DISABLED",
				"PARTNER_BRANCH", "BRANCH_MAIN", "LANG_CXX11",
				"ALLOW_FLAT_VR_MODES=1",
				$"_DLL_EXT=.{SharedExtension}", "_DLL_PREFIX=lib", $"_EXTERNAL_DLL_EXT=.{SharedExtension}" );

			if ( !options.Buildbot ) config.Define( "DEV_BUILD" );
			if ( options.Retail ) config.Define( "RETAIL", "_RETAIL" );
			if ( module.Strict ) config.Define( "STRICT_TYPE_CONVERSION_WARNINGS_ACTIVE=1" );
			if ( module.StrictHandles ) config.Define( "REQUIRE_SPECIFIC_RESOURCE_HANDLE_VALID_METHOD=1" );
			if ( lib ) config.Define( "_LIB", $"LIBNAME={module.OutputName}" );
			else config.Define( $"DLLNAME={module.OutputName}" );

			config.Define( debug ? "_DEBUG" : "NDEBUG" );

			config.Include( ".", "common", "public", "public/tier0", "thirdparty/sdl3/include" );
			config.Include( CompatIncludes );

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

			config.Option( MachineOptions );
			config.Option( "-Usprintf", "-Ustrncpy", "-UPROTECTED_THINGS_ENABLE" );

			// What MSVC's /fp:fast licenses. Not -ffast-math: it implies -ffinite-math-only, and mathlib
			// validates floats through IsFinite.
			config.Option( "-ffp-contract=fast", "-fno-math-errno", "-fno-signed-zeros",
				"-fno-trapping-math", "-fassociative-math", "-freciprocal-math" );

			config.Option( debug ? "-O0" : "-O2" );
			config.Option( "-w" );

			if ( lib ) continue;

			if ( !exe ) config.LinkOptions.AddRange( SharedLinkOptions( module ) );
			config.LinkOptions.Add( $"-Wl,-rpath,{RpathOrigin}" );

			// SDL3 ships beside the engine, but a build time tool runs from devtools and still needs it.
			var sdl = Paths.Relative( OutputDir( module ), SdlDir ).Replace( '\\', '/' );
			config.LinkOptions.Add( $"-Wl,-rpath,{RpathOrigin}/{sdl}" );

			config.LinkLibs.AddRange( SystemLibs );
			config.LinkOptions.AddRange( Frameworks.Select( f => $"-framework {f}" ) );
			config.LibDirs.AddRange( [SdlDir, Paths.BinDir] );
		}
	}

	/// <summary>The vendored SDL3, which tier0 links and every binary then needs at runtime.</summary>
	public string SdlDir => $"thirdparty/sdl3/lib/{DirectoryName}";
}
