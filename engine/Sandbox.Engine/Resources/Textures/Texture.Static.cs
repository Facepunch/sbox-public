
namespace Sandbox;

public partial class Texture
{
	/// <summary>
	/// 1x1 solid magenta colored texture.
	/// </summary>
	public static Texture Invalid { get; internal set; } = Create( 1, 1 ).WithData( new byte[4] { 255, 0, 255, 255 } ).Finish();

	/// <summary>
	/// 1x1 solid white opaque texture.
	/// </summary>
	public static Texture White { get; internal set; } = Create( 1, 1 ).WithData( new byte[4] { 255, 255, 255, 255 } ).Finish();

	/// <summary>
	/// 1x1 solid black opaque texture.
	/// </summary>
	public static Texture Black { get; internal set; } = Create( 1, 1 ).WithData( new byte[4] { 0, 0, 0, 255 } ).Finish();

	/// <summary>
	/// 1x1 fully transparent texture.
	/// </summary>
	public static Texture Transparent { get; internal set; } = Create( 1, 1 ).WithData( new byte[4] { 128, 128, 128, 0 } ).Finish();

	internal static unsafe Texture Create( string name, bool anonymous, TextureBuilder builder, IntPtr data, int dataSize )
	{
		var config = builder._config.GetWithFixes();

		var texture = g_pRenderDevice.FindOrCreateTexture2( string.IsNullOrEmpty( name ) ? "unnamed" : name, anonymous, config, data, dataSize );
		//bool isRenderTarget = builder.common.m_nFlags.HasFlag( RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET );

		// Source 2's empty device has no image to clear; what's drawn makes its copy cleared (InitialColor)
		if ( (data == IntPtr.Zero || dataSize <= 0) && Graphics.HasSource2Device )
		{
			g_pRenderDevice.ClearTexture( texture, builder._initialColor ?? Color.Transparent );
		}

		var result = FromNative( texture );
		if ( !string.IsNullOrWhiteSpace( name ) )
			result?.RegisterWeakResourceId( name );

		if ( result is not null )
			result.CreatedMultisample = config.m_nMultisampleType;

		// An anonymous texture is always a new one, described by what it was made with; a named one may be one Source 2
		// found by its name (NamedDescription)
		if ( result is not null && !result.gotdesc && DescriptionOf( config, dataSize ) is var made && (anonymous || NamedDescription( name, result, made )) )
		{
			result._desc = made;
			result.gotdesc = true;
			result.describedAtCreation = true;
		}

		// Another device makes its copy from these rather than reading Source 2's back (with -borealisgpu, from before
		// the device is made too: without Source 2's device there's nothing to read back)
		if ( result is not null && (RenderBackend.Default is not Source2RenderBackend || RenderBackend.OthersExist || Graphics.BorealisGpuRequested) )
		{
			result.InitialPixels = data != IntPtr.Zero && dataSize > 0 ? new ReadOnlySpan<byte>( (void*)data, dataSize ).ToArray() : null;
			result.InitialColor = builder._initialColor ?? Color.Transparent;
		}

		return result;
	}

	/// <summary>Named textures made at run time, by native texture: the name and description each was made with.</summary>
	static readonly Dictionary<long, (string Name, NativeEngine.CTextureDesc Desc)> namedTextures = new();

	/// <summary>
	/// Whether a texture made with a name, new to managed code, is described by what it was made with
	/// (<paramref name="made"/>). Source 2 finds a named texture that's still there rather than making it, so one made here
	/// before at the same texture and name with another description (found, or made again where the old one was) is
	/// described by Source 2, as is a name that's a compiled file's.
	/// </summary>
	static bool NamedDescription( string name, Texture texture, in NativeEngine.CTextureDesc made )
	{
		if ( string.IsNullOrEmpty( name ) || name.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) || name.EndsWith( ".vtex_c", StringComparison.OrdinalIgnoreCase ) ) return false;

		// As Source 2 names it (FindOrCreateTexture)
		var key = name.Replace( ':', '_' ).TrimStart( '/' ).ToLowerInvariant();
		var at = texture.native.GetBindingPtr().ToInt64();
		lock ( namedTextures )
		{
			if ( namedTextures.TryGetValue( at, out var known ) && known.Name == key )
			{
				if ( !SameDescription( known.Desc, made ) ) return false;
			}

			namedTextures[at] = (key, made);
			return true;
		}
	}

	static bool SameDescription( in NativeEngine.CTextureDesc a, in NativeEngine.CTextureDesc b ) => a.m_nWidth == b.m_nWidth && a.m_nHeight == b.m_nHeight && a.m_nDepth == b.m_nDepth
		&& a.m_nNumMipLevels == b.m_nNumMipLevels && a.m_nImageFormat == b.m_nImageFormat && a.m_nFlags == b.m_nFlags && a.m_nDecodeFlags == b.m_nDecodeFlags
		&& a.m_nDisplayRectWidth == b.m_nDisplayRectWidth && a.m_nDisplayRectHeight == b.m_nDisplayRectHeight;

	/// <summary>
	/// A render target only Borealis.Gpu draws with or into (a frame's targets handed to the engine's drawing, a widget's
	/// window target, a bitmap render's target). Where Source 2's device is kept beside Borealis.Gpu (the editor), its image
	/// on Source 2 is 1x1, never drawn with, rather than a full-size copy nothing uses; the texture describes itself at its
	/// own size, which is what Borealis.Gpu makes its backing from. Anywhere else this is <see cref="TextureBuilder.Create"/>.
	/// </summary>
	internal static Texture CreateOnBorealisGpu( TextureBuilder builder, string name )
	{
		if ( !Graphics.HasSource2Device || RenderBackend.Default is not BorealisGpuRenderBackend ) return builder.Create( name );

		var config = builder._config.GetWithFixes();
		var placeholder = builder;
		placeholder._config.m_nWidth = 1;
		placeholder._config.m_nHeight = 1;
		placeholder._config.m_nNumMipLevels = 1;
		var texture = placeholder.Create( name );
		if ( texture is null ) return null;

		var desc = texture.Desc;
		desc.m_nWidth = config.m_nWidth;
		desc.m_nHeight = config.m_nHeight;
		desc.m_nNumMipLevels = config.m_nNumMipLevels;
		desc.m_nDisplayRectWidth = config.m_nWidth;
		desc.m_nDisplayRectHeight = config.m_nHeight;
		texture._desc = desc;
		texture.gotdesc = true;
		texture.OnBorealisGpuOnly = true;
		return texture;
	}

	/// <summary>Made by <see cref="CreateOnBorealisGpu"/>: Source 2's image of it is a 1x1 placeholder.</summary>
	internal bool OnBorealisGpuOnly { get; private set; }
}
