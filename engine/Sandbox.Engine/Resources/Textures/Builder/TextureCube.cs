using NativeEngine;

namespace Sandbox;

public struct TextureCubeBuilder
{
	internal TextureBuilder builder = new();
	internal int Width { set => builder._config.m_nWidth = (short)value; }
	internal int Height { set => builder._config.m_nHeight = (short)value; }
	internal int Depth { set => builder._config.m_nDepth = (short)value; }
	internal ImageFormat Format { set => builder._config.m_nImageFormat = value; }


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

	public TextureCubeBuilder()
	{
		builder._config.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_CUBE_TEXTURE;
		builder._config.m_nDepth = 0;
	}


	#region Common methods

	/// <inheritdoc cref="TextureBuilder.WithStaticUsage"/>
	public TextureCubeBuilder WithStaticUsage()
	{
		builder.WithStaticUsage();
		return this;
	}

	/// <inheritdoc cref="TextureBuilder.WithSemiStaticUsage"/>
	public TextureCubeBuilder WithSemiStaticUsage()
	{
		builder.WithSemiStaticUsage();
		return this;
	}

	/// <inheritdoc cref="TextureBuilder.WithDynamicUsage"/>
	public TextureCubeBuilder WithDynamicUsage()
	{
		builder.WithDynamicUsage();
		return this;
	}

	/// <inheritdoc cref="TextureBuilder.WithGPUOnlyUsage"/>
	public TextureCubeBuilder WithGPUOnlyUsage()
	{
		builder.WithGPUOnlyUsage();
		return this;
	}

	/// <inheritdoc cref="TextureBuilder.WithUAVBinding( bool )"/>
	public TextureCubeBuilder WithUAVBinding()
	{
		builder.WithUAVBinding();
		return this;
	}

	/// <inheritdoc cref="TextureBuilder.WithMips"/>
	public TextureCubeBuilder WithMips( int mips )
	{
		builder.WithMips( mips );
		return this;
	}

	/// <inheritdoc cref="TextureBuilder.WithFormat"/>
	public TextureCubeBuilder WithFormat( ImageFormat format )
	{
		builder.WithFormat( format );
		return this;
	}

	/// <inheritdoc cref="TextureBuilder.WithScreenFormat"/>
	public TextureCubeBuilder WithScreenFormat()
	{
		builder.WithScreenFormat();
		return this;
	}

	/// <inheritdoc cref="TextureBuilder.WithDepthFormat"/>
	public TextureCubeBuilder WithDepthFormat()
	{
		builder.WithDepthFormat();
		return this;
	}

	/// <inheritdoc cref="TextureBuilder.WithMultiSample2X"/>
	public TextureCubeBuilder WithMultiSample2X()
	{
		return WithMultisample( MultisampleAmount.Multisample2x );
	}

	/// <inheritdoc cref="TextureBuilder.WithMultiSample4X"/>
	public TextureCubeBuilder WithMultiSample4X()
	{
		return WithMultisample( MultisampleAmount.Multisample4x );
	}

	/// <inheritdoc cref="TextureBuilder.WithMultiSample6X"/>
	public TextureCubeBuilder WithMultiSample6X()
	{
		return WithMultisample( MultisampleAmount.Multisample6x );
	}

	/// <inheritdoc cref="TextureBuilder.WithMultiSample8X"/>
	public TextureCubeBuilder WithMultiSample8X()
	{
		return WithMultisample( MultisampleAmount.Multisample8x );
	}

	/// <inheritdoc cref="TextureBuilder.WithMultiSample16X"/>
	public TextureCubeBuilder WithMultiSample16X()
	{
		return WithMultisample( MultisampleAmount.Multisample16x );
	}

	/// <inheritdoc cref="TextureBuilder.WithScreenMultiSample"/>
	public TextureCubeBuilder WithScreenMultiSample()
	{
		return WithMultisample( MultisampleAmount.MultisampleScreen );
	}

	#endregion

	/// <summary>
	/// Provide a name to identify the texture by
	/// </summary>
	/// <param name="name">Desired texture name</param>
	public TextureCubeBuilder WithName( string name )
	{
		_name = name;
		return this;
	}


	/// <summary>
	/// Initialize texture with pre-existing texture data
	/// </summary>
	/// <param name="data">Texture data</param>
	public TextureCubeBuilder WithData( byte[] data )
	{
		return WithData( data, data.Length );
	}

	/// <summary>
	/// Initialize texture with pre-existing texture data
	/// </summary>
	/// <param name="data">Texture data</param>
	/// <param name="dataLength">How big our texture data is</param>
	public TextureCubeBuilder WithData( byte[] data, int dataLength )
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
	internal TextureCubeBuilder WithData( IntPtr data, int dataLength )
	{
		_dataPtr = data;
		_dataLength = dataLength;
		return this;
	}

	/// <summary>
	/// Define which how much multisampling the current texture should use
	/// </summary>
	/// <param name="amount">Multisampling amount</param>
	public TextureCubeBuilder WithMultisample( MultisampleAmount amount )
	{
		builder.WithMSAA( amount );
		return this;
	}

	/// <summary>
	/// Set whether the texture is an anonymous texture or not
	/// </summary>
	/// <param name="isAnonymous">Set if it's anonymous or not</param>
	public TextureCubeBuilder WithAnonymous( bool isAnonymous )
	{
		_asAnonymous = isAnonymous;
		return this;
	}

	/// <summary>
	/// 
	/// </summary>
	public TextureCubeBuilder WithArrayCount( int count )
	{
		builder._config.m_nDepth = (short)count;
		return this;
	}

	/// <summary>
	/// Build and create the actual texture
	/// </summary>
	public Texture Finish()
	{

		if ( builder._config.m_nDepth > 1 )
		{
			builder._config.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_ARRAY;
		}

		builder._config.m_nNumMipLevels = Math.Max( builder._config.m_nNumMipLevels, (short)1 );
		builder._config.m_nWidth = Math.Max( builder._config.m_nWidth, (short)1 );
		builder._config.m_nHeight = Math.Max( builder._config.m_nHeight, (short)1 );
		builder._config.m_nDepth = Math.Max( builder._config.m_nDepth, (short)1 );

		// If UAV, imply we want to access it as an array for compute shaders too
		if ( (builder._config.m_nFlags & RuntimeTextureSpecificationFlags.TSPEC_UAV) != 0 )
		{
			builder._config.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_CUBE_CAN_SAMPLE_AS_ARRAY;
		}

		if ( builder._config.m_nImageFormat == ImageFormat.Default )
			builder._config.m_nImageFormat = ImageFormat.RGBA8888;

		if ( HasData )
		{
			int memoryRequiredForTexture = ImageLoader.GetMemRequired( builder._config.m_nWidth, builder._config.m_nHeight, builder._config.m_nDepth, builder._config.m_nNumMipLevels, builder._config.m_nImageFormat ) * 6;

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
			return Texture.Create( string.IsNullOrEmpty( _name ) ? "unnamed" : _name, _asAnonymous, builder, _dataPtr, _dataLength );
		}

		return builder.Create( string.IsNullOrEmpty( _name ) ? "unnamed" : _name, _asAnonymous, _data, _dataLength );
	}

	/// Custom methods

	/// <summary>
	/// Create texture with a predefined size
	/// </summary>
	/// <param name="width">Width in pixel</param>
	/// <param name="height">Height in pixels</param>
	public TextureCubeBuilder WithSize( int width, int height )
	{
		Width = width;
		Height = height;
		return this;
	}

	/// <summary>
	/// Create texture with a predefined size
	/// </summary>
	/// <param name="size">Width and Height in pixels</param>
	public TextureCubeBuilder WithSize( Vector2 size )
	{
		Width = size.x.CeilToInt();
		Height = size.y.CeilToInt();
		return this;
	}

	public TextureCubeBuilder AsRenderTarget()
	{
		builder._config.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET;
		builder._config.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET_SAMPLEABLE;

		builder._config.m_nUsage = TextureUsage.TEXTURE_USAGE_GPU_ONLY;
		builder._config.m_nScope = TextureScope.TEXTURE_SCOPE_GLOBAL;
		return this;
	}
}
