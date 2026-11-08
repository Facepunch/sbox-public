using NativeEngine;
using System.Runtime.CompilerServices;

namespace Basalt.Gpu.Source2;

/// <summary>
/// A Source 2 texture: the engine's <see cref="Texture"/>, wrapped once so identity holds.
/// </summary>
internal sealed class Source2Texture : GpuTexture
{
	public Texture Texture { get; }

	Source2Texture( Texture texture )
	{
		Texture = texture;
	}

	static readonly ConditionalWeakTable<Texture, Source2Texture> wrapped = new();

	/// <summary>
	/// The engine texture as Basalt's, or null.
	/// </summary>
	public static GpuTexture From( Texture texture ) => texture is null ? null : wrapped.GetValue( texture, static t => new Source2Texture( t ) );

	public override int Width => Texture.Width;
	public override int Height => Texture.Height;
	public override int Mips => Texture.Mips;
	public override TextureFormat Format => Source2Formats.From( Texture.ImageFormat );
	public override int Samples => Source2Formats.Samples( Texture.MultisampleType );
	public override bool IsCube => Texture.Desc.IsCube;
	public override bool IsArray => Texture.Desc.IsArray;
	public override bool IsValid => Texture.IsValid;
	public override int BindlessIndex => Texture.Index;
	public override Vector4 SheetData => Texture.SequenceData;

	public override int ViewIndex( bool srgb, TextureViewDimension dimension ) => g_pRenderDevice.GetTextureViewIndex( Texture.native,
		srgb ? (byte)RenderColorSpace.RENDER_SRGB : (byte)0,
		dimension == TextureViewDimension.Texture2D ? RenderTextureDimension.RENDER_TEXTURE_DIMENSION_2D : RenderTextureDimension.RENDER_TEXTURE_DIMENSION_INVALID );

	/// <summary>
	/// What the device answers for a missing texture's 2D view, as decals pack it.
	/// </summary>
	public static int NoViewIndex( bool srgb ) => g_pRenderDevice.GetTextureViewIndex( default, srgb ? (byte)RenderColorSpace.RENDER_SRGB : (byte)0,
		RenderTextureDimension.RENDER_TEXTURE_DIMENSION_2D );

	public override void KeepResident() => Texture.MarkUsed( ushort.MaxValue );

	public override void Dispose() => Texture.Dispose();
}

/// <summary>
/// Basalt's textures as the engine's, while the rest of Basalt still records through engine APIs.
/// </summary>
internal static class Source2TextureExtensions
{
	/// <summary>
	/// The engine texture behind a Basalt one, or null.
	/// </summary>
	public static Texture Engine( this GpuTexture texture ) => (texture as Source2Texture)?.Texture;

	/// <summary>
	/// The native texture behind a Basalt one, or an invalid handle.
	/// </summary>
	public static ITexture Native( this GpuTexture texture ) => texture is Source2Texture s ? s.Texture.native : default;

	/// <summary>
	/// Bind a Basalt texture as an attribute.
	/// </summary>
	public static void Set( this RenderAttributes attributes, StringToken name, GpuTexture texture ) => attributes.Set( name, texture.Engine() );

	/// <summary>
	/// The engine buffer behind a Basalt one, or null.
	/// </summary>
	public static Sandbox.GpuBuffer Engine( this GpuBuffer buffer ) => buffer?.Resource as Sandbox.GpuBuffer;

	/// <summary>
	/// The native buffer behind a Basalt one, or an invalid handle.
	/// </summary>
	public static RenderBufferHandle_t Native( this GpuBuffer buffer ) => buffer?.Resource is Sandbox.GpuBuffer b ? b.native : default;

	/// <summary>
	/// Bind a Basalt buffer as an attribute.
	/// </summary>
	public static void Set( this RenderAttributes attributes, StringToken name, GpuBuffer buffer ) => attributes.Set( name, buffer.Engine() );
}

/// <summary>
/// Formats and sample counts between Basalt and the engine. Names match, so the mapping is by name.
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
