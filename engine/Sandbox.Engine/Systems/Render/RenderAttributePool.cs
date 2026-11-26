using System.Collections.Concurrent;

namespace Sandbox;

/// <summary>
/// A thread-safe pool for reusing <see cref="RenderAttributes"/> instances.
/// Returning to the pool clears the instance but does not free its memory, allowing for efficient reuse.
/// Uses ConcurrentQueue instead of ConcurrentBag to avoid ThreadLocal strong handle roots
/// that survive even after Clear/null.
/// </summary>
internal class RenderAttributePool
{
	readonly ConcurrentQueue<RenderAttributes> pool = new();

	/// <summary>
	/// Get a pooled managed attribute set.
	/// Make sure you return it to the pool after use with <see cref="Return(RenderAttributes)"/>.
	/// </summary>
	public RenderAttributes Get()
	{
		return pool.TryDequeue( out var managed ) ? managed : RenderAttributes.CreateManaged();
	}

	/// <summary>
	/// Returns a <see cref="RenderAttributes"/> instance to the pool for reuse.
	/// Make sure you're done with it because it will be cleared.
	/// </summary>
	public void Return( RenderAttributes renderAttributes )
	{
		ArgumentNullException.ThrowIfNull( renderAttributes );

		renderAttributes.Clear( false ); // don't free our memory, we want to reuse it
		pool.Enqueue( renderAttributes );
	}

	public void Clear() => pool.Clear();
}
