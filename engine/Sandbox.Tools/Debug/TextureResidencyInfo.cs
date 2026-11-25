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

	/// <summary>
	/// Get info about all resident textures
	/// </summary>
	public static IEnumerable<TextureResidencyInfo> GetAll()
	{
		// Borealis.Gpu's device holds the textures that are drawn with; Source 2's empty device only describes them
		if ( RenderBackend.Default is BorealisGpuRenderBackend gpu ) return FromBorealisGpu( gpu );
		return [];
	}

	/// <summary>
	/// Borealis.Gpu's textures: what its device holds, at the size it holds them. It doesn't stream or keep use counts, so
	/// what's loaded is what's on disk and nothing is stale.
	/// </summary>
	static List<TextureResidencyInfo> FromBorealisGpu( BorealisGpuRenderBackend gpu )
	{
		var ret = new List<TextureResidencyInfo>();
		foreach ( var texture in gpu.ResidentTextures() )
		{
			var categories = TextureCategory.None;
			if ( texture.RenderTarget ) categories |= TextureCategory.RenderTarget;
			if ( texture.Depthbuffer ) categories |= TextureCategory.DepthBuffer;
			if ( texture.Storage ) categories |= TextureCategory.UAV;
			if ( texture.Multisampled ) categories |= TextureCategory.MSAA;

			var desc = new Desc { Width = texture.Width, Height = texture.Height, Depth = texture.Volume ? texture.Depth : texture.Layers, MemorySize = texture.Bytes };
			ret.Add( new TextureResidencyInfo
			{
				Name = texture.Name,
				Format = texture.Format,
				Dimension = texture.Cube ? (texture.Array ? TextureDimension.CubeArray : TextureDimension.Cube) : texture.Volume ? TextureDimension._3D : texture.Array ? TextureDimension._2DArray : TextureDimension._2D,
				MipCount = texture.Mips,
				Categories = categories,
				Loaded = desc,
				Disk = desc,
			} );
		}

		return ret;
	}
}
