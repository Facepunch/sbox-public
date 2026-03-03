using System.Buffers;

namespace Sandbox.Internal;

/// <summary>
/// Calls to ArrayPool.Shared{T} will map to this class.
/// You can use it directly but you probably shouldn't
/// </summary>
public sealed class PublicArrayPool<T>
{
	// Store the shared ArrayPool in a field of its derived sealed type so the Jit can "see" the exact type
	// when the Shared property is inlined which will allow it to devirtualize calls made on it.
	private static readonly SharedArrayPool<T> s_shared = new SharedArrayPool<T>();

	/// <summary>
	/// Retrieves a shared <see cref="ArrayPool{T}"/> instance.
	/// </summary>
	/// <remarks>
	/// The shared pool provides a default implementation of <see cref="ArrayPool{T}"/>
	/// that's intended for general applicability.  It maintains arrays of multiple sizes, and
	/// may hand back a larger array than was actually requested, but will never hand back a smaller
	/// array than was requested. Renting a buffer from it with <see cref="ArrayPool{T}.Rent"/> will result in an
	/// existing buffer being taken from the pool if an appropriate buffer is available or in a new
	/// buffer being allocated if one is not available.
	/// The shared pool instance is created lazily on first access.
	/// </remarks>
	public static ArrayPool<T> Shared => s_shared;
}
