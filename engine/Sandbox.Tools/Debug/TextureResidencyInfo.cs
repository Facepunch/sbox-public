using NativeEngine;
using Sandbox;

namespace Editor;

/// <summary>
/// Provides information about currently resident textures on the GPU
/// </summary>
public struct TextureResidencyInfo
{
	public enum TextureDimension
	{
		_1D,
		_2D,
		_2DArray,
		_3D,
		Cube,
		CubeArray,
		Buffer
	}

	public enum TextureCategory
	{
		None = 0,
		RenderTarget = 1 << 0,
		DepthBuffer = 1 << 1,
		Streaming = 1 << 2,
		UAV = 1 << 3,
		Stale = 1 << 4,
		MSAA = 1 << 5,
	}

	public struct Desc
	{
		public int Width;
		public int Height;
		public int Depth;
		public long MemorySize;
	}

	public string Name;
	public TextureDimension Dimension;
	public ImageFormat Format;
	public Desc Loaded;
	public Desc Disk;
	public int MipCount;
	public int LastUsedFrames;
	public TextureCategory Categories;

	/// <summary>
	/// Managed Texture wrapper for this GPU-resident texture. May be null if
	/// the native handle could not be wrapped.
	/// </summary>
	public Texture Texture;

	static TextureResidencyInfo From( ITexture texture, Texture managedTexture, string name )
	{
		var loadedDesc = g_pRenderDevice.GetTextureDesc( texture );
		var diskDesc = g_pRenderDevice.GetOnDiskTextureDesc( texture );

		var loadedMemorySize = loadedDesc.ArrayCount * ImageLoader.GetMemRequired( loadedDesc.m_nWidth, loadedDesc.m_nHeight, loadedDesc.Depth, loadedDesc.m_nNumMipLevels, loadedDesc.m_nImageFormat );
		var diskMemorySize = diskDesc.ArrayCount * ImageLoader.GetMemRequired( diskDesc.m_nWidth, diskDesc.m_nHeight, diskDesc.Depth, diskDesc.m_nNumMipLevels, diskDesc.m_nImageFormat );

		var flags = loadedDesc.m_nFlags;
		var dimension = (flags & RuntimeTextureSpecificationFlags.TSPEC_CUBE_TEXTURE) != 0
		? (flags & RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_ARRAY) != 0 ? TextureDimension.CubeArray : TextureDimension.Cube
		: (flags & RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE) != 0 ? TextureDimension._3D
		: (flags & RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_ARRAY) != 0 ? TextureDimension._2DArray
		: TextureDimension._2D;

		// Build category flags
		var categories = TextureCategory.None;
		if ( managedTexture is not null && managedTexture.IsValid )
		{
			if ( managedTexture.IsRenderTarget )
				categories |= TextureCategory.RenderTarget;

			if ( managedTexture.UAVAccess )
				categories |= TextureCategory.UAV;

			if ( managedTexture.MultisampleType != NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_NONE )
				categories |= TextureCategory.MSAA;
		}

		if ( loadedDesc.m_nImageFormat.IsDepthFormat() )
			categories |= TextureCategory.DepthBuffer;

		if ( diskMemorySize > 0 && loadedMemorySize < diskMemorySize )
			categories |= TextureCategory.Streaming;

		var lastUsed = managedTexture is { IsValid: true } ? managedTexture.LastUsed : -1;
		if ( lastUsed >= 100 )
			categories |= TextureCategory.Stale;

		return new()
		{
			Name = name,
			Format = loadedDesc.m_nImageFormat,
			Dimension = dimension,
			Texture = managedTexture,
			MipCount = loadedDesc.m_nNumMipLevels,
			LastUsedFrames = lastUsed,
			Categories = categories,
			Loaded =
			{
				Width = loadedDesc.m_nWidth,
				Height = loadedDesc.m_nHeight,
				Depth = loadedDesc.m_nDepth,
				MemorySize = loadedMemorySize
			},
			Disk =
			{
				Width = diskDesc.m_nWidth,
				Height = diskDesc.m_nHeight,
				Depth = diskDesc.m_nDepth,
				MemorySize = diskMemorySize
			},
		};
	}

	/// <summary>
	/// Get info about all resident textures
	/// </summary>
	public static IEnumerable<TextureResidencyInfo> GetAll()
	{
		var ret = new List<TextureResidencyInfo>();

		var names = CUtlVectorString.Create( 8, 8 );
		var list = CUtlVectorTexture.Create( 8, 8 );
		g_pRenderDevice.GetTextureResidencyInfo( list, names );

		var count = list.Count();

		for ( int i = 0; i < count; i++ )
		{
			// CUtlVectorTexture.Element allocates a fresh strong handle on the C++ side
			// (HRenderTextureStrongCopyable). We must release it ourselves — otherwise
			// every diagnostic call would leak a ref and keep textures alive artificially.
			var texture = list.Element( i );
			var name = names.Element( i );

			// Look up an existing managed wrapper without taking a strong handle. Engine-owned
			// textures (render targets, depth buffers, etc.) won't be in the cache; that's fine —
			// the residency entry is still built from the native descriptor.
			NativeResourceCache.TryGetValue<Texture>( texture.GetBindingPtr().ToInt64(), out var managedTexture );

			// Generate a managed handle if we have a native-only texture, this will be found later in the texture cache
			if ( managedTexture is null || !managedTexture.IsValid )
			{
				managedTexture = Texture.FromNative( texture );
				ret.Add( From( texture, managedTexture, name ) );
			}
			else
			{
				ret.Add( From( texture, managedTexture, name ) );

			}


			if ( !texture.IsNull )
				texture.DestroyStrongHandle();

		}

		list.DeleteThis();
		names.DeleteThis();

		return ret;
	}
}
