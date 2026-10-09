using NativeEngine;
using System;

namespace Sandbox
{
	public struct TextureArrayBuilder
	{
		internal TextureBuilder config = new();
		internal int Width { set => config._config.m_nWidth = (short)value; }
		internal int Height { set => config._config.m_nHeight = (short)value; }
		internal int Count { set => config._config.m_nDepth = (short)value; } // Depth is used for count in rendersystemdx11
		internal ImageFormat Format { set => config._config.m_nImageFormat = value; }


		internal string _name = null;
		internal byte[] _data = null;
		internal int _dataLength = 0;
		internal IntPtr _dataPtr = IntPtr.Zero;
		internal bool _asAnonymous = true;

		internal bool HasData
		{
			get
			{
				return (_data != null || _dataPtr != IntPtr.Zero) && _dataLength > 0;
			}
		}

		public TextureArrayBuilder()
		{
			config._config.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_ARRAY;
			config._config.m_nDepth = 1;
		}

		#region Common methods

		/// <inheritdoc cref="TextureBuilder.WithStaticUsage"/>
		public TextureArrayBuilder WithStaticUsage()
		{
			config.WithStaticUsage();
			return this;
		}

		/// <inheritdoc cref="TextureBuilder.WithSemiStaticUsage"/>
		public TextureArrayBuilder WithSemiStaticUsage()
		{
			config.WithStaticUsage();
			return this;
		}

		/// <inheritdoc cref="TextureBuilder.WithDynamicUsage"/>
		public TextureArrayBuilder WithDynamicUsage()
		{
			config.WithDynamicUsage();
			return this;
		}

		/// <inheritdoc cref="TextureBuilder.WithGPUOnlyUsage"/>
		public TextureArrayBuilder WithGPUOnlyUsage()
		{
			config.WithGPUOnlyUsage();
			return this;
		}

		/// <inheritdoc cref="TextureBuilder.WithUAVBinding( bool )"/>
		public TextureArrayBuilder WithUAVBinding()
		{
			config.WithUAVBinding();
			return this;
		}

		/// <inheritdoc cref="TextureBuilder.WithMips"/>
		public TextureArrayBuilder WithMips( int mips )
		{
			config.WithMips( mips );
			return this;
		}

		/// <inheritdoc cref="TextureBuilder.WithFormat"/>
		public TextureArrayBuilder WithFormat( ImageFormat format )
		{
			config.WithFormat( format );
			return this;
		}

		/// <inheritdoc cref="TextureBuilder.WithScreenFormat"/>
		public TextureArrayBuilder WithScreenFormat()
		{
			config.WithScreenFormat();
			return this;
		}

		/// <inheritdoc cref="TextureBuilder.WithDepthFormat"/>
		public TextureArrayBuilder WithDepthFormat()
		{
			config.WithDepthFormat();
			return this;
		}

		/// <inheritdoc cref="TextureBuilder.WithMultiSample2X"/>
		public TextureArrayBuilder WithMultiSample2X()
		{
			return WithMultisample( MultisampleAmount.Multisample2x );
		}

		/// <inheritdoc cref="TextureBuilder.WithMultiSample4X"/>
		public TextureArrayBuilder WithMultiSample4X()
		{
			return WithMultisample( MultisampleAmount.Multisample4x );
		}

		/// <inheritdoc cref="TextureBuilder.WithMultiSample6X"/>
		public TextureArrayBuilder WithMultiSample6X()
		{
			return WithMultisample( MultisampleAmount.Multisample6x );
		}

		/// <inheritdoc cref="TextureBuilder.WithMultiSample8X"/>
		public TextureArrayBuilder WithMultiSample8X()
		{
			return WithMultisample( MultisampleAmount.Multisample8x );
		}

		/// <inheritdoc cref="TextureBuilder.WithMultiSample16X"/>
		public TextureArrayBuilder WithMultiSample16X()
		{
			return WithMultisample( MultisampleAmount.Multisample16x );
		}

		/// <inheritdoc cref="TextureBuilder.WithScreenMultiSample"/>
		public TextureArrayBuilder WithScreenMultiSample()
		{
			return WithMultisample( MultisampleAmount.MultisampleScreen );
		}

		#endregion

		/// <summary>
		/// Provide a name to identify the texture by, allowing it to be retrieved with <see cref="Texture.Load(string, bool)"/>.
		/// </summary>
		/// <param name="name">Desired texture name</param>
		public TextureArrayBuilder WithName( string name )
		{
			_name = name;
			return this;
		}

		/// <summary>
		/// Initialize texture with pre-existing texture data
		/// </summary>
		/// <param name="data">Texture data</param>
		public TextureArrayBuilder WithData( byte[] data )
		{
			return WithData( data, data.Length );
		}

		/// <summary>
		/// Initialize texture with pre-existing texture data
		/// </summary>
		/// <param name="data">Texture data</param>
		/// <param name="dataLength">How big our texture data is</param>
		public TextureArrayBuilder WithData( byte[] data, int dataLength )
		{
			if ( dataLength > data.Length )
			{
				throw new System.Exception( "Data length exceeds the data" );
			}
			if ( dataLength < 0 )
			{
				throw new System.Exception( "Data length is less than zero" );
			}

			_data = data;
			_dataLength = dataLength;
			return this;
		}

		/// <summary>
		/// Create a texture with data using an UNSAFE intptr
		/// </summary>
		/// <param name="data">Pointer to the data</param>
		/// <param name="dataLength">Length of the data</param>
		internal TextureArrayBuilder WithData( IntPtr data, int dataLength )
		{
			_dataPtr = data;
			_dataLength = dataLength;
			return this;
		}

		/// <summary>
		/// Define which how much multisampling the current texture should use
		/// </summary>
		/// <param name="amount">Multisampling amount</param>
		public TextureArrayBuilder WithMultisample( MultisampleAmount amount )
		{
			config.WithMSAA( amount );
			return this;
		}

		/// <summary>
		/// Set whether the texture is an anonymous texture or not
		/// </summary>
		/// <param name="isAnonymous">Set if it's anonymous or not</param>
		public TextureArrayBuilder WithAnonymous( bool isAnonymous )
		{
			_asAnonymous = isAnonymous;
			return this;
		}

		/// <summary>
		/// Build and create the actual texture
		/// </summary>
		public Texture Finish()
		{
			config._config.m_nNumMipLevels = Math.Max( config._config.m_nNumMipLevels, (short)1 );
			config._config.m_nWidth = Math.Max( config._config.m_nWidth, (short)1 );
			config._config.m_nHeight = Math.Max( config._config.m_nHeight, (short)1 );
			config._config.m_nDepth = Math.Max( config._config.m_nDepth, (short)1 );

			if ( config._config.m_nImageFormat == ImageFormat.Default )
				config._config.m_nImageFormat = ImageFormat.RGBA8888;

			if ( HasData )
			{
				int memoryRequiredForTexture = ImageLoader.GetMemRequired( config._config.m_nWidth, config._config.m_nHeight, config._config.m_nDepth, config._config.m_nImageFormat, false );
				if ( _dataLength < memoryRequiredForTexture )
				{
					throw new Exception( $"{_dataLength} is not enough data to create texture! {memoryRequiredForTexture} bytes are required! You're missing {_dataLength - memoryRequiredForTexture} bytes!" );
				}
				// We're passing excessive data in places which we need to fix up, commenting this out for now because it breaks a bunch of shit like thumbnails in the main menu
				/*
				else if ( _dataLength > memoryRequiredForTexture )
				{
					throw new Exception( $"{_dataLength} is too much data to create texture! {memoryRequiredForTexture} bytes are required! You have {_dataLength - memoryRequiredForTexture} extra bytes! Remove them" );
				}*/
			}

			if ( _dataPtr != IntPtr.Zero )
			{
				return Texture.Create( _name, _asAnonymous, config, _dataPtr, _dataLength );
			}

			return config.Create( _name, _asAnonymous, _data, _dataLength );
		}

		/// Custom methods

		/// <summary>
		/// Create texture with a predefined size
		/// </summary>
		/// <param name="width">Width in pixel</param>
		/// <param name="height">Height in pixels</param>
		public TextureArrayBuilder WithSize( int width = 1, int height = 1 )
		{
			Width = width;
			Height = height;
			return this;
		}

		/// <summary>
		/// Create texture array with this many textures
		/// </summary>
		public TextureArrayBuilder WithCount( int count = 1 )
		{
			Count = count;
			return this;
		}
	}
}
