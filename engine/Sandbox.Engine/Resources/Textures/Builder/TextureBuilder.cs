
using NativeEngine;
using Sandbox;
using System.Runtime.InteropServices;

namespace NativeEngine
{
	[StructLayout( LayoutKind.Sequential )]
	internal struct TextureCreationConfig_t
	{
		public short m_nWidth;
		public short m_nHeight;
		public short m_nDepth;     // Doubles as slice count if TSPEC_TEXTURE_ARRAY is specified. Cannot do arrays of volume textures
		public short m_nNumMipLevels;
		public TextureDecodingFlags m_nDecodeFlags;
		public Sandbox.ImageFormat m_nImageFormat;
		public RuntimeTextureSpecificationFlags m_nFlags;

		public short m_nDisplayRectWidth;  // Width of the sub-rect of the texture that should actually be displayed
		public short m_nDisplayRectHeight; // Height of the sub-rect of the texture that should actually be displayed
		public short m_nMotionVectorsMaxDistanceInPixels;  // For motion vectors, the maximum distance that can be displaced per-frame

		public RenderMultisampleType m_nMultisampleType;
		public TextureUsage m_nUsage;
		public TextureScope m_nScope;
		public TextureOnDiskCompressionType m_compressionType;

		/// <summary>
		/// Get a version of this config with some fixes applied, to reduce the chance of runtime errors.
		/// </summary>
		public TextureCreationConfig_t GetWithFixes()
		{
			var fix = this;

			bool isDepth = fix.m_nImageFormat.IsDepthFormat();
			if ( isDepth )
			{
				fix.m_nFlags &= ~RuntimeTextureSpecificationFlags.TSPEC_UAV; // Depth textures cannot be UAVs
				if ( fix.m_nNumMipLevels > 1 ) fix.m_nNumMipLevels = 1;
			}

			// Remove UAV if texture has MSAA
			// shaderStorageImageMultisample is an *optional* feature in Vulkan for using MSAA with UAVs.
			// In HLSL (<6.7 anyway), RWTexture2DMS is not a thing, so we can't use it regardless
			if ( fix.m_nMultisampleType != RenderMultisampleType.RENDER_MULTISAMPLE_NONE )
			{
				fix.m_nFlags &= ~RuntimeTextureSpecificationFlags.TSPEC_UAV;
			}

			return fix;
		}

	}
}

namespace Sandbox
{

	[StructLayout( LayoutKind.Sequential )]
	public struct TextureBuilder
	{
		internal TextureCreationConfig_t _config;
		internal Color? _initialColor;

		// Common methods

		/// <summary>
		/// Once the texture is created it will be cleared to this color
		/// </summary>
		public readonly TextureBuilder WithInitialColor( in Color color )
		{
			var o = this;
			o._initialColor = color;
			return o;
		}

		/// <summary>
		/// Provides a hint to the GPU that this texture will not be modified.
		/// </summary>
		public TextureBuilder WithStaticUsage()
		{
			_config.m_nUsage = TextureUsage.TEXTURE_USAGE_STATIC;
			return this;
		}

		/// <summary>
		/// Provides a hint to the GPU that this texture will only be updated sometimes.
		/// </summary>
		public TextureBuilder WithSemiStaticUsage()
		{
			_config.m_nUsage = TextureUsage.TEXTURE_USAGE_SEMISTATIC;
			return this;
		}

		/// <summary>
		/// Provides a hint to the GPU that this texture will be updated regularly. (almost every frame)
		/// </summary>
		public TextureBuilder WithDynamicUsage()
		{
			_config.m_nUsage = TextureUsage.TEXTURE_USAGE_DYNAMIC;
			return this;
		}

		/// <summary>
		/// Specify the texture to ONLY be used on the GPU on not allow CPU access.
		/// </summary>
		public TextureBuilder WithGPUOnlyUsage()
		{
			_config.m_nUsage = TextureUsage.TEXTURE_USAGE_GPU_ONLY;
			return this;
		}

		public TextureBuilder WithSize( int width, int height )
		{
			_config.m_nWidth = (short)width;
			_config.m_nHeight = (short)height;
			return this;
		}

		public TextureBuilder WithSize( Vector2 size )
		{
			_config.m_nWidth = (short)size.x.CeilToInt();
			_config.m_nHeight = (short)size.y.CeilToInt();
			return this;
		}

		public TextureBuilder WithWidth( int width )
		{
			_config.m_nWidth = (short)width;
			return this;
		}

		public TextureBuilder WithHeight( int height )
		{
			_config.m_nHeight = (short)height;
			return this;
		}

		public TextureBuilder WithDepth( int depth )
		{
			_config.m_nDepth = (short)depth;
			return this;
		}

		public TextureBuilder WithMSAA( MultisampleAmount amount )
		{
			switch ( amount )
			{
				case MultisampleAmount.Multisample2x:
					_config.m_nMultisampleType = RenderMultisampleType.RENDER_MULTISAMPLE_2X;
					break;
				case MultisampleAmount.Multisample4x:
					_config.m_nMultisampleType = RenderMultisampleType.RENDER_MULTISAMPLE_4X;
					break;
				case MultisampleAmount.Multisample6x:
					_config.m_nMultisampleType = RenderMultisampleType.RENDER_MULTISAMPLE_6X;
					break;
				case MultisampleAmount.Multisample8x:
					_config.m_nMultisampleType = RenderMultisampleType.RENDER_MULTISAMPLE_8X;
					break;
				case MultisampleAmount.Multisample16x:
					_config.m_nMultisampleType = RenderMultisampleType.RENDER_MULTISAMPLE_16X;
					break;
				case MultisampleAmount.MultisampleNone:
					_config.m_nMultisampleType = RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
					break;
				case MultisampleAmount.MultisampleScreen:
					_config.m_nMultisampleType = Graphics.IdealMsaaLevel.ToEngine();
					break;
			}
			return this;
		}

		/// <summary>
		/// Sets the texture to use 2x multisampling.
		/// </summary>
		public TextureBuilder WithMultiSample2X() => WithMSAA( MultisampleAmount.Multisample2x );

		/// <summary>
		/// Sets the texture to use 4x multisampling.
		/// </summary>
		public TextureBuilder WithMultiSample4X() => WithMSAA( MultisampleAmount.Multisample4x );

		/// <summary>
		/// Sets the texture to use 6x multisampling.
		/// </summary>
		public TextureBuilder WithMultiSample6X() => WithMSAA( MultisampleAmount.Multisample6x );

		/// <summary>
		/// Sets the texture to use 8x multisampling.
		/// </summary>
		public TextureBuilder WithMultiSample8X() => WithMSAA( MultisampleAmount.Multisample8x );

		/// <summary>
		/// Sets the texture to use 16x multisampling.
		/// </summary>
		public TextureBuilder WithMultiSample16X() => WithMSAA( MultisampleAmount.Multisample16x );

		/// <summary>
		/// Sets the texture to use the same multisampling as whatever the screen/framebuffer uses
		/// </summary>
		public TextureBuilder WithScreenMultiSample() => WithMSAA( MultisampleAmount.MultisampleScreen );

		/// <summary>
		/// The internal texture format to use.
		/// </summary>
		/// <param name="format">Texture format</param>
		public TextureBuilder WithFormat( ImageFormat format )
		{
			_config.m_nImageFormat = format;
			return this;
		}

		/// <summary>
		/// Sets the internal texture format to use the same format as the screen/frame buffer.
		/// </summary>
		public TextureBuilder WithScreenFormat()
		{
			_config.m_nImageFormat = ImageFormat.RGBA8888;
			return this;
		}

		/// <summary>
		/// Uses the same depth format as what the screen/framebuffer uses.
		/// </summary>
		public TextureBuilder WithDepthFormat()
		{
			_config.m_nImageFormat = ImageFormat.D24S8;
			return this;
		}

		/// <summary>
		/// Generate amount of mip levels.
		/// </summary>
		/// <param name="mips">How many mips should be generated for this texture</param>
		public TextureBuilder WithMips( int? mips = default )
		{
			int numMips = mips ?? ((int)Math.Log2( Math.Min( _config.m_nWidth, _config.m_nHeight ) ) + 1);

			_config.m_nNumMipLevels = (short)numMips;
			return this;
		}

		/// <summary>
		/// Support binding the texture as a Unordered Access View in a compute or pixel shader.
		/// This is required for binding a texture within a compute shader.
		/// </summary>
		public TextureBuilder WithUAVBinding( bool uav = true )
		{
			if ( uav )
				_config.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_UAV;
			else
				_config.m_nFlags &= ~RuntimeTextureSpecificationFlags.TSPEC_UAV;

			return this;
		}

		/// <summary>
		/// Finish creating the texture.
		/// </summary>
		/// <param name="name">Name for the new texture.</param>
		/// <param name="anonymous">Whether this texture is anonymous.</param>
		/// <param name="data">Raw color data in correct format for the texture.</param>
		/// <param name="dataLength">Length of the <paramref name="data"/>.</param>
		/// <returns>The created texture.</returns>
		/// <exception cref="ArgumentException">Thrown when the texture size is invalid, i.e. less then or equal to 0 on either axis.</exception>
		public readonly Texture Create( string name = null, bool anonymous = true, ReadOnlySpan<byte> data = default, int dataLength = 0 )
		{
			// matt: wtf does this accept a Span which has a length and an int length parameter for... ???

			if ( _config.m_nWidth <= 0 || _config.m_nHeight <= 0 )
				throw new ArgumentException( $"Couldn't create texture - invalid size - {_config.m_nWidth} x {_config.m_nHeight}" );

			var depth = _config.m_nDepth;
			var faces = 1;
			if ( _config.m_nFlags.Contains( RuntimeTextureSpecificationFlags.TSPEC_CUBE_TEXTURE ) )
				faces *= 6;

			var mips = (_config.m_nNumMipLevels <= 0) ? 1 : _config.m_nNumMipLevels;
			var memoryRequiredWithMips = ImageLoader.GetMemRequired( _config.m_nWidth, _config.m_nHeight, depth, mips, _config.m_nImageFormat ) * faces;

			// Early out sanity checks if we have no data
			if ( data.IsEmpty || dataLength == 0 )
			{
				return CreateInternal( name, anonymous, ReadOnlySpan<byte>.Empty );
			}

			if ( dataLength <= 0 )
				dataLength = data.Length;

			if ( dataLength > data.Length )
				throw new ArgumentException( "Data length exceeds the data" );

			// HERE'S WHERE WE SANITY CHECK EVERYTHING TO PREVENT FUCKUPS

			var memoryRequired = ImageLoader.GetMemRequired( _config.m_nWidth, _config.m_nHeight, _config.m_nDepth, _config.m_nImageFormat, false ) * faces;
			if ( dataLength != memoryRequired && dataLength != memoryRequiredWithMips )
				throw new Exception( $"{dataLength} is wrong for this texture! {memoryRequired:n0} bytes are required (or {memoryRequiredWithMips:n0} with mips)! You sent {dataLength:n0} bytes!" );

			return CreateInternal( name, anonymous, data[..dataLength] );
		}

		internal readonly unsafe Texture CreateInternal( string name, bool anonymous, ReadOnlySpan<byte> data )
		{
			fixed ( byte* dataPtr = data )
			{
				return Texture.Create( string.IsNullOrEmpty( name ) ? "unnamed" : name, anonymous, this, data.IsEmpty ? IntPtr.Zero : (IntPtr)dataPtr, data.Length );
			}
		}
	}
}
