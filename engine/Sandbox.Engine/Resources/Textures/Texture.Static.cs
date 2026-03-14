
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

	internal static Texture Create( string name, bool anonymous, TextureBuilder builder, IntPtr data, int dataSize )
	{
		var config = builder._config.GetWithFixes();

		var texture = g_pRenderDevice.FindOrCreateTexture2( name, anonymous, config, data, dataSize );
		//bool isRenderTarget = builder.common.m_nFlags.HasFlag( RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET );

		if ( data == IntPtr.Zero || dataSize <= 0 )
		{
			g_pRenderDevice.ClearTexture( texture, builder._initialColor ?? Color.Transparent );
		}

		return FromNative( texture );
	}
}
