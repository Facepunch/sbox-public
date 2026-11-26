namespace Nav;

/// <summary>
/// Caller-supplied compression for the rebuild cache. Both functions return the bytes written, 0 when the
/// output did not fit, and must be reentrant: they run on worker threads. A default NavCodec keeps the cache
/// uncompressed. The cache is not baked, so the functions may change between runs. A throwing Compress fails
/// the job with the exception's message; a throwing Decompress makes the job ask for geometry.
/// </summary>
public readonly struct NavCodec
{
	internal readonly Func<ReadOnlySpan<byte>, Span<byte>, int>? Compress;
	internal readonly Func<ReadOnlySpan<byte>, Span<byte>, int>? Decompress;

	public NavCodec( Func<ReadOnlySpan<byte>, Span<byte>, int> compress, Func<ReadOnlySpan<byte>, Span<byte>, int> decompress )
	{
		ArgumentNullException.ThrowIfNull( compress );
		ArgumentNullException.ThrowIfNull( decompress );
		Compress = compress;
		Decompress = decompress;
	}

	internal bool IsNone => Compress == null;
}
