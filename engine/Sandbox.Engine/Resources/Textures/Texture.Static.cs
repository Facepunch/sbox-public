
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

		if ( data == IntPtr.Zero || dataSize <= 0 )
		{
			g_pRenderDevice.ClearTexture( texture, builder._initialColor ?? Color.Transparent );
		}

		var result = FromNative( texture );
		if ( !string.IsNullOrWhiteSpace( name ) )
			result?.RegisterWeakResourceId( name );

		if ( result is not null )
			result.CreatedMultisample = config.m_nMultisampleType;

		// Another device makes its copy from these rather than reading Source 2's back (with -borealisgpu, from before
		// the device is made too: without Source 2's device there's nothing to read back)
		if ( result is not null && (RenderBackend.Default is not Source2RenderBackend || RenderBackend.OthersExist || Graphics.BorealisGpuRequested) )
		{
			result.InitialPixels = data != IntPtr.Zero && dataSize > 0 ? new ReadOnlySpan<byte>( (void*)data, dataSize ).ToArray() : null;
			result.InitialColor = builder._initialColor ?? Color.Transparent;
		}

		return result;
	}
}
