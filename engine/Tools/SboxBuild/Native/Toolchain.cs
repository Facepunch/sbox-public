namespace Facepunch.Native;

/// <summary>
/// Settings only one toolchain understands, stated in that toolchain's own vocabulary. A module writes
/// <c>Msvc.NoWarn = [4718]</c> or <c>Clang.NoWarn = ["parentheses"]</c>, and the platform that owns the
/// toolchain is the only thing that reads them, so nothing has to guess what a value means.
/// </summary>
public abstract class Toolchain
{
	/// <summary>Switches passed to the compiler as written.</summary>
	public List<string> CompileOptions = [];

	/// <summary>Libraries to link, named the way this toolchain names them.</summary>
	public List<string> LinkLibraries = [];

	public List<string> LinkDirectories = [];

	/// <summary>Raw switches for the linker, written as given.</summary>
	public List<string> LinkOptions = [];

	/// <summary>Headers forced into every source, as if it included them first.</summary>
	public List<string> ForceIncludes = [];

	/// <summary>Adds what the module stated to one configuration.</summary>
	public virtual void Apply( Config config )
	{
		config.Options.AddRange( CompileOptions );
		config.LinkLibs.AddRange( LinkLibraries.Select( Library ) );
		config.LibDirs.AddRange( LinkDirectories );
		config.LinkOptions.AddRange( LinkOptions );
		config.ForceIncludes.AddRange( ForceIncludes );
	}

	/// <summary>How this toolchain spells a library on the link line.</summary>
	protected virtual string Library( string name ) => name;
}

/// <summary>What MSVC settings look like, shared by the module wide and per configuration scopes.</summary>
public abstract class MsvcSettings : Toolchain
{
	/// <summary>Escape hatch: MSBuild properties, written into the project as-is.</summary>
	public Dictionary<string, string> Cl = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, string> Link = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, string> Lib = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>A system library by name, or a prebuilt one by path. The .lib is implied.</summary>
	protected override string Library( string name ) =>
		name.EndsWith( ".lib", StringComparison.OrdinalIgnoreCase ) ? name : $"{name}.lib";

	public override void Apply( Config config )
	{
		base.Apply( config );

		foreach ( var (key, value) in Cl ) config.Cl[key] = value;
		foreach ( var (key, value) in Link ) config.Link[key] = value;
		foreach ( var (key, value) in Lib ) config.Lib[key] = value;
	}
}

/// <summary>MSVC, and the settings that only exist for it.</summary>
public sealed class Msvc : MsvcSettings
{
	/// <summary>For the few things that genuinely differ between configurations.</summary>
	public Configured Debug = new(), Release = new();

	public sealed class Configured : MsvcSettings;

	/// <summary>Warning numbers to silence for this module.</summary>
	public List<int> NoWarn = [];

	/// <summary>Libraries merged into a static library, so consumers get them for free.</summary>
	public List<string> MergeLibraries = [];

	/// <summary>Sources to compile with /Ob3, by glob: hot code where inlining is worth the size.</summary>
	public List<string> AggressiveInliningFiles = [];

	/// <summary>Reserved stack, for tools that recurse deeply.</summary>
	public int? StackSize;

	/// <summary>Minimum Windows version the binary loads on, e.g. "5.02".</summary>
	public string MinWindowsVersion;

	/// <summary>Exports definition file for a dll.</summary>
	public string ModuleDefinition;

	/// <summary>Edit and continue debug info. Off means a plain program database.</summary>
	public bool EditAndContinue = true;

	/// <summary>Compile the module's files in parallel. Off for code that cannot take it.</summary>
	public bool MultiProcessor = true;

	/// <summary>Runtime type information.</summary>
	public bool Rtti = true;

	/// <summary>Precompiled header memory in MB (/Zm), for the few headers that need more.</summary>
	public int? PchMemory;

	/// <summary>Symbol the precompiled header hangs its object code on (/Yl).</summary>
	public string PchSymbol;

	public override void Apply( Config config )
	{
		if ( PchSymbol is not null ) config.Option( $"/Yl{PchSymbol}" );
		if ( PchMemory is not null ) config.Option( $"/Zm{PchMemory}" );

		base.Apply( config );

		config.NoWarn( [.. NoWarn.Select( warning => warning.ToString() )] );

		(config.Name == "Debug" ? Debug : Release).Apply( config );
	}
}

/// <summary>clang, as the posix builds use it.</summary>
public sealed class Clang : Toolchain
{
	/// <summary>Warnings to silence, named the way clang names them: without the -Wno- prefix.</summary>
	public List<string> NoWarn = [];

	public override void Apply( Config config )
	{
		base.Apply( config );
		config.Option( [.. NoWarn.Select( warning => $"-Wno-{warning}" )] );
	}
}
