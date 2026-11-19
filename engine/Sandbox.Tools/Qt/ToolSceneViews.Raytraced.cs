using Sandbox.Rendering;
using System;

namespace Editor;

internal static partial class ToolSceneViews
{
	/// <summary>
	/// Hammer's raytraced baked lighting preview (<see cref="Flags.Raytraced"/>, <c>CBakedLightingInteractiveVisualizer</c>): a
	/// CPU path tracer fills an image block by block, which native draws in place of the scene (its <c>RaytracedImage</c>
	/// layer: the linear colour times the view's tonemap scalar) with its status text over it. Here the image is uploaded
	/// when its version moves, drawn over the frame after post processing (the screen-space tool layers draw after it), and
	/// the status text after the UI stage.
	/// </summary>
	internal sealed unsafe class ToolRaytraced : IDisposable
	{
		static readonly StringToken TextureName = "CustomTexture1";
		static readonly StringToken TintName = "ColorTint";

		readonly RenderAttributes attributes = new();
		Material material;
		Texture texture;
		float[] pixels = Array.Empty<float>();
		int version = -1;
		float scale = 1;
		string status;
		IntPtr statusPointer;
		ulong statusHash;

		/// <summary>Whether this frame draws the image.</summary>
		public bool Active { get; private set; }

		/// <summary>
		/// The request's image, outside a render block: uploaded when its version or size moved.
		/// </summary>
		public void Update( ref Request request )
		{
			Active = (request.DrawFlags & Flags.Raytraced) != 0;
			if ( !Active ) return;

			material ??= Material.FromShader( "shaders/copytexture.shader" );
			scale = request.RaytracedScale > 0 ? request.RaytracedScale : 1;
			status = StatusOf( request.StatusText );

			var width = request.RaytracedWidth;
			var height = request.RaytracedHeight;
			if ( request.RaytracedImage == null || width < 1 || height < 1 ) return;

			if ( texture is null || texture.Width != width || texture.Height != height )
			{
				texture?.Dispose();
				texture = Texture.Create( width, height, ImageFormat.RGBA32323232F ).WithName( "Hammer raytraced preview" ).Finish();
				version = -1;
			}

			if ( request.RaytracedVersion == version ) return;
			version = request.RaytracedVersion;

			var count = width * height;
			if ( pixels.Length != count * 4 ) pixels = new float[count * 4];
			Expand( new ReadOnlySpan<float>( request.RaytracedImage, count * 3 ), pixels );
			texture.Update<float>( pixels );
		}

		/// <summary>RGB floats into RGBA floats, alpha 1.</summary>
		internal static void Expand( ReadOnlySpan<float> rgb, Span<float> rgba )
		{
			var count = Math.Min( rgb.Length / 3, rgba.Length / 4 );
			for ( int i = 0; i < count; i++ )
			{
				rgba[i * 4 + 0] = rgb[i * 3 + 0];
				rgba[i * 4 + 1] = rgb[i * 3 + 1];
				rgba[i * 4 + 2] = rgb[i * 3 + 2];
				rgba[i * 4 + 3] = 1;
			}
		}

		string StatusOf( IntPtr utf8 )
		{
			if ( utf8 == IntPtr.Zero ) return null;

			// FNV-1a over the bytes: no string is made while they stay the same
			var bytes = (byte*)utf8;
			ulong hash = 14695981039346656037UL;
			int length = 0;
			for ( ; length < 256 && bytes[length] != 0; length++ )
				hash = (hash ^ bytes[length]) * 1099511628211UL;

			if ( utf8 == statusPointer && hash == statusHash ) return status;
			statusPointer = utf8;
			statusHash = hash;
			return length == 0 ? null : System.Text.Encoding.UTF8.GetString( bytes, length );
		}

		/// <summary>The image over the frame (the camera's <see cref="Stage.AfterPostProcess"/>), in linear colour as native's.</summary>
		public void DrawImage()
		{
			if ( !Active || texture is null ) return;

			attributes.Set( TextureName, texture );
			attributes.Set( TintName, new Vector4( scale, scale, scale, 1 ) );
			Graphics.Blit( material, attributes );
		}

		/// <summary>The status text, yellow with a black shadow, where native's debug overlay puts it (after the UI stage).</summary>
		public void DrawStatus()
		{
			if ( !Active || string.IsNullOrEmpty( status ) ) return;

			Graphics.DrawText( new Rect( 11, 16, 1024, 20 ), status, Color.Black, "Roboto", 13, 600, TextFlag.LeftTop );
			Graphics.DrawText( new Rect( 10, 15, 1024, 20 ), status, Color.Yellow, "Roboto", 13, 600, TextFlag.LeftTop );
		}

		public void Dispose()
		{
			texture?.Dispose();
			texture = null;
			version = -1;
			Active = false;
		}
	}
}
