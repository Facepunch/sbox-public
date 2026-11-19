using NativeEngine;
using System.IO;

namespace Sandbox
{
	public partial struct Texture2DBuilder
	{
		private readonly record struct Sequence( Rect[] Frames, float FrameRate, bool Loop );

		/// <summary>
		/// Adds an animation sequence to a static atlas texture, for particles to play. Sequences are
		/// numbered in the order they are added. The texture needs data for every mip level. If a texture
		/// with the same name already exists, that texture is returned and keeps its own sequences.
		/// </summary>
		/// <param name="frames">Where each frame is in the atlas, in 0..1 texture coordinates.</param>
		/// <param name="frameRate">Frames per second.</param>
		/// <param name="loop">Whether the sequence repeats, or holds its last frame.</param>
		public Texture2DBuilder AddSequence( ReadOnlySpan<Rect> frames, float frameRate, bool loop = true )
		{
			if ( frames.Length is < 1 or > 1024 )
				throw new ArgumentException( "A sequence needs between 1 and 1024 frames.", nameof( frames ) );
			if ( !float.IsFinite( frameRate ) || frameRate <= 0 || !float.IsFinite( 1 / frameRate ) )
				throw new ArgumentOutOfRangeException( nameof( frameRate ), "Sequence frame rate must be finite and positive." );
			if ( _sequences is { Length: >= 256 } )
				throw new InvalidOperationException( "A texture can have at most 256 sequences." );

			foreach ( var frame in frames )
			{
				if ( !(frame.Left >= 0 && frame.Top >= 0 && frame.Right <= 1 && frame.Bottom <= 1 && frame.Width > 0 && frame.Height > 0) )
					throw new ArgumentException( "Sequence frames need a size and have to be within the atlas.", nameof( frames ) );
			}

			_sequences = [.. _sequences ?? [], new Sequence( frames.ToArray(), frameRate, loop )];
			return this;
		}

		private unsafe Texture FinishWithSequences()
		{
			if ( !HasData || config._config.m_nUsage != TextureUsage.TEXTURE_USAGE_STATIC
				|| config._config.m_nMultisampleType != RenderMultisampleType.RENDER_MULTISAMPLE_NONE )
				throw new InvalidOperationException( "Sequences require static, non-multisampled texture data." );

			var required = ImageLoader.GetMemRequired( config._config.m_nWidth, config._config.m_nHeight, 1,
				config._config.m_nNumMipLevels, config._config.m_nImageFormat );
			if ( _dataLength != required )
				throw new InvalidOperationException( "Sequences require data for every mip level." );

			var nativeConfig = config._config.GetWithFixes();
			nativeConfig.m_nImageFormat.ThrowIfInvalid();

			// Native keys sequences by resource name, which anonymous textures don't have.
			var hasName = !string.IsNullOrWhiteSpace( _name );
			var nativeName = hasName && !_asAnonymous ? _name : $"runtime_sequences_{Guid.NewGuid():N}.vtex";
			var sequences = SerializeSequences();

			fixed ( byte* pixels = _data )
			fixed ( byte* data = sequences )
			{
				var native = g_pRenderDevice.FindOrCreateTextureWithSheet( nativeName, nativeConfig,
					_dataPtr != IntPtr.Zero ? _dataPtr : (IntPtr)pixels, _dataLength, (IntPtr)data, sequences.Length );
				var result = Texture.FromNative( native );
				if ( hasName ) result?.RegisterWeakResourceId( _name );
				return result;
			}
		}

		private byte[] SerializeSequences()
		{
			using var data = new MemoryStream();
			using var writer = new BinaryWriter( data );
			writer.Write( _sequences.Length );
			foreach ( var sequence in _sequences )
			{
				writer.Write( sequence.Loop ? 0 : 1 );
				writer.Write( sequence.Frames.Length );
				foreach ( var frame in sequence.Frames )
				{
					writer.Write( 1 / sequence.FrameRate );
					writer.Write( frame.Left ); writer.Write( frame.Top );
					writer.Write( frame.Right ); writer.Write( frame.Bottom );
				}
			}

			return data.ToArray();
		}
	}
}
