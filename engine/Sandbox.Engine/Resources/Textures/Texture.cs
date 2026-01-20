using NativeEngine;

namespace Sandbox;

/// <summary>
/// A texture is an image used in rendering. Can be a static texture loaded from disk, or a dynamic texture rendered to by code.
/// Can also be 2D, 3D (multiple slices), or a cube texture (6 slices).
/// </summary>
[SkipHotload]
[ResourceType( "vtex" )]
public partial class Texture : Resource, IDisposable
{
	internal ITexture native;

	private CTextureDesc _desc;
	bool gotdesc;

	/// <summary>
	/// Allow the texture to keep a reference to its parent object (like a videoplayer).
	/// </summary>
	internal object ParentObject;

	/// <summary>
	/// Has the native handle changed?
	/// </summary>
	internal bool IsDirty;

	/// <summary>
	/// Whether this texture is an error or invalid or not.
	/// </summary>
	public bool IsError => native.IsNull || !native.IsStrongHandleValid() || native.IsError();

	public override bool IsValid => native.IsValid;

	/// <summary>
	/// Flags providing hints about this texture
	/// </summary>
	public TextureFlags Flags { get; set; } = TextureFlags.None;

	/// <summary>
	/// Private constructor, use <see cref="FromNative(ITexture)"/>
	/// </summary>
	private Texture( ITexture native )
	{
		if ( native.IsNull ) throw new Exception( "Texture pointer cannot be null!" );
		this.native = native;

		if ( native.IsStrongHandleValid() )
		{
			lock ( LoadedByPointer )
			{
				IntPtr targetPointer = native.GetBindingPtr();
				LoadedByPointer[targetPointer] = new WeakReference<Texture>( this );
			}
		}

		UpdateSheetInfo();
	}

	~Texture()
	{
		Dispose();
	}

	/// <summary>
	/// Texture index. Bit raw dog and needs a higher level abstraction.
	/// </summary>
	public int Index => g_pRenderDevice.GetTextureViewIndex( native, 0, RenderTextureDimension.RENDER_TEXTURE_DIMENSION_2D );

	/// <summary>
	/// Replace our strong handle with a copy of the strong handle of the passed texture
	/// Which means that this texture will invisibly become that texture.
	/// I suspect that there might be a decent way to do this in native using the resource system.
	/// In which case we should change all this code to use that way instead of doing this.
	/// </summary>
	internal void CopyFrom( Texture texture )
	{
		// Important - dispose the old handle, we're done with it!
		// if we don't do this, we'll leak memory!
		Dispose();

		// Copy the handle from the other texture.
		// Important - we can't just use the handle because when
		// they release it, it'll be a hanging pointer!
		native = texture.native.CopyStrongHandle();

		gotdesc = false;
		_desc = default;

		IsDirty = true;
	}

	internal CTextureDesc Desc
	{
		get
		{
			if ( gotdesc ) return _desc;

			gotdesc = true;
			_desc = g_pRenderDevice.GetOnDiskTextureDesc( native );
			return _desc;
		}
	}

	/// <summary>
	/// Width of the texture in pixels.
	/// </summary>
	public int Width => Desc.m_nWidth;

	/// <summary>
	/// Height of the texture in pixels.
	/// </summary>
	public int Height => Desc.m_nHeight;

	/// <summary>
	/// Depth of a 3D texture in pixels, or slice count for 2D texture arrays, or 6 for slices of cubemap.
	/// </summary>
	public int Depth => Desc.IsCube ? 6 : Desc.m_nDepth;

	/// <summary>
	/// Number of <a href="https://en.wikipedia.org/wiki/Mipmap">mip maps</a> this texture has.
	/// </summary>
	public int Mips => Desc.m_nNumMipLevels;

	/// <summary>
	/// Returns a Vector2 representing the size of the texture (width, height)
	/// </summary>
	public Vector2 Size => new( Width, Height );

	/// <summary>
	/// Whether this texture has finished loading or not.
	/// </summary>
	public bool IsLoaded { get; internal set; } = true;

	/// <summary>
	/// Image format of this texture.
	/// </summary>
	public ImageFormat ImageFormat => Desc.m_nImageFormat;

	/// <summary>
	/// Returns how many frames ago this texture was last used by the renderer
	/// </summary>
	public int LastUsed => native.IsValid ? g_pRenderDevice.GetTextureLastUsed( native ).Clamp( 0, 1000 ) : 1000;

	/// <summary>
	/// Gets if the texture has UAV access
	/// </summary>
	public bool UAVAccess => Desc.m_nFlags.HasFlag( RuntimeTextureSpecificationFlags.TSPEC_UAV );

	internal RenderMultisampleType MultisampleType
	{
		get
		{
			return native.IsValid ? g_pRenderDevice.GetTextureMultisampleType( native ) : RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
		}
	}

	/// <summary>
	/// Will release the handle for this texture. If the texture isn't referenced by anything
	/// else it'll be released properly. This will happen anyway because it's called in the destructor.
	/// By calling it manually you're just telling the engine you're done with this texture right now
	/// instead of waiting for the garbage collector.
	/// </summary>
	public void Dispose()
	{
		if ( !native.IsNull )
		{
			if ( native.IsStrongHandleValid() )
			{
				IntPtr targetPointer = native.GetBindingPtr();

				lock ( LoadedByPointer )
				{
					LoadedByPointer.Remove( targetPointer );
				}
			}

			native.DestroyStrongHandle();
			native = IntPtr.Zero;
		}
	}

	internal void TryReload( BaseFileSystem filesystem, string filename )
	{
		//
		// Try to load the texture again, make a new texture
		//
		using var newTex = TryToLoad( filesystem, filename, false );

		//
		// If success, copy from this texture
		//
		if ( newTex != null )
		{
			CopyFrom( newTex );
		}
	}

	/// <summary>
	/// If this texture is a sprite sheet, will return information about the sheet, which
	/// is generally used in the shader. You don't really need to think about the contents.
	/// </summary>
	public Vector4 SequenceData
	{
		get
		{
			// I think this would be safe to cache off, right?
			return g_pRenderDevice.GetSheetInfo( native );
		}
	}

	/// <summary>
	/// The count of sequences in this texture, if any. The rest of the sequence data is encoded into the texture itself.
	/// </summary>
	public int SequenceCount { get; private set; }

	class SequenceInfo
	{
		public float Length { get; set; }
		public int FrameCount { get; internal set; }
		public bool Looped { get; internal set; }
	}

	SequenceInfo[] sequences;

	/// <summary>
	/// Get the frame count for this sequence
	/// </summary>
	public int GetSequenceFrameCount( int sequenceId )
	{
		if ( sequences is null ) return 0;
		if ( sequenceId < 0 ) return 0;
		if ( sequenceId >= sequences.Length ) return 0;

		return sequences[sequenceId].FrameCount;
	}

	/// <summary>
	/// Get the total length of this seqence
	/// </summary>
	private float GetSequenceLength( int sequenceId )
	{
		if ( sequences is null ) return 0;
		if ( sequenceId < 0 ) return 0;
		if ( sequenceId >= sequences.Length ) return 0;

		return sequences[sequenceId].Length;
	}

	/// <summary>
	/// TODO: Fill this out, build a structure of Sequence[] for people to access
	/// Then make it so we can actually preview them
	/// </summary>
	private void UpdateSheetInfo()
	{
		HasAnimatedSequences = false;

		SequenceCount = g_pRenderDevice.GetSequenceCount( native );
		if ( SequenceCount <= 0 ) return;

		var s = new List<SequenceInfo>();

		for ( int i = 0; i < SequenceCount; i++ )
		{
			var info = new SequenceInfo();
			s.Add( info );

			SheetSequence_t seq = g_pRenderDevice.GetSequence( native, i );

			info.Length = seq.m_flTotalTime;
			info.FrameCount = seq.FrameCount();
			info.Looped = !seq.m_bClamp;

			HasAnimatedSequences = HasAnimatedSequences || info.FrameCount > 1;
		}

		sequences = s.ToArray();
	}

	public bool HasAnimatedSequences { get; private set; }

	/// <summary>
	/// Tells texture streaming this texture is being used.
	/// This is usually automatic, but useful for bindless pipelines.
	/// </summary>
	public void MarkUsed( int requiredMipSize = 0 )
	{
		g_pRenderDevice.MarkTextureUsed( native, requiredMipSize );
	}

	internal bool IsRenderTarget => g_pRenderDevice.IsTextureRenderTarget( native );

	/// <summary>
	/// Mark this texture as loading, create a texture in a task, replace this texture with it, mark it as loaded.
	/// This is for situations where you create a placeholder texture then replace it with the real texture later.
	/// </summary>
	internal async Task ReplacementAsync( Task<Texture> task )
	{
		IsLoaded = false;

		//
		// Wait for the new texture to exist
		//
		var texture = await task;

		//
		// replace us with the new texture
		//
		if ( texture is not null && texture != this )
		{
			this.CopyFrom( texture );
		}

		IsLoaded = true;
	}
}

/// <summary>
/// Flags providing hints about a texture
/// </summary>
public enum TextureFlags
{
	None = 0,

	/// <summary>
	/// Hint that this texture has pre-multiplied alpha
	/// </summary>
	PremultipliedAlpha = 1 << 0,
}
