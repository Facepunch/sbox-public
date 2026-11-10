
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// A stack-only type with the ability to rent a buffer of a specified length and getting a <see cref="Span{T}"/> from it.
/// It should be used like so:
/// <code>
/// using (PooledSpan&lt;byte> buffer = PooledSpan&lt;byte>(1024))
/// {
///     // Use the buffer here...
/// }
/// </code>
/// As soon as the code leaves the scope of that <see langword="using"/> block, the underlying buffer will automatically
/// be disposed.
/// </summary>
internal readonly ref struct PooledSpan<T>
{
	/// <summary>
	/// The usable length within <see cref="array"/>.
	/// </summary>
	private readonly int length;

	/// <summary>
	/// The underlying <typeparamref name="T"/> array.
	/// </summary>
	private readonly T[] array;

	private readonly bool clearOnReturn;

	/// <summary>
	/// Initializes a new instance of the <see cref="PooledSpan{T}"/> struct with the specified parameters.
	/// </summary>
	/// <param name="length">The length of the new memory buffer to use.</param>
	/// <param name="clearOnReturn">
	/// Wipe the buffer on dispose. Pass true when <typeparamref name="T"/> contains references, or the
	/// pool holds them alive until the buffer is rented again.
	/// </param>
	public PooledSpan( int length, bool clearOnReturn = false )
	{
		this.length = length;
		this.clearOnReturn = clearOnReturn;
		this.array = ArrayPool<T>.Shared.Rent( length );
	}

	/// <summary>
	/// Gets a <see cref="Span{T}"/> wrapping the memory belonging to the current instance.
	/// </summary>
	public Span<T> Span => array.AsSpan( 0, length );

	/// <summary>
	/// The rented array, which may be longer than <see cref="Span"/>. Only for APIs that can't accept a
	/// span, such as anything called through a delegate - prefer <see cref="Span"/> everywhere else.
	/// </summary>
	public T[] Array => array;

	/// <summary>
	/// Implements the duck-typed <see cref="IDisposable.Dispose"/> method.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Dispose()
	{
		// A default instance never rented anything.
		if ( array is null )
			return;

		ArrayPool<T>.Shared.Return( array, clearOnReturn );
	}
}
