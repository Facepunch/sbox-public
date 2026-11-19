namespace Nav;

/// <summary>
/// Caller-supplied compression for the rebuild cache. Both functions return the bytes written, 0 when the
/// output did not fit. They are called on worker threads and must be reentrant. The default is no compression.
/// The cache is not part of a bake, so the functions may differ from one run to the next. One that throws
/// does not get past the build: when compressing, the job ends Failed with the exception's message, and
/// when decompressing the job asks for geometry, as it does for bytes that are not the field.
/// </summary>
public readonly struct NavCodec
{
	internal readonly Func<ReadOnlySpan<byte>, Span<byte>, int>? Compress;
	internal readonly Func<ReadOnlySpan<byte>, Span<byte>, int>? Decompress;

	public NavCodec( Func<ReadOnlySpan<byte>, Span<byte>, int> compress, Func<ReadOnlySpan<byte>, Span<byte>, int> decompress )
	{
		Compress = compress;
		Decompress = decompress;
	}

	internal bool IsNone => Compress == null || Decompress == null;
}
