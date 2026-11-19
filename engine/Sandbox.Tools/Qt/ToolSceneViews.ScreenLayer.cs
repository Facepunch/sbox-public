using Sandbox.Rendering;
using System;
using System.Runtime.InteropServices;

namespace Editor;

internal static partial class ToolSceneViews
{
	/// <summary>
	/// A tool scene's screen layer (<c>CToolSceneTextureOverlay</c>, <c>CToolSceneBitmapOverlay</c>: texture and bitmap asset
	/// previews and their thumbnails), which native draws as a blit into the frame. A texture from a file is loaded by its
	/// name; a bitmap's pixels come with the request and are uploaded when they change. One channel alone is shown grey,
	/// made once from the texture's pixels. Drawn over the frame after post processing, alpha blended, as native's layer.
	/// </summary>
	internal sealed unsafe class ToolScreenLayer : IDisposable
	{
		/// <summary><c>TextureOverlayChannelChoice_t</c>.</summary>
		internal enum Channel
		{
			All,
			Red,
			Green,
			Blue,
			Alpha,
		}

		readonly RenderAttributes attributes = new();
		string textureName;
		Texture named;
		Texture pixelTexture;
		int pixelVersion = -1;
		Texture channelTexture;
		(Texture Source, Channel Channel) channelOf;
		Texture drawn;
		Rect rect;

		/// <summary>
		/// The request's layer, outside a render block: textures loaded or uploaded as needed.
		/// </summary>
		public void Update( ref Request request )
		{
			drawn = null;
			if ( request.OverlayRect.z <= 0 || request.OverlayRect.w <= 0 ) return;

			Texture texture = null;
			if ( request.OverlayPixels != null && request.OverlayWidth > 0 && request.OverlayHeight > 0 )
			{
				texture = Pixels( request.OverlayPixels, request.OverlayWidth, request.OverlayHeight, request.OverlayVersion );
			}
			else if ( request.OverlayTexture != IntPtr.Zero )
			{
				texture = Named( Marshal.PtrToStringUTF8( request.OverlayTexture ) );
			}

			if ( texture is null || !texture.IsValid ) return;

			var channel = (Channel)request.OverlayChannel;
			drawn = channel is > Channel.All and <= Channel.Alpha ? ChannelOf( texture, channel ) : texture;
			rect = new Rect( request.OverlayRect.x, request.OverlayRect.y, request.OverlayRect.z, request.OverlayRect.w );
		}

		Texture Pixels( byte* pixels, int width, int height, int version )
		{
			if ( pixelTexture is not null && pixelTexture.Width == width && pixelTexture.Height == height && pixelVersion == version ) return pixelTexture;

			pixelTexture?.Dispose();
			pixelTexture = Texture.Create( width, height, ImageFormat.BGRA8888 )
				.WithName( "Tool scene bitmap overlay" )
				.WithData( new ReadOnlySpan<byte>( pixels, width * height * 4 ) )
				.Finish();
			pixelVersion = version;
			return pixelTexture;
		}

		Texture Named( string name )
		{
			if ( string.IsNullOrEmpty( name ) ) return null;
			if ( name.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) name = name[..^2];
			if ( name == textureName ) return named;

			textureName = name;
			named = Texture.Load( name, false );
			return named;
		}

		/// <summary>One channel of <paramref name="texture"/> as grey, opaque: read back once per texture and channel.</summary>
		Texture ChannelOf( Texture texture, Channel channel )
		{
			if ( channelOf == (texture, channel) && channelTexture is not null ) return channelTexture;

			channelTexture?.Dispose();
			channelTexture = null;
			channelOf = (texture, channel);

			Color32[] pixels;
			try
			{
				pixels = texture.GetPixels();
			}
			catch ( Exception )
			{
				return texture;
			}

			var (width, height) = texture.ReadSize( 0 );
			if ( pixels.Length < width * height ) return texture;

			Grey( pixels, channel );
			channelTexture = Texture.Create( width, height, ImageFormat.RGBA8888 )
				.WithName( "Tool scene overlay channel" )
				.WithData( MemoryMarshal.AsBytes( pixels.AsSpan( 0, width * height ) ) )
				.Finish();
			return channelTexture;
		}

		/// <summary>Each pixel's <paramref name="channel"/> into its red, green and blue, alpha 255.</summary>
		internal static void Grey( Span<Color32> pixels, Channel channel )
		{
			for ( int i = 0; i < pixels.Length; i++ )
			{
				var p = pixels[i];
				var value = channel switch
				{
					Channel.Red => p.r,
					Channel.Green => p.g,
					Channel.Blue => p.b,
					_ => p.a,
				};
				pixels[i] = new Color32( value, value, value, 255 );
			}
		}

		/// <summary>The layer over the frame (the camera's <see cref="Stage.AfterPostProcess"/>).</summary>
		public void Draw()
		{
			if ( drawn is null ) return;

			attributes.Set( "Texture", drawn );
			Graphics.DrawQuad( rect, Material.UI.Basic, Color.White, attributes );
		}

		public void Dispose()
		{
			pixelTexture?.Dispose();
			pixelTexture = null;
			channelTexture?.Dispose();
			channelTexture = null;
			named = null;
			textureName = null;
			drawn = null;
		}
	}
}
