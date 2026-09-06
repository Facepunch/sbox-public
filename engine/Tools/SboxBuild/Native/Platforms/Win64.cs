using System.Diagnostics;

namespace Facepunch.Native;

/// <summary>
/// x64 Windows with MSVC, generating .vcxproj and .slnx.
/// </summary>
public sealed class Win64 : NativePlatform
{
	public const string Toolset = "v145";
	public const string WindowsSdk = "10.0";
	public const string MinimumVisualStudio = "18.9";

	public const int MpCount = 8;

	public override string Name => "win64";
	public override bool IsWindows => true;

	// MSVC keeps its libraries in a toolset subfolder; the posix builds do not.
	public override string LibPublic => $"lib/public/{Name}/vc14";
	public override string LibCommon => $"lib/common/{Name}/vc14";

	public override string OutputDir( Module module ) => module.Publish switch
	{
		Publish.Lib => LibPublic,
		Publish.Tools => Paths.ToolsDir,
		Publish.DevTools => Paths.DevToolsDir,
		_ => Paths.BinDir
	};

	public override string OutputFile( Module module ) => module.Kind switch
	{
		ModuleKind.Lib => $"{module.OutputName}.lib",
		ModuleKind.Exe or ModuleKind.ConsoleExe => $"{module.OutputName}.exe",
		_ => $"{module.OutputName}.dll"
	};

	public override void Apply( Module module, Options options )
	{
		foreach ( var config in module.Configs )
		{
			module.Msvc.Apply( config );
			Output( module, config, options );
			Common( module, config, options );

			if ( config.Name == "Debug" ) Debug( module, config, options );
			else Release( module, config, options );
		}

		module.ResolvedFiles.Add( new SourceFile { Path = "devtools/engine2.natvis", Kind = FileKind.Natvis } );
	}

	public override void Generate( List<Module> modules, Options options )
	{
		foreach ( var module in modules ) Generate( module, options );

		Solution.Write( SchemaCompilerSolution, Solution.WithDependencies( modules.Where( m => m.Name is SchemaCompiler.ToolChild or SchemaCompiler.Tool ) ) );
		Solution.Write( EverythingSolution( options ), modules );

		if ( options.Retail )
			Solution.Write( ToolsSolution, Solution.WithDependencies( modules.Where( m => Tools.Contains( m.Name ) ) ) );

		Log.Info( $"Generated {modules.Count} native projects." );
	}

	private const string SchemaCompilerSolution = "schemacompiler_all";

	/// <summary>The editors, which Retail builds again on their own so they are never incremental.</summary>
	private static readonly string[] Tools = ["hammer", "modeldoc_editor", "animgraph_editor"];

	private string ToolsSolution => $"buildbot_tools_{Name}";

	public override IEnumerable<(string Name, bool AlwaysRebuild)> Solutions( Options options )
	{
		// The schema compiler has to exist before anything that uses schema can build.
		yield return (SchemaCompilerSolution, false);
		yield return (EverythingSolution( options ), false);

		// Tools are never built incrementally in Retail.
		if ( options.Retail ) yield return (ToolsSolution, true);
	}

	public override bool Build( string name, bool forceRebuild = false )
	{
		var vsDevCmd = FindVsDevCmd();
		if ( string.IsNullOrEmpty( vsDevCmd ) )
		{
			Log.Error( "Could not find the Visual Studio Developer Command Prompt. Ensure Visual Studio is installed and accessible." );
			return false;
		}

		var target = forceRebuild ? "/t:Rebuild" : "/t:Build";
		var msbuild = $"msbuild {name}.slnx {target} /p:Configuration=Release /p:Platform=x64 /m /v:minimal /clp:Summary";

		return Utility.RunProcess( "cmd.exe", $"/c \"call \"{vsDevCmd}\" -no_logo && cd src && {msbuild}\"", null );
	}

	/// <summary>VsDevCmd.bat sets the environment msbuild and the compilers need.</summary>
	private static string FindVsDevCmd()
	{
		using var vsWhere = new Process();
		vsWhere.StartInfo.FileName = @"src\devtools\bin\win64\vswhere";
		vsWhere.StartInfo.Arguments = $"-latest -prerelease -version {MinimumVisualStudio} -products Microsoft.VisualStudio.Product.Enterprise Microsoft.VisualStudio.Product.Professional Microsoft.VisualStudio.Product.Community Microsoft.VisualStudio.Product.BuildTools -property installationPath";
		vsWhere.StartInfo.UseShellExecute = false;
		vsWhere.StartInfo.RedirectStandardOutput = true;
		vsWhere.StartInfo.CreateNoWindow = true;
		vsWhere.Start();

		var installation = vsWhere.StandardOutput.ReadToEnd().Trim();
		vsWhere.WaitForExit();

		if ( string.IsNullOrEmpty( installation ) )
		{
			Log.Error( $"No Visual Studio {MinimumVisualStudio} or newer installation found. The native build needs MSBuild {MinimumVisualStudio}; update Visual Studio." );
			return string.Empty;
		}

		var path = Path.Combine( installation, @"Common7\Tools\VsDevCmd.bat" );
		if ( File.Exists( path ) ) return path;

		Log.Error( $"Could not find VsDevCmd.bat at {path}" );
		return string.Empty;
	}

	public override void Generate( Module module, Options options )
	{
		Vcxproj.Write( module, options );
		SchemaCompiler.WriteInfo( module );

		// Written when the project is generated, so the stale check passes until the definition changes.
		var sentinel = Path.Combine( Paths.SrcDir, module.Dir, Registry.Sentinel( module ) );
		Directory.CreateDirectory( Path.GetDirectoryName( sentinel ) );
		File.WriteAllText( sentinel, module.Name );
	}

	private static void Set( Dictionary<string, string> values, string key, string value ) => values.TryAdd( key, value );

	/// <summary>
	/// Where the build puts things. Binaries link straight to their final home, so there is no copy step
	/// and nothing to keep in sync.
	/// </summary>
	private void Output( Module module, Config config, Options options )
	{
		var msvc = module.Msvc;
		bool lib = module.Kind == ModuleKind.Lib;
		bool exe = module.Kind is ModuleKind.Exe or ModuleKind.ConsoleExe;

		Set( config.Settings, "ConfigurationType", lib ? "StaticLibrary" : exe ? "Application" : "DynamicLibrary" );
		Set( config.Settings, "TargetName", module.OutputName );
		Set( config.Settings, "CharacterSet", "MultiByte" );
		Set( config.Settings, "PlatformToolset", Toolset );
		Set( config.Settings, "PreferredToolArchitecture", "x64" );

		// Forward slashes on purpose: rc.exe reads a backslash-t in a generated path as a tab.
		Set( config.Properties, "OutDir", Paths.Relative( module.Dir, OutputDir( module ) ).Replace( '\\', '/' ) + "/" );
		// Includes the module name so two modules in one directory cannot share intermediates.
		Set( config.Properties, "IntDir", $"obj/{module.Name}/{config.Name}/{Name}/" );
		Set( config.Properties, "TargetExt", lib ? ".lib" : exe ? ".exe" : ".dll" );
		Set( config.Properties, "GenerateManifest", "false" );
		Set( config.Properties, "CustomBuildBeforeTargets", "ClCompile" );

		if ( msvc.StackSize is not null ) Set( config.Link, "StackReserveSize", $"0x{msvc.StackSize:X}" );
		if ( msvc.MinWindowsVersion is not null ) Set( config.Link, "MinimumRequiredVersion", msvc.MinWindowsVersion );
		if ( msvc.ModuleDefinition is not null ) Set( config.Link, "ModuleDefinitionFile", msvc.ModuleDefinition );
		if ( msvc.MergeLibraries.Count > 0 ) Set( config.Lib, "AdditionalDependencies", string.Join( ';', msvc.MergeLibraries ) );

		if ( lib )
		{
			Set( config.Lib, "TargetMachine", "MachineX64" );
			Set( config.Lib, "SuppressStartupBanner", "true" );
			config.Cl["ProgramDataBaseFileName"] = $"$(OutDir){module.OutputName}.pdb";
			config.Define( "_LIB", $"LIBNAME={module.OutputName}" );
			return;
		}

		bool incremental = !options.Retail && !options.MemoryDebug;

		config.LibDirs.AddRange( [LibCommon, LibPublic] );
		Set( config.Properties, "LinkIncremental", incremental ? "true" : "false" );
		Set( config.Link, "TargetMachine", "MachineX64" );
		Set( config.Link, "SubSystem", module.Kind == ModuleKind.ConsoleExe ? "Console" : "Windows" );
		Set( config.Link, "GenerateDebugInformation", "true" );
		Set( config.Link, "ProgramDatabaseFile", "$(OutDir)$(TargetName).pdb" );
		Set( config.Link, "LargeAddressAware", "true" );
		Set( config.Link, "LinkLibraryDependencies", "false" );
		Set( config.Link, "RandomizedBaseAddress", options.Retail ? "true" : "false" );
		Set( config.Link, "LinkErrorReporting", "PromptImmediately" );
		Set( config.Link, "OptimizeReferences", incremental ? "false" : "true" );
		Set( config.Link, "EnableCOMDATFolding", options.Retail ? "true" : "false" );
		config.LinkOptions.Add( "/ignore:4221" );
		config.LinkLibs.AddRange( ["legacy_stdio_definitions.lib", "user32.lib", "advapi32.lib", "gdi32.lib"] );

		// Consumers look for import libs in lib/public, wherever the binary itself lands.
		if ( !exe ) Set( config.Link, "ImportLibrary", Paths.Relative( module.Dir, $"{LibPublic}/{module.OutputName}.lib" ) );

		config.Define( exe ? $"EXENAME={module.OutputName}" : "_USRDLL" );
		if ( !exe ) config.Define( $"DLLNAME={module.OutputName}" );
	}

	private static void Common( Module module, Config config, Options options )
	{
		config.Define(
			$"IS_{module.Name.ToUpperInvariant()}",
			$"PROJECTNAME={module.Name}",
			"SBOX=1",
			"FRAME_POINTER_OMISSION_DISABLED",
			"PARTNER_BRANCH",
			"BRANCH_MAIN",
			"LANG_CXX11",
			"ALLOW_FLAT_VR_MODES=1",
			"WIN32", "_WIN32", "_WINDOWS", "V_COMPILER_MSVC",
			"_CRT_SECURE_NO_DEPRECATE", "_CRT_NONSTDC_NO_DEPRECATE",
			"_HAS_ITERATOR_DEBUGGING=0",
			"V_COMPILER_MSVC64", "WIN64", "_WIN64", "PLATFORM_64BITS", "_M_AMD64",
			"_HAS_STD_BYTE=0", "_HAS_AUTO_PTR_ETC=1",
			"_SILENCE_CXX17_ITERATOR_BASE_CLASS_DEPRECATION_WARNING=1",
			"_SILENCE_CXX17_CODECVT_HEADER_DEPRECATION_WARNING=1",
			"VALVE_MSC_VER=1950",
			// tier0 builds library names from these at runtime.
			"_DLL_EXT=.dll", "_DLL_PREFIX=", "_EXTERNAL_DLL_EXT=.dll" );

		// binlaunch loads the dll named after it, so a copy of it named after this module runs the module.
		if ( module.Launcher )
		{
			var launcher = Paths.Relative( module.Dir, $"{Paths.DevToolsDir}/binlaunch.exe" );
			config.PostBuild.Add( $@"copy /y ""{launcher}"" ""$(OutDir)$(TargetName).exe""" );
			config.PostBuild.Description = "Copying binlaunch as the module's executable";
		}

		if ( !options.Buildbot ) config.Define( "DEV_BUILD" );
		if ( options.Retail ) config.Define( "RETAIL", "_RETAIL" );
		if ( module.Tracy && !options.Retail ) config.Define( "TRACY_ENABLE", "TRACY_ON_DEMAND", "TRACY_DELAYED_INIT", "TRACY_MANUAL_LIFETIME", "TRACY_TIMER_FALLBACK" );

		if ( module.Strict ) config.Define( "STRICT_TYPE_CONVERSION_WARNINGS_ACTIVE=1" );
		else config.NoWarn( ["4018", "4244", "4389"] );

		if ( module.StrictHandles ) config.Define( "REQUIRE_SPECIFIC_RESOURCE_HANDLE_VALID_METHOD=1" );

		config.Include( "common", "public", "public/tier0", "thirdparty/sdl3/include" );

		if ( !module.Msvc.MultiProcessor ) Set( config.Cl, "MultiProcessorCompilation", "false" );
		else config.Option( $"/MP{MpCount}" );

		if ( !module.Msvc.Rtti ) Set( config.Cl, "RuntimeTypeInfo", "false" );
		if ( module.CompileAsC ) config.Cl["CompileAs"] = "CompileAsC";
		config.Option( "/std:c++20", "/permissive", "/Zc:__cplusplus", "/Wv:18", "/Gw", "/bigobj" );
		if ( !module.ThirdParty ) config.Option( "/w14555" );

		Set( config.Cl, "StringPooling", "true" );
		Set( config.Cl, "MinimalRebuild", "false" );
		Set( config.Cl, "ExceptionHandling", "false" );
		Set( config.Cl, "RuntimeTypeInfo", "true" );
		Set( config.Cl, "EnableEnhancedInstructionSet", "AdvancedVectorExtensions" );
		Set( config.Cl, "FloatingPointModel", "Fast" );
		Set( config.Cl, "ForceConformanceInForLoopScope", "true" );
		Set( config.Cl, "WarningLevel", module.ThirdParty ? "Level2" : "Level4" );
		Set( config.Cl, "TreatWarningAsError", module.ThirdParty ? "false" : "true" );
		Set( config.Cl, "SuppressStartupBanner", "true" );
		Set( config.Cl, "UseFullPaths", "true" );
		Set( config.Cl, "CompileAs", "CompileAsCpp" );
		Set( config.Cl, "ErrorReporting", "Prompt" );
		Set( config.Cl, "ObjectFileName", "$(IntDir)" );
		Set( config.Cl, "ProgramDataBaseFileName", "$(IntDir)" );
		Set( config.Cl, "PrecompiledHeader", module.PrecompiledHeader is null ? "NotUsing" : "Use" );

		if ( module.PrecompiledHeader is not null )
		{
			Set( config.Cl, "PrecompiledHeaderFile", module.PrecompiledHeader );

			// Force it in rather than relying on every source including it first; NoPch files get the
			// force-include cleared again by the project writer.
			if ( !config.ForceIncludes.Contains( module.PrecompiledHeader ) ) config.ForceIncludes.Add( module.PrecompiledHeader );

			var root = Conventions.PchRoot( module );
			if ( root is not null && !config.Includes.Contains( root ) ) config.Include( root );
		}

		config.NoWarn( DisabledWarnings );
	}

	private static void Debug( Module module, Config config, Options options )
	{
		config.Define( "_DEBUG", "DEBUG", "_ALLOW_RUNTIME_LIBRARY_MISMATCH", "_ALLOW_ITERATOR_DEBUG_LEVEL_MISMATCH", "_ALLOW_MSC_VER_MISMATCH", "_ALLOW_COMPILER_AND_STL_VERSION_MISMATCH" );

		Set( config.Cl, "Optimization", "Disabled" );
		Set( config.Cl, "DebugInformationFormat", module.Msvc.EditAndContinue ? "EditAndContinue" : "ProgramDatabase" );
		Set( config.Cl, "BasicRuntimeChecks", "Default" );
		Set( config.Cl, "RuntimeLibrary", "MultiThreadedDebug" );
		Set( config.Cl, "BufferSecurityCheck", "true" );

		Set( config.Rc, "Culture", "1033" );
		Set( config.Rc, "PreprocessorDefinitions", "_DEBUG;_CRT_SECURE_NO_DEPRECATE;_CRT_NONSTDC_NO_DEPRECATE" );
		config.IgnoreLibs.AddRange( ["libc", "libcd", "libcmt", "msvcrt", "msvcrtd", "msvcprt", "msvcprtd"] );
	}

	private static void Release( Module module, Config config, Options options )
	{
		config.Define( "NDEBUG", "_ALLOW_RUNTIME_LIBRARY_MISMATCH", "_ALLOW_ITERATOR_DEBUG_LEVEL_MISMATCH", "_ALLOW_MSC_VER_MISMATCH", "_ALLOW_COMPILER_AND_STL_VERSION_MISMATCH" );

		Set( config.Cl, "Optimization", "MaxSpeed" );
		Set( config.Cl, "DebugInformationFormat", "ProgramDatabase" );
		Set( config.Cl, "IntrinsicFunctions", "true" );
		Set( config.Cl, "FavorSizeOrSpeed", "Speed" );
		Set( config.Cl, "FunctionLevelLinking", "true" );
		Set( config.Cl, "RuntimeLibrary", "MultiThreaded" );
		Set( config.Cl, "BufferSecurityCheck", options.Retail ? "false" : "true" );
		config.Option( "/Ob3", "/d2Zi+" );

		// Only what links. A static library compiled with /GL carries compiler intermediates instead of
		// machine code, which makes every consumer's link generate its code again.
		if ( options.Retail && module.Kind != ModuleKind.Lib )
			Set( config.Settings, "WholeProgramOptimization", "true" );

		Set( config.Rc, "Culture", "1033" );
		Set( config.Rc, "PreprocessorDefinitions", "NDEBUG;_CRT_SECURE_NO_DEPRECATE;_CRT_NONSTDC_NO_DEPRECATE" );
		config.IgnoreLibs.AddRange( ["libc", "libcd", "libcmtd", "libcpmtd0", "msvcrt", "msvcrtd", "msvcprt", "msvcprtd"] );
	}

	private static readonly string[] DisabledWarnings =
	[
		"4061", "4062", "4091", "4100", "4121", "4127", "4189", "4201", "4250", "4255", "4265", "4316",
		"4324", "4350", "4351", "4355", "4371", "4435", "4464", "4481", "4505", "4511", "4512", "4514",
		"4530", "4544", "4571", "4574", "4577", "4587", "4592", "4611", "4619", "4623", "4625", "4626",
		"4628", "4640", "4647", "4668", "4702", "4710", "4711", "4738", "4748", "4774", "4786", "4820",
		"4868", "4917", "4946", "4986", "4987", "4996", "5026", "5027", "5029", "5031", "5032", "5033", "5040"
	];
}
