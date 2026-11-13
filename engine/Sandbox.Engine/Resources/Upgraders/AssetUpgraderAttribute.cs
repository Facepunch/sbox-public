using System;

namespace Editor;

/// <summary>
/// Registers a static method to upgrade assets with the given extension.
/// Source methods have signature <c>static string (string source)</c> and return null when unchanged.
/// Compiled methods have signature <c>static void (KeyValues3 data)</c> and modify data in place.
/// Compiled data is native-owned and valid only during the call.
/// Upgrades must be safe to run again and must not write files themselves.
/// </summary>
[AttributeUsage( AttributeTargets.Method, AllowMultiple = true )]
internal sealed class AssetUpgraderAttribute : Attribute
{
	/// <summary>
	/// The source file extension, normalized without a leading dot.
	/// </summary>
	public string Extension { get; }

	/// <summary>
	/// Whether this method upgrades source text (the default) or decoded compiled data.
	/// </summary>
	public AssetUpgradeTarget Target { get; set; }

	/// <summary>
	/// Optional resource filename referenced directly and unconditionally by source that needs this upgrade.
	/// The compiler must record this reference independently of other inputs, such as a material's "bark.shader".
	/// Skips source reads only when its cached checksum matches the compiled source. Missing cache data runs the upgrade.
	/// Matching ignores directories, case and the compiled "_c" suffix.
	/// </summary>
	public string SourceReference { get; set; }

	/// <summary>
	/// Execution order when several methods upgrade the same extension. Lower values run first;
	/// ties are ordered by declaring type and method name. This is not a stored asset version.
	/// </summary>
	public int Order { get; set; }

	/// <summary>
	/// Registers an upgrader for a source extension, with or without a leading dot.
	/// </summary>
	/// <param name="extension">The source extension, such as ".vmat" or "sound".</param>
	public AssetUpgraderAttribute( string extension )
	{
		extension = extension?.TrimStart( '.' );
		ArgumentException.ThrowIfNullOrWhiteSpace( extension );
		Extension = extension.ToLowerInvariant();
	}
}

internal enum AssetUpgradeTarget
{
	Source,
	Compiled
}
