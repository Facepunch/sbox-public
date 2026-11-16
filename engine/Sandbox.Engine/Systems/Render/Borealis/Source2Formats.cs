using NativeEngine;

namespace Borealis.Gpu.Source2;

/// <summary>
/// Formats and sample counts between Borealis and the engine. Names match, so the mapping is by name.
/// </summary>
internal static class Source2Formats
{
	static readonly Dictionary<ImageFormat, TextureFormat> fromEngine = new();
	static readonly Dictionary<TextureFormat, ImageFormat> toEngine = new();

	static Source2Formats()
	{
		foreach ( var format in Enum.GetValues<TextureFormat>() )
		{
			if ( format == TextureFormat.Unknown ) continue;
			var engine = Enum.Parse<ImageFormat>( format.ToString() );
			fromEngine[engine] = format;
			toEngine[format] = engine;
		}
	}

	public static TextureFormat From( ImageFormat format ) => fromEngine.GetValueOrDefault( format, TextureFormat.Unknown );

	public static ImageFormat ToEngine( this TextureFormat format ) => toEngine.TryGetValue( format, out var engine ) ? engine
		: throw new ArgumentException( $"{format} has no engine format", nameof( format ) );

	/// <summary>
	/// The engine's format, or RGBA8888 for one Borealis doesn't name, where the engine needs a format to set pipeline state for.
	/// </summary>
	public static ImageFormat ToEngineOrDefault( TextureFormat format ) => toEngine.TryGetValue( format, out var engine ) ? engine : ImageFormat.RGBA8888;

	public static int Samples( RenderMultisampleType type ) => type switch
	{
		RenderMultisampleType.RENDER_MULTISAMPLE_2X => 2,
		RenderMultisampleType.RENDER_MULTISAMPLE_4X => 4,
		RenderMultisampleType.RENDER_MULTISAMPLE_6X => 6,
		RenderMultisampleType.RENDER_MULTISAMPLE_8X => 8,
		RenderMultisampleType.RENDER_MULTISAMPLE_16X => 16,
		_ => 1,
	};

	public static MultisampleAmount ToAmount( int samples ) => samples switch
	{
		<= 1 => MultisampleAmount.MultisampleNone,
		2 => MultisampleAmount.Multisample2x,
		4 => MultisampleAmount.Multisample4x,
		6 => MultisampleAmount.Multisample6x,
		8 => MultisampleAmount.Multisample8x,
		_ => MultisampleAmount.Multisample16x,
	};
}
