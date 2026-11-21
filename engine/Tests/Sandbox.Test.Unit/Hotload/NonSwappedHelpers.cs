using System;

namespace HotloadTests;

/// <summary>
/// Called by fixtures that need code from an assembly that isn't swapped by the hotload.
/// </summary>
public static class NonSwappedHelpers
{
	/// <summary>
	/// A generic method in a non-swapped assembly, returning a lambda that captures a value of a swapped type.
	/// </summary>
	public static Func<T, bool> GenericMethod<T>( T arg )
	{
		return t => Equals( t, arg );
	}

	/// <summary>
	/// A lambda declared in a non-swapped assembly.
	/// </summary>
	public static Func<int, int> Identity { get; } = x => x;
}
