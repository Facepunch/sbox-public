using NativeEngine;
using Sandbox;
using System;

namespace Editor;

/// <summary>
/// Provides information about currently resident textures on the GPU
/// </summary>
public class TextureResidencyInfo
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
	public int RefCount;
	public TextureCategory Categories;

	private WeakReference<Texture> _texture;

	/// <summary>
	/// Managed wrapper, if one already exists. Weak: a snapshot holding these strongly would
	/// keep every listed texture alive, so the viewer could never show anything being freed.
	/// </summary>
	public Texture Texture
	{
		get => _texture is not null && _texture.TryGetTarget( out var texture ) ? texture : null;
		private set => _texture = value is null ? null : new WeakReference<Texture>( value );
	}

	static TextureResidencyInfo From( ITexture texture, Texture managedTexture, string name, int refCount )
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

		// Straight off the native handle. Going via a managed Texture would mean wrapping every
		// resident texture just to list it, and each wrapper gets held by NativeResourceCache.
		var categories = TextureCategory.None;

		if ( g_pRenderDevice.IsTextureRenderTarget( texture ) )
			categories |= TextureCategory.RenderTarget;

		if ( flags.HasFlag( RuntimeTextureSpecificationFlags.TSPEC_UAV ) )
			categories |= TextureCategory.UAV;

		if ( g_pRenderDevice.GetTextureMultisampleType( texture ) != RenderMultisampleType.RENDER_MULTISAMPLE_NONE )
			categories |= TextureCategory.MSAA;

		if ( loadedDesc.m_nImageFormat.IsDepthFormat() )
			categories |= TextureCategory.DepthBuffer;

		if ( diskMemorySize > 0 && loadedMemorySize < diskMemorySize )
			categories |= TextureCategory.Streaming;

		var lastUsed = g_pRenderDevice.GetTextureLastUsed( texture ).Clamp( 0, 1000 );
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
			RefCount = refCount,
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
		var refCounts = CUtlVectorUInt32.Create( 8, 8 );
		g_pRenderDevice.GetTextureResidencyInfo( list, names, refCounts );

		var count = list.Count();

		try
		{
			for ( int i = 0; i < count; i++ )
			{
				// CUtlVectorTexture.Element allocates a fresh strong handle on the C++ side
				// (HRenderTextureStrongCopyable, +1 refcount). We own that handle and must
				// release it ourselves unless we hand ownership to a managed Texture wrapper —
				// otherwise every diagnostic call would leak a ref and keep textures alive artificially.
				var texture = list.Element( i );
				var name = names.Element( i );
				var refCount = (int)refCounts.Element( i );

				// Refcount is -1 so we dont self-reference the ref we have from this list
				refCount = Math.Max( 0, refCount - 1 );

				// Look up an existing managed wrapper without taking another strong handle. Engine-owned
				// textures (render targets, depth buffers, etc.) won't be in the cache.
				NativeResourceCache.TryPeek<Texture>( texture.GetBindingPtr().ToInt64(), out var managedTexture );

				// Everything displayed comes off the native handle, so no wrapper is created
				// here. Release the reference Element() allocated for us either way.
				ret.Add( From( texture, managedTexture, name, refCount ) );

				if ( !texture.IsNull )
					texture.DestroyStrongHandle();
			}
		}
		finally
		{
			list.DeleteThis();
			names.DeleteThis();
			refCounts.DeleteThis();
		}

		return ret;
	}
}
