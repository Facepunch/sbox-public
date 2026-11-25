using Borealis.Source2.Materials;
using NativeEngine;

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

		private Texture FinishWithSequences()
		{
			if ( !HasData || config._config.m_nUsage != TextureUsage.TEXTURE_USAGE_STATIC
				|| config._config.m_nMultisampleType != RenderMultisampleType.RENDER_MULTISAMPLE_NONE )
				throw new InvalidOperationException( "Sequences require static, non-multisampled texture data." );

			var required = ImageLoader.GetMemRequired( config._config.m_nWidth, config._config.m_nHeight, 1,
				config._config.m_nNumMipLevels, config._config.m_nImageFormat );
			if ( _dataLength != required )
				throw new InvalidOperationException( "Sequences require data for every mip level." );

			// A texture found by its name is returned as it is, keeping its own sequences
			var named = !string.IsNullOrWhiteSpace( _name ) && !_asAnonymous;
			if ( named && Texture.Find( _name ) is Texture existing && existing.IsValid )
			{
				Log.Warning( $"Texture '{_name}' already exists, keeping its sequences" );
				return existing;
			}

			var result = FinishTexture();
			result?.MadeWithSheet( MakeSheet() );
			return result;
		}

		/// <summary>The sequences as a sheet: one image a frame, cropped to nothing.</summary>
		private TextureSheet MakeSheet()
		{
			var sequences = new TextureSheet.Sequence[_sequences.Length];
			for ( int s = 0; s < sequences.Length; s++ )
			{
				var sequence = _sequences[s];
				var time = 1 / sequence.FrameRate;
				var frames = new TextureSheet.Frame[sequence.Frames.Length];
				for ( int f = 0; f < frames.Length; f++ )
				{
					var r = sequence.Frames[f];
					var rect = new System.Numerics.Vector4( r.Left, r.Top, r.Right, r.Bottom );
					frames[f] = new TextureSheet.Frame( time, [new TextureSheet.Image( rect, rect )] );
				}

				sequences[s] = new TextureSheet.Sequence( s, !sequence.Loop, false, time * frames.Length, frames );
			}

			return new TextureSheet( sequences );
		}
	}
}
