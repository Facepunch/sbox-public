using System.Runtime.InteropServices;

namespace Facepunch.Native;

/// <summary>
/// A platform the native code is built for: the toolchain settings every module gets, how output is
/// named and where it lands, the build files that come out, and how those are then built. All of that
/// lives behind this, so a module definition only branches when its own sources or libraries differ.
/// </summary>
public abstract class NativePlatform
{
	/// <summary>Every platform there is a generator for.</summary>
	public static NativePlatform[] All => [new Win64(), new Linux64()];

	/// <summary>The platform this run targets. Defaults to the one this machine is.</summary>
	public static NativePlatform Current
	{
		get => current ??= Host();
		internal set => current = value;
	}

	private static NativePlatform current;

	/// <summary>The platform by name, or null if nothing generates for it. Its directory answers too.</summary>
	public static NativePlatform Find( string name ) =>
		All.FirstOrDefault( p => p.Name.Equals( name, StringComparison.OrdinalIgnoreCase )
			|| p.DirectoryName.Equals( name, StringComparison.OrdinalIgnoreCase ) );

	/// <summary>
	/// The platform this machine is, which is what a build targets unless it was told otherwise. The
	/// architecture comes from the running process, so an arm64 host asks for an arm64 platform.
	/// </summary>
	public static NativePlatform Host()
	{
		var os = OperatingSystem.IsWindows() ? "win"
			: OperatingSystem.IsLinux() ? "linux"
			: OperatingSystem.IsMacOS() ? "osx"
			: throw new PlatformNotSupportedException( RuntimeInformation.OSDescription );

		var architecture = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "64",
			Architecture.Arm64 => "arm64",
			Architecture.X86 => "32",
			Architecture.Arm => "arm",
			var other => throw new PlatformNotSupportedException( other.ToString() )
		};

		return Find( os + architecture )
			?? throw new PlatformNotSupportedException( $"Nothing generates a native build for {os}{architecture}." );
	}

	/// <summary>What the platform is called: on the command line and in generated file names.</summary>
	public abstract string Name { get; }

	/// <summary>What the platform is called in paths, when that is not what it is called.</summary>
	public virtual string DirectoryName => Name;

	public abstract bool IsWindows { get; }

	/// <summary>Where prebuilt and generated libraries live, relative to src/.</summary>
	public abstract string LibPublic { get; }
	public abstract string LibCommon { get; }

	/// <summary>Where a module's output lands, relative to src/, and what it is called there.</summary>
	public abstract string OutputDir( Module module );
	public abstract string OutputFile( Module module );

	/// <summary>Toolchain settings for every config of a module. A module's own settings win.</summary>
	public abstract void Apply( Module module, Options options );

	/// <summary>Write the projects, solutions or makefiles this platform builds from.</summary>
	public abstract void Generate( List<Module> modules, Options options );

	/// <summary>Write one module's project, for <c>generate-solutions --module</c>.</summary>
	public abstract void Generate( Module module, Options options );

	/// <summary>
	/// Build what <see cref="Generate(List{Module}, Options)"/> wrote. Paired with it on purpose: only the
	/// platform that wrote a solution or a makefile knows the tool that reads it.
	/// </summary>
	public abstract bool Build( string name, bool forceRebuild = false );

	/// <summary>
	/// What a build of this platform runs through, in order. AlwaysRebuild is for the parts that are never
	/// built incrementally.
	/// </summary>
	public abstract IEnumerable<(string Name, bool AlwaysRebuild)> Solutions( Options options );

	/// <summary>The everything solution, named the same way by whoever writes it and whoever builds it.</summary>
	protected string EverythingSolution( Options options ) => $"{(options.Retail ? "buildbot" : "developer")}_all_{Name}";

	/// <summary>A module this platform does not build at all.</summary>
	public virtual bool Skips( Module module ) => false;
}
