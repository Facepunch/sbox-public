namespace Facepunch.Native;

public enum ModuleKind
{
	Dll,
	Lib,
	Exe,
	ConsoleExe
}

/// <summary>Where a module's output is linked to.</summary>
public enum Publish
{
	/// <summary>game/bin/&lt;platform&gt; - the engine and its modules.</summary>
	Bin,
	/// <summary>game/bin/&lt;platform&gt;/tools - editor tools.</summary>
	Tools,
	/// <summary>lib/public/&lt;platform&gt; - static libraries.</summary>
	Lib,
	/// <summary>devtools/bin/&lt;platform&gt; - build time tools.</summary>
	DevTools
}

public enum FileKind
{
	Compile,
	Include,
	None,
	Resource
}

public sealed class SourceFile
{
	/// <summary>Path relative to src/.</summary>
	public string Path;
	public FileKind Kind = FileKind.Compile;
	public bool NoPch;
	public bool CreatePch;
	public string CompileAs;

	/// <summary>Switches for this file alone, in the toolchain's own spelling.</summary>
	public List<string> Msvc = [], Clang = [];
	public List<string> Defines = [];
	public List<string> Includes = [];

	/// <summary>Config names this file is excluded from.</summary>
	public List<string> ExcludeFrom = [];

	public CustomBuild Build;
}

/// <summary>
/// A custom build step. %config% expands to the lowercase config name.
/// </summary>
public sealed class CustomBuild
{
	public string Message;
	public string Command;
	public List<string> Inputs = [];
	public List<string> Outputs = [];
	public List<string> PotentialOutputs = [];
}

/// <summary>
/// A native project. Derive from this in a src/&lt;module&gt;/&lt;name&gt;.build.cs file; it is discovered
/// by reflection. Paths are relative to the module directory; a leading / means relative to src/.
/// </summary>
public abstract class Module
{
	/// <summary>What the current run is generating for, so a module can branch on it.</summary>
	public static bool Windows => NativePlatform.Current.IsWindows;
	public static bool Linux => !Windows;
	public static bool Retail { get; internal set; }

	/// <summary>
	/// A prebuilt third party library under src/thirdparty, named the way the platform being
	/// built names it. Set shared for one that ships as a dll or so rather than statically.
	/// </summary>
	public static string ThirdPartyLib( string dir, string name, bool shared = false )
	{
		var path = $"thirdparty/{dir}/lib/{NativePlatform.Current.DirectoryName}";

		if ( Windows )
			return $"{path}/{name}.lib";

		// Some carry the prefix in the name already, as libwebp does.
		var prefix = name.StartsWith( "lib", StringComparison.Ordinal ) ? "" : "lib";
		return $"{path}/{prefix}{name}.{(shared ? "so" : "a")}";
	}

	public string Name;
	public ModuleKind Kind = ModuleKind.Lib;

	/// <summary>Output binary name. Defaults to the module name.</summary>
	public string OutputName { get => output ?? Name; set => output = value; }
	private string output;

	/// <summary>Directory of the .build.cs that declared this module, relative to src/.</summary>
	public string Dir { get; private set; }

	/// <summary>Settings for one configuration only, where the two genuinely differ.</summary>
	public sealed class Configured
	{
		public List<string> CompileDefinitions = [];
	}

	public Configured Debug = new(), Release = new();

	public Config DebugConfig = new( "Debug" );
	public Config ReleaseConfig = new( "Release" );
	public IEnumerable<Config> Configs => [DebugConfig, ReleaseConfig];

	public List<SourceFile> ResolvedFiles = [];

	/// <summary>Libraries anything linking this module also needs.</summary>
	public List<string> PublicLibs = [];

	/// <summary>
	/// IncludeDirectories directories anything depending on this module also gets, so a consumer only has to name the
	/// module. Same idea as Sharpmake's public dependencies and UBT's PublicIncludePaths.
	/// </summary>
	public List<string> PublicIncludes = [];

	/// <summary>Resolved link inputs, src-relative. Filled in by <see cref="Registry"/>.</summary>
	public List<string> Libraries = [];

	/// <summary>Modules that must build first. Filled in by <see cref="Registry"/>.</summary>
	public List<Module> Dependencies = [];

	/// <summary>
	/// Precompiled header, e.g. "engine2_pch.h". The matching .cpp creates it. Stored with forward slashes:
	/// it ends up inside generated #include directives, where a backslash before a letter is an escape.
	/// </summary>
	public string PrecompiledHeader { get => pch; set => pch = value?.Replace( '\\', '/' ); }
	private string pch;

	/// <summary>Skip tier0, memoverride and the other sources every engine module gets.</summary>
	public bool Standalone;

	/// <summary>Where the output is linked to. Defaults to the natural place for the kind.</summary>
	public Publish Publish { get => publish ?? (Kind == ModuleKind.Lib ? Publish.Lib : Publish.Bin); set => publish = value; }
	private Publish? publish;

	/// <summary>This module is only built on Windows.</summary>
	public bool WindowsOnly;


	/// <summary>Relax warnings for code we do not own.</summary>
	public bool ThirdParty;

	/// <summary>Signed/unsigned and narrowing conversions are errors, not warnings.</summary>
	public bool Strict;

	/// <summary>Resource handles must use their specific IsValid method.</summary>
	public bool StrictHandles;

	/// <summary>Links the CRT statically and skips memoverride.</summary>
	public bool StaticLink;

	/// <summary>Compile in the Tracy profiler.</summary>
	public bool Tracy = true;

	/// <summary>
	/// Ship an executable beside the dll, so the module can be run as a program as well as loaded. It is a
	/// copy of binlaunch, which loads the dll sitting next to it under its own name.
	/// </summary>
	public bool Launcher;

	/// <summary>Treat the module's sources as C.</summary>
	public bool CompileAsC;

	/// <summary>Settings only MSVC understands. The Windows platform is the only thing that reads them.</summary>
	public Msvc Msvc = new();

	/// <summary>Settings only clang understands. The posix platforms are the only thing that reads them.</summary>
	public Clang Clang = new();

	/// <summary>Run moc over headers that declare Q_OBJECT and compile the result.</summary>
	public bool Qt;

	/// <summary>Batch sources into unity files. Faster full builds, slower single file edits.</summary>
	public bool Unity;

	public SchemaSettings Schema = new();

	/// <summary>Headers whose classes are exposed to the schema compiler.</summary>
	public void SchemaFiles( params string[] files ) => Schema.Files.AddRange( files );

	public void SchemaPreIncludes( params string[] files ) => Schema.PreIncludes.AddRange( files );
	public void SchemaAnchorIncludes( params string[] files ) => Schema.AnchorIncludes.AddRange( files );
	public void SchemaGlobalTypes( params string[] types ) => Schema.GlobalTypes.AddRange( types );
	public void SchemaOmitTypes( params string[] types ) => Schema.OmitTypes.AddRange( types );

	/// <summary>
	/// Called once per configuration after the defaults are in place. Override it when a setting genuinely
	/// differs between configurations, instead of repeating a line for each.
	/// </summary>
	public virtual void Configure( Config config ) { }

	protected Module( string declaredIn = null )
	{
		if ( declaredIn is null )
		{
			(Name, Dir) = Paths.Locate( GetType().Name );
			return;
		}

		Name = GetType().Name.ToLowerInvariant();
		Dir = Paths.ToSrcRelative( System.IO.Path.GetDirectoryName( declaredIn ) );
	}

	/// <summary>Preprocessor defines, e.g. "ENGINE2" or "MAX_X=16".</summary>
	public List<string> CompileDefinitions = [];

	/// <summary>IncludeDirectories directories. Module relative; a leading / means relative to src/.</summary>
	public List<string> IncludeDirectories = [];

	/// <summary>Libraries to link. A bare name is a lib/public library and a build dependency.</summary>
	public List<string> LinkLibraries = [];

	/// <summary>Libraries whose consumers link them, without linking them here.</summary>
	public List<string> InterfaceLinkLibraries = [];

	/// <summary>Sources to compile, by glob. Recurses on **.</summary>
	public List<string> SourceFiles = [];

	/// <summary>Headers to show in the project. Browsing only: does not affect what gets built.</summary>
	public List<string> HeaderFiles = [];

	/// <summary>Files shown in the project but not built.</summary>
	public List<string> ExtraFiles = [];

	/// <summary>Resource scripts (.rc).</summary>
	public List<string> ResourceFiles = [];

	/// <summary>Files to leave out of the build, by glob. Applied after everything else.</summary>
	public List<string> ExcludeFiles = [];

	/// <summary>Files that must not use the precompiled header, by glob.</summary>
	public List<string> NoPchFiles = [];

	/// <summary>Files to keep out of unity batches, by glob.</summary>
	public List<string> NoUnityFiles = [];

	/// <summary>Sources to compile as C rather than C++, by glob.</summary>
	public List<string> CompileAsCFiles = [];

	/// <summary>Vendored trees to compile whole: no precompiled header, left out of unity batches.</summary>
	public List<string> VendorDirectories = [];

	/// <summary>ANTLR grammars to compile and build the generated parser from.</summary>
	public List<string> AntlrGrammars = [];

	/// <summary>Assembly files to assemble into the build.</summary>
	public List<string> MasmFiles = [];

	/// <summary>Windows ETW manifests, which generate a header and a resource script.</summary>
	public List<string> EtwManifests = [];

	/// <summary>Whether the module said what to compile. Nothing is implied: it has to say.</summary>
	public bool HasSources { get; private set; }

	/// <summary>
	/// Applies the modifiers the module stated. Runs after the constructor, so NoPchFiles and ExcludeFiles also
	/// cover files that a glob brought in.
	/// </summary>
	public void Finish()
	{
		// Before the globs below: these add files with settings of their own, and a glob must not get
		// there first and add them as ordinary sources.
		Vendor( [.. VendorDirectories] );
		Antlr( [.. AntlrGrammars] );
		Masm( [.. MasmFiles] );
		EtwManifest( [.. EtwManifests] );

		foreach ( var config in Configs )
		{
			config.Define( [.. CompileDefinitions] );
			config.Include( [.. IncludeDirectories] );
		}

		DebugConfig.Define( [.. Debug.CompileDefinitions] );
		ReleaseConfig.Define( [.. Release.CompileDefinitions] );

		AddGlobs( [.. SourceFiles], FileKind.Compile );
		AddGlobs( [.. HeaderFiles], FileKind.Include );
		foreach ( var path in ExtraFiles ) AddNamed( path, FileKind.None );
		foreach ( var path in ResourceFiles ) AddNamed( path, FileKind.Resource );

		HasSources = ResolvedFiles.Any( f => f.Kind == FileKind.Compile );

		if ( Qt ) AddGlobs( ["*.ui", "*.qrc"], FileKind.None );

		foreach ( var matcher in Matchers( NoPchFiles ) )
			foreach ( var file in ResolvedFiles.Where( f => matcher( f.Path ) ) ) file.NoPch = true;

		foreach ( var matcher in Matchers( CompileAsCFiles ) )
			foreach ( var file in ResolvedFiles.Where( f => matcher( f.Path ) ) ) file.CompileAs = "CompileAsC";

		foreach ( var matcher in Matchers( Msvc.AggressiveInliningFiles ) )
			foreach ( var file in ResolvedFiles.Where( f => matcher( f.Path ) ) ) file.Msvc.Add( "/Ob3" );

		foreach ( var matcher in Matchers( ExcludeFiles ) ) ResolvedFiles.RemoveAll( f => matcher( f.Path ) );
	}

	/// <summary>
	/// A file listed for browsing is shown as listed, whether or not it is there. Only a glob has to match
	/// something.
	/// </summary>
	private void AddNamed( string path, FileKind kind )
	{
		if ( path.Contains( '*' ) ) AddGlobs( [path], kind );
		else File( path, kind );
	}

	/// <summary>Turns globs into predicates over src relative paths.</summary>
	internal IEnumerable<Func<string, bool>> Matchers( IEnumerable<string> globs ) =>
		globs.Select( glob => Paths.Matcher( Paths.Resolve( Dir, glob ) ) );

	/// <summary>
	/// SourceFiles a vendored source tree into this module: everything under the directory, no precompiled
	/// header, left out of unity batches.
	/// </summary>
	private void Vendor( params string[] dirs )
	{
		foreach ( var dir in dirs )
		{
			var before = ResolvedFiles.Count;

			AddGlobs( [$"{dir}/**/*.cpp", $"{dir}/**/*.c", $"{dir}/**/*.cc"], FileKind.Compile );

			foreach ( var file in ResolvedFiles.Skip( before ) ) file.NoPch = true;
			NoUnityFiles.Add( $"{dir}/**" );
		}
	}

	internal SourceFile File( string path, FileKind kind = FileKind.Compile, bool noPch = false, bool createPch = false,
		string compileAs = null, string[] msvc = null, string[] clang = null, string[] defines = null, string[] includes = null, string[] excludeFrom = null )
	{
		var file = new SourceFile
		{
			Path = Paths.Resolve( Dir, path ),
			Kind = kind,
			NoPch = noPch,
			CreatePch = createPch,
			CompileAs = compileAs,
			Msvc = [.. msvc ?? []],
			Clang = [.. clang ?? []],
			Defines = [.. defines ?? []],
			Includes = [.. includes ?? []],
			ExcludeFrom = [.. excludeFrom ?? []]
		};

		// A file stated by name replaces one a glob happened to match, so its own settings are not lost.
		ResolvedFiles.RemoveAll( f => f.Path == file.Path );
		ResolvedFiles.Add( file );
		return file;
	}

	/// <summary>Windows SDK tools are not on PATH by default.</summary>
	private const string SdkPath = "PATH=%PATH%;$(WindowsSDK_ExecutablePath_x86)\r\n";

	private static string Quoted( string value ) => "\"" + value + "\"";

	/// <summary>SourceFiles ANTLR grammars and build the generated parser.</summary>
	private void Antlr( params string[] grammars )
	{
		const string output = "generated_antlr_code";
		IncludeDirectories.Add( $"{Dir}/{output}" );

		foreach ( var grammar in grammars )
		{
			var name = Path.GetFileNameWithoutExtension( grammar );
			var antlr = Paths.Relative( Dir, "devtools/bin/antlr-3.2" );

			CustomStep( grammar,
				command: $@"{antlr} %(FullPath) -o {output}",
				outputs: [$@"{output}\{name}.tokens", $@"{output}\{name}Lexer.c", $@"{output}\{name}Lexer.h", $@"{output}\{name}Parser.c", $@"{output}\{name}Parser.h"],
				message: $"ANTLR: {grammar}" );

			// Generated as C but compiled as C++, like the rest of the module.
			foreach ( var generated in new[] { $"{output}/{name}Lexer.c", $"{output}/{name}Parser.c" } )
				File( generated, noPch: true );
		}
	}

	/// <summary>Assemble a .masm file into the build.</summary>
	private void Masm( params string[] files )
	{
		foreach ( var file in files )
			CustomStep( file,
				command: SdkPath + "ml64.exe /nologo /c /Cp /Zi /Fo" + Quoted( "$(IntDir)%(Filename).obj" ) + " " + Quoted( "%(FullPath)" ),
				outputs: ["$(IntDir)%(Filename).obj"],
				message: $"Assembling {file}" );
	}

	/// <summary>SourceFiles a Windows ETW manifest, which generates a header and a resource script.</summary>
	private void EtwManifest( params string[] files )
	{
		// The generated header and .rc land in the intermediate directory, and a resource script that
		// includes them has to be able to find them.
		if ( files.Length > 0 )
			foreach ( var config in Configs )
				config.Rc["AdditionalIncludeDirectories"] = "$(IntermediateOutputPath);%(AdditionalIncludeDirectories)";

		foreach ( var file in files )
			CustomStep( file,
				command: SdkPath + "mc.exe -um %(Filename)%(Extension) -z $(IntermediateOutputPath)%(Filename)Events",
				outputs: ["$(IntermediateOutputPath)%(Filename)Events.h", "$(IntermediateOutputPath)%(Filename)Events.rc"],
				message: $"Compiling {file}" );
	}

	/// <summary>A file with its own build step. %config% expands to the lowercase config name.</summary>
	public SourceFile CustomStep( string path, string command, string[] outputs, string[] inputs = null, string message = null )
	{
		var file = File( path, FileKind.None );
		file.Build = new CustomBuild
		{
			Message = message,
			Command = command,
			Inputs = [.. inputs ?? []],
			Outputs = [.. outputs]
		};

		return file;
	}

	private void AddGlobs( string[] globs, FileKind kind )
	{
		foreach ( var glob in globs )
		{
			foreach ( var path in Paths.Glob( Dir, glob ) )
			{
				if ( ResolvedFiles.Any( f => f.Path == path ) ) continue;
				ResolvedFiles.Add( new SourceFile { Path = path, Kind = kind } );
			}
		}
	}
}

public sealed class SchemaSettings
{
	public List<string> Files = [];
	public List<string> PreIncludes = [];
	public List<string> AnchorIncludes = [];
	public List<string> GlobalTypes = [];
	public List<string> OmitTypes = [];

	public bool Any => Files.Count > 0;
}
