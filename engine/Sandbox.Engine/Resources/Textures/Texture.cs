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
	/// Bumped when the native handle is swapped, so the size may have changed. A version, not
	/// a flag - one texture can be shared by many panels, and a flag is consumed by whichever
	/// one happens to rebuild first, leaving the rest stale.
	/// </summary>
	internal int DirtyVersion;

	/// <summary>
	/// Whether this texture is an error or invalid or not.
	/// </summary>
	public override bool IsError => native.IsNull || !native.IsStrongHandleValid() || native.IsError();

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
	}

	~Texture()
	{
		Destroy();
	}

	/// <summary>
	/// Texture index. Bit raw dog and needs a higher level abstraction.
	/// </summary>
	public int Index => Graphics.Backend.TextureIndex( this );

	/// <summary>
	/// Replace our strong handle with a copy of the strong handle of the passed texture
	/// Which means that this texture will invisibly become that texture.
	/// I suspect that there might be a decent way to do this in native using the resource system.
	/// In which case we should change all this code to use that way instead of doing this.
	/// </summary>
	/// <summary>
	/// What the texture was made with, kept when the process draws on a device other than Source 2's
	/// (<see cref="RenderBackend.Default"/>) or has another device beside it, which makes its copy from them: the pixel
	/// data, or the colour it was cleared to.
	/// Null once the texture has been changed since.
	/// </summary>
	internal byte[] InitialPixels { get; set; }
	internal Color? InitialColor { get; set; }

	/// <summary>
	/// Made in code (<see cref="Create(string, bool, TextureBuilder, IntPtr, int)"/>) rather than loaded from a file: its
	/// name, if it has one, isn't a file another device could read it from.
	/// </summary>
	internal bool MadeAtRunTime { get; set; }

	internal void CopyFrom( Texture texture )
	{
		if ( !texture.IsValid() )
			return;

		// What other devices made of the old contents is stale
		RenderBackend.ReleaseEverywhere( this );
		MadeAtRunTime = texture.MadeAtRunTime;
		InitialPixels = texture.InitialPixels;
		InitialColor = texture.InitialColor;

		if ( !native.IsNull )
		{
			var n = native;
			native = IntPtr.Zero;

			// Evict from NativeResourceCache so a new wrapper can be created
			// if the same native pointer is reused (e.g. RenderTarget pool, TextBlock rebuild).
			NativeResourceCache.Remove( n.GetBindingPtr().ToInt64() );

			MainThread.Queue( () => n.DestroyStrongHandle() );
		}

		// Copy the handle from the other texture.
		// Important - we can't just use the handle because when
		// they release it, it'll be a hanging pointer!
		native = texture.native.CopyStrongHandle();

		IsAnimated = texture.IsAnimated;

		// Described again from the new handle: the same as the other texture's
		gotdesc = texture.gotdesc;
		_desc = texture._desc;
		describedAtCreation = texture.describedAtCreation;
		fileHeader = texture.fileHeader;
		fileHeaderRead = texture.fileHeaderRead;
		sheetRead = false;

		DirtyVersion++;
	}

	/// <summary>How many textures were described from their files, and how many by asking Source 2, for diagnostics.</summary>
	internal static int DescribedFromFiles, DescribedBySource2;

	/// <summary>The first textures described by asking Source 2.</summary>
	internal static readonly System.Collections.Concurrent.ConcurrentQueue<string> DescribedBySource2Names = new();

	internal CTextureDesc Desc
	{
		get
		{
			if ( gotdesc ) return _desc;

			// Without Source 2's device, a loaded texture is described from its compiled file as Source 2 describes it
			if ( FileHeader is { } header && DescriptionOf( header ) is { } fromFile )
			{
				_desc = fromFile;
				gotdesc = true;
				System.Threading.Interlocked.Increment( ref DescribedFromFiles );
				return _desc;
			}

			// Else (a texture made with a name, one whose file isn't found, a device of Source 2's) Source 2's description
			if ( System.Threading.Interlocked.Increment( ref DescribedBySource2 ) <= 64 ) DescribedBySource2Names.Enqueue( ResourcePath ?? RenderTools.GetTextureResourceName( native ) );
			// No description at all (a texture that fell back to the error texture on Source 2's empty render system): an
			// empty one, which reads as a failed load, rather than a null dereference
			ref var desc = ref g_pRenderDevice.GetOnDiskTextureDesc( native );
			_desc = System.Runtime.CompilerServices.Unsafe.IsNullRef( ref desc ) ? default : desc;
			gotdesc = true;
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
	/// True if this is a multi-frame animated image (GIF, animated WebP) driven by <see cref="Tick"/>.
	/// </summary>
	public bool IsAnimated { get; internal set; }

	/// <summary>
	/// Image format of this texture.
	/// </summary>
	public ImageFormat ImageFormat => Desc.m_nImageFormat;

	/// <summary>
	/// Returns how many frames ago this texture was last used by the renderer
	/// </summary>
	public int LastUsed => !native.IsValid ? 1000
		: RenderBackend.Default is Source2RenderBackend ? g_pRenderDevice.GetTextureLastUsed( native ).Clamp( 0, 1000 )
		: (int)Math.Clamp( (long)(Application.FrameCount - Math.Max( _markedUsedFrame, _deviceUsedFrame )), 0, 1000 ); // Another device draws: when it last used it

	/// <summary>
	/// Gets if the texture has UAV access
	/// </summary>
	public bool UAVAccess => Desc.m_nFlags.HasFlag( RuntimeTextureSpecificationFlags.TSPEC_UAV );

	internal RenderMultisampleType MultisampleType
	{
		get
		{
			if ( !native.IsValid ) return RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
			return Graphics.HasSource2Device ? g_pRenderDevice.GetTextureMultisampleType( native ) : CreatedMultisample;
		}
	}

	/// <summary>What the texture was made with, for answering without Source 2's device (<see cref="Graphics.HasSource2Device"/>).</summary>
	internal RenderMultisampleType CreatedMultisample { get; set; }

	internal override void Destroy()
	{
		if ( !native.IsNull )
		{
			var n = native;
			native = IntPtr.Zero;

			// Evict from NativeResourceCache so a new wrapper can be created
			// if the same native pointer is reused (e.g. RenderTarget pool, TextBlock rebuild).
			NativeResourceCache.Remove( n.GetBindingPtr().ToInt64() );

			MainThread.Queue( () => n.DestroyStrongHandle() );
		}

		base.Destroy();
	}

	/// <summary>
	/// Will release the handle for this texture. If the texture isn't referenced by anything
	/// else it'll be released properly. This will happen anyway because it's called in the destructor.
	/// By calling it manually you're just telling the engine you're done with this texture right now
	/// instead of waiting for the garbage collector.
	/// </summary>
	public void Dispose()
	{
		RenderBackend.ReleaseEverywhere( this );
		Destroy();
	}

	internal void TryReload( BaseFileSystem filesystem, string filename )
	{
		//
		// Try to load the texture again, make a new texture
		//
		var newTex = TryToLoad( filesystem, filename, false );

		//
		// FromNative can return this same cached wrapper - nothing to copy.
		//
		if ( newTex is null || ReferenceEquals( newTex, this ) )
			return;

		//
		// If success, copy from this texture, always releasing the temporary handle.
		//
		try
		{
			CopyFrom( newTex );
		}
		finally
		{
			newTex.Dispose();
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
			// On Borealis.Gpu, its own sheet cache's: Source 2's has nothing under -borealisgpu
			if ( RenderBackend.Default is BorealisGpuRenderBackend borealis ) return borealis.SheetData( this );

			// I think this would be safe to cache off, right?
			return g_pRenderDevice.GetSheetInfo( native );
		}
	}

	/// <summary>
	/// The count of sequences in this texture, if any. The rest of the sequence data is encoded into the texture itself.
	/// </summary>
	public int SequenceCount
	{
		get
		{
			ReadSheet();
			return sequenceCount;
		}
	}

	int sequenceCount;

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
		ReadSheet();
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
		ReadSheet();
		if ( sequences is null ) return 0;
		if ( sequenceId < 0 ) return 0;
		if ( sequenceId >= sequences.Length ) return 0;

		return sequences[sequenceId].Length;
	}

	// Read on first use: from the texture's file where it describes itself from it (FileHeader), else from Source 2
	bool sheetRead;

	/// <summary>
	/// TODO: Fill this out, build a structure of Sequence[] for people to access
	/// Then make it so we can actually preview them
	/// </summary>
	private void ReadSheet()
	{
		if ( sheetRead ) return;

		var animated = false;
		SequenceInfo[] read = null;
		if ( describedAtCreation )
		{
			// Made at run time: no sheet
		}
		else if ( FileHeader is { } header )
		{
			if ( header.Sheet is { Count: > 0 } sheet )
			{
				read = new SequenceInfo[sheet.Count];
				for ( int i = 0; i < sheet.Count; i++ )
				{
					read[i] = new SequenceInfo { Length = sheet[i].TotalTime, FrameCount = sheet[i].Frames.Count, Looped = !sheet[i].Clamp };
					animated |= read[i].FrameCount > 1;
				}
			}
		}
		else if ( !native.IsNull )
		{
			var count = g_pRenderDevice.GetSequenceCount( native );
			if ( count > 0 )
			{
				read = new SequenceInfo[count];
				for ( int i = 0; i < count; i++ )
				{
					SheetSequence_t seq = g_pRenderDevice.GetSequence( native, i );
					read[i] = new SequenceInfo { Length = seq.m_flTotalTime, FrameCount = seq.FrameCount(), Looped = !seq.m_bClamp };
					animated |= read[i].FrameCount > 1;
				}
			}
		}

		sequences = read;
		sequenceCount = read?.Length ?? 0;
		hasAnimatedSequences = animated;
		sheetRead = true;
	}

	public bool HasAnimatedSequences
	{
		get
		{
			ReadSheet();
			return hasAnimatedSequences;
		}
	}

	bool hasAnimatedSequences;

	ulong _markedUsedFrame;
	int _markedUsedMipSize;

	// The frame another device than Source 2's last used it, for LastUsed; set from any thread
	ulong _deviceUsedFrame;

	/// <summary>Used by the device drawing (when it isn't Source 2's, which keeps its own count) this frame.</summary>
	internal void UsedOnDevice() => _deviceUsedFrame = Application.FrameCount;

	/// <summary>
	/// Tells texture streaming this texture is being used.
	/// This is usually automatic, but useful for bindless pipelines.
	/// </summary>
	public void MarkUsed( int requiredMipSize = 0 )
	{
		// Streaming only needs one touch per texture per frame, skip repeat calls
		// unless they're asking for more detail than we already reported.
		if ( _markedUsedFrame == Application.FrameCount && requiredMipSize <= _markedUsedMipSize )
			return;

		_markedUsedFrame = Application.FrameCount;
		_markedUsedMipSize = requiredMipSize;

		// Source 2's empty device streams nothing
		if ( Graphics.HasSource2Device ) g_pRenderDevice.MarkTextureUsed( native, requiredMipSize );
	}

	internal bool IsRenderTarget => Graphics.HasSource2Device ? g_pRenderDevice.IsTextureRenderTarget( native ) : Desc.m_nFlags.Contains( RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET );

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

			// update any animation instance (eg for gifs) to point to this as well
			if ( Animations.FirstOrDefault( x => x.Texture.TryGetTarget( out var t ) && ReferenceEquals( t, texture ) ) is { } animation )
			{
				animation.Texture.SetTarget( this );
			}

			texture.Dispose();
		}

		IsLoaded = true;
	}
}

/// <summary>
/// Flags providing hints about a texture
/// </summary>
[Expose]
public enum TextureFlags
{
	None = 0,

	/// <summary>
	/// Hint that this texture has pre-multiplied alpha
	/// </summary>
	PremultipliedAlpha = 1 << 0,
}
