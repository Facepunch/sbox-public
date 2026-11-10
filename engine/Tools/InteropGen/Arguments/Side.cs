namespace Facepunch.InteropGen;

/// <summary>
/// Which side of the interop boundary a marshalling step is generating code for.
/// </summary>
public enum Side
{
	/// <summary> The managed (C#) side. </summary>
	Managed,

	/// <summary> The native (C++) side. </summary>
	Native,
}

/// <summary>
/// Disambiguates the two roles a delegate-type can be emitted for. What matters for correctness is
/// that a function definition and every function-pointer cast of it use the same (Side, Dir) pair
/// per element - the conventions differ per channel and both are deliberate:
///
/// Native exports (managed calls native) and variables use data-flow semantics: the side receiving
/// a value types it Incoming, the side sending it types it Outgoing - so the native thunk takes
/// (Incoming params, Outgoing return) and the managed caller declares (Outgoing params, Incoming
/// return).
///
/// Managed exports (native calls managed) use callee/caller semantics instead: the managed thunk
/// and its function-pointer table type every element - return included - as Incoming, and the
/// native Imports declaration and bind cast type everything as Outgoing.
///
/// Don't "fix" one site to the other convention: pairs like a non-small struct return flip between
/// by-value and by-pointer with Dir, and a one-sided change is an ABI mismatch.
/// </summary>
public enum Dir
{
	/// <summary> Incoming relative to the code being emitted - a value it receives, or (for managed exports) the defining side. </summary>
	Incoming,

	/// <summary> Outgoing relative to the code being emitted - a value it sends, or (for managed exports) the calling side. </summary>
	Outgoing,
}
