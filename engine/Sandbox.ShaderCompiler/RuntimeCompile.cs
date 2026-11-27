namespace Sandbox.ShaderCompiler;

/// <summary>Why a shader compiled at run time instead of being read from its offline compile (<see cref="SlangOutput"/>).</summary>
public enum RuntimeCompileCause
{
	/// <summary>No <c>_c</c> beside its source: one the build doesn't compile, or made at run time.</summary>
	NoCompiled,

	/// <summary>Its <c>_c</c> was made from another text, compiler or includes, or can't be read.</summary>
	Stale,

	/// <summary>Made from the source embedded in a package's <c>.shader_c</c> that an older engine compiled.</summary>
	EmbeddedSource,

	/// <summary>Its <c>_c</c> is current but doesn't hold these combo values.</summary>
	MissingVariant,
}

/// <summary>
/// One compile at run time: what compiled (a module's file name, and a pass stage's stage and combo values), the source's
/// path, why, its wall time on the compile threads and whether it failed.
/// </summary>
public readonly record struct RuntimeCompile( string What, string Path, RuntimeCompileCause Cause, double Milliseconds, bool Failed );
