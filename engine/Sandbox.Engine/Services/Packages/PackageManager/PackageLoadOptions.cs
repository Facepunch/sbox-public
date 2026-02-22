using Sandbox.Internal;
using System.Threading;

namespace Sandbox;

/// <summary>
/// General options that are usable whenever we load a package
/// </summary>
internal struct PackageLoadOptions
{
	/// <summary>
	/// Mounts assets from the package, but does not compile or load any code.
	/// </summary>
	public static PackageLoadOptions Default => new PackageLoadOptions();

	public PackageLoadOptions()
	{

	}

	public PackageLoadOptions( string fullIdent, string tag, CancellationToken token = default ) : this()
	{
		PackageIdent = fullIdent;
		ContextTag = tag;
		CancellationToken = token;
	}

	/// <summary>
	/// The ident of the package to load
	/// </summary>
	public string PackageIdent { get; set; }

	/// <summary>
	/// A group tag for this package, so we can unload all packages using this tag
	/// </summary>
	public string ContextTag { get; set; }

	/// <summary>
	/// For cancelling the task
	/// </summary>
	public CancellationToken CancellationToken { get; set; }

	/// <summary>
	/// </summary>
	public bool AllowLocalPackages { get; set; } = true;

	/// <summary>
	/// If true we will only download the code files (.bin) and not the assets.
	/// </summary>
	public bool SkipAssetDownload { get; set; }

	/// <summary>
	/// If false, the resource system will not reload symlinked resident resources after mounting.
	/// Set to false when batch-installing packages and call
	/// <c>NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources()</c> once manually afterwards.
	/// </summary>
	public bool ReloadResources { get; set; } = true;

	/// <summary>
	/// Loading progress callbacks
	/// </summary>
	internal ILoadingInterface Loading { get; set; }
}

