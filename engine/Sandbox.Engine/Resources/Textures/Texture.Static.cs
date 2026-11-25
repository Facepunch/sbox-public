
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
		config.m_nImageFormat.ThrowIfInvalid();

		var texture = g_pRenderDevice.FindOrCreateTexture2( string.IsNullOrEmpty( name ) ? "unnamed" : name, anonymous, config, data, dataSize );
		//bool isRenderTarget = builder.common.m_nFlags.HasFlag( RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET );

		var result = FromNative( texture );
		if ( !string.IsNullOrWhiteSpace( name ) )
			result?.RegisterWeakResourceId( name );

		if ( result is not null )
		{
			result.CreatedMultisample = config.m_nMultisampleType;
			result.MadeAtRunTime = true;
		}

		// An anonymous texture is always a new one, described by what it was made with; a named one may be one found by its
		// name (NamedDescription)
		if ( result is not null && !result.gotdesc && DescriptionOf( config, dataSize ) is var made )
		{
			var desc = anonymous ? made : NamedDescription( name, result, made );
			if ( desc is { } d )
			{
				result._desc = d;
				result.gotdesc = true;
				result.describedAtCreation = true;
			}
		}

		// The drawing device makes its copy from these (Source 2's empty device holds no pixels), and a headless process reads
		// them back; what's drawn makes a copy cleared (InitialColor)
		if ( result is not null )
		{
			result.InitialPixels = data != IntPtr.Zero && dataSize > 0 ? new ReadOnlySpan<byte>( (void*)data, dataSize ).ToArray() : null;
			result.InitialColor = builder._initialColor ?? Color.Transparent;
		}

		return result;
	}

	/// <summary>Named textures made at run time, by native texture: the name and description each was made with.</summary>
	static readonly Dictionary<long, (string Name, NativeEngine.CTextureDesc Desc)> namedTextures = new();

	/// <summary>
	/// The description of a texture made with a name, new to managed code: what it was made with (<paramref name="made"/>),
	/// unless it's a texture found by its name. One made here before at the same texture and name with another description
	/// was found, and keeps that description; a name that's a compiled file's that exists is described by the file (null).
	/// </summary>
	static NativeEngine.CTextureDesc? NamedDescription( string name, Texture texture, in NativeEngine.CTextureDesc made )
	{
		if ( string.IsNullOrEmpty( name ) ) return made;
		if ( name.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) || name.EndsWith( ".vtex_c", StringComparison.OrdinalIgnoreCase ) )
			return texture.FileHeader is null ? made : null;

		// As Source 2 names it (FindOrCreateTexture)
		var key = name.Replace( ':', '_' ).TrimStart( '/' ).ToLowerInvariant();
		var at = texture.native.GetBindingPtr().ToInt64();
		lock ( namedTextures )
		{
			if ( namedTextures.TryGetValue( at, out var known ) && known.Name == key && !SameDescription( known.Desc, made ) )
				return known.Desc;

			namedTextures[at] = (key, made);
			return made;
		}
	}

	/// <summary>
	/// The wrapper of a texture let its handle go: one made again with that name may be a new texture at the same place,
	/// described by what it's made with.
	/// </summary>
	static void ForgetNamed( long at )
	{
		lock ( namedTextures ) namedTextures.Remove( at );
	}

	static bool SameDescription( in NativeEngine.CTextureDesc a, in NativeEngine.CTextureDesc b ) => a.m_nWidth == b.m_nWidth && a.m_nHeight == b.m_nHeight && a.m_nDepth == b.m_nDepth
		&& a.m_nNumMipLevels == b.m_nNumMipLevels && a.m_nImageFormat == b.m_nImageFormat && a.m_nFlags == b.m_nFlags && a.m_nDecodeFlags == b.m_nDecodeFlags
		&& a.m_nDisplayRectWidth == b.m_nDisplayRectWidth && a.m_nDisplayRectHeight == b.m_nDisplayRectHeight;
}
