using Borealis.Source2.Materials;
using NativeEngine;
using System.Threading;

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
	/// What the texture was made with, until the device that draws it has made its copy from it: a process whose only device is
	/// Borealis.Gpu's (<see cref="RenderBackend.IsOnlyDevice"/>) lets the pixels go then, and what reads them back
	/// (<see cref="GetPixels(int)"/> and the rest) reads that copy. Kept where another device might still make its own copy from them,
	/// and where nothing draws (<see cref="HeadlessRenderBackend"/> reads them back): the pixel data, or the colour it was cleared to.
	/// Null once the texture has been changed since.
	/// </summary>
	internal byte[] InitialPixels { get => initialPixels; set => initialPixels = value; }
	byte[] initialPixels;

	/// <summary>
	/// Let <see cref="InitialPixels"/> go if they're still <paramref name="consumed"/>, the array a device made its copy from:
	/// pixels given to the texture since (<see cref="CopyFrom"/> on another thread) are kept for the copy made from them.
	/// </summary>
	internal void ForgetInitialPixels( byte[] consumed ) => System.Threading.Interlocked.CompareExchange( ref initialPixels, null, consumed );

	internal Color? InitialColor { get; set; }

	/// <summary>
	/// Made in code (<see cref="Create(string, bool, TextureBuilder, IntPtr, int)"/>) rather than loaded from a file: its
	/// name, if it has one, isn't a file another device could read it from.
	/// </summary>
	internal bool MadeAtRunTime { get; set; }

	/// <summary>
	/// A view Borealis rendered into a texture of its own drew this render target last (a command list's
	/// <c>DrawReflection</c> or <c>DrawView</c> on Borealis.Gpu): what reads it through the engine reads that texture, until the
	/// engine writes the target itself.
	/// </summary>
	internal bool DrawnByView
	{
		get;
		set
		{
			if ( value == field ) return;
			field = value;
			Interlocked.Increment( ref viewDraws );
		}
	}

	/// <summary>Publish a view's backing, invalidating lowered bindings if it took over or replaced another backing.</summary>
	internal void MarkDrawnByView( bool bindingChanged )
	{
		if ( bindingChanged && DrawnByView ) Interlocked.Increment( ref viewDraws );
		else DrawnByView = true;
	}

	static int viewDraws;

	/// <summary>Changes when a view takes over or replaces a backing, or when the engine takes it back.</summary>
	internal static int ViewDraws => Volatile.Read( ref viewDraws );

	internal void CopyFrom( Texture texture )
	{
		if ( !texture.IsValid() )
			return;

		// What other devices made of the old contents is stale
		RenderBackend.ReleaseEverywhere( this );
		MadeAtRunTime = texture.MadeAtRunTime;
		InitialPixels = texture.InitialPixels;
		InitialColor = texture.InitialColor;

		// A device that let the pixels go once it made its copy has nothing to make this texture from: it copies that copy
		if ( InitialPixels is null && MadeAtRunTime ) RenderBackend.AdoptEverywhere( texture, this );

		if ( !native.IsNull )
		{
			var n = native;
			native = IntPtr.Zero;

			// Evict from NativeResourceCache so a new wrapper can be created
			// if the same native pointer is reused (e.g. RenderTarget pool, TextBlock rebuild).
			NativeResourceCache.Remove( n.GetBindingPtr().ToInt64() );
			ForgetNamed( n.GetBindingPtr().ToInt64() );

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
		madeSheet = texture.madeSheet;
		sheetRead = false;

		DirtyVersion++;
	}

	/// <summary>
	/// Refreshes cached metadata when the texture behind a strong handle changes: a baked texture written over its file and
	/// reloaded in place. Its description, file header and sheet are read again when next used, and what every device made
	/// of the old contents is released.
	/// </summary>
	internal void RefreshNativeDescription()
	{
		// One made at run time keeps what it was made with: its handle hasn't changed
		if ( !describedAtCreation )
		{
			gotdesc = false;
			_desc = default;
			fileHeader = null;
			fileHeaderRead = false;
		}

		sheetRead = false;

		RenderBackend.Default.Release( this );
		RenderBackend.ReleaseEverywhere( this );

		DirtyVersion++;
	}

	/// <summary>How many textures were described from their files, and how many had no description to read, for diagnostics.</summary>
	internal static int DescribedFromFiles, Undescribed;

	/// <summary>The first textures that had no description to read.</summary>
	internal static readonly System.Collections.Concurrent.ConcurrentQueue<string> UndescribedNames = new();

	/// <summary>
	/// Its description: what it was made with, or its compiled file's header as Source 2 describes a loaded texture. One
	/// with neither (its file isn't found, or can't be read) has an empty description, which reads as a failed load.
	/// </summary>
	internal CTextureDesc Desc
	{
		get
		{
			if ( gotdesc ) return _desc;

			if ( FileHeader is { } header && DescriptionOf( header ) is { } fromFile )
			{
				_desc = fromFile;
				System.Threading.Interlocked.Increment( ref DescribedFromFiles );
			}
			else
			{
				_desc = default;
				if ( System.Threading.Interlocked.Increment( ref Undescribed ) <= 64 ) UndescribedNames.Enqueue( ResourcePath ?? RenderTools.GetTextureResourceName( native ) );
			}

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
	public bool IsLoaded
	{
		get => Volatile.Read( ref loading )?.Task.IsCompleted ?? true;
		internal set
		{
			if ( value ) Volatile.Read( ref loading )?.TrySetResult();
			else BeginLoading();
		}
	}

	TaskCompletionSource loading;

	TaskCompletionSource BeginLoading()
	{
		TaskCompletionSource replacement = null;
		while ( true )
		{
			var current = Volatile.Read( ref loading );
			if ( current is not null && !current.Task.IsCompleted ) return current;

			replacement ??= new( TaskCreationOptions.RunContinuationsAsynchronously );
			if ( Interlocked.CompareExchange( ref loading, replacement, current ) == current ) return replacement;
		}
	}

	internal Task WhenLoaded( CancellationToken cancellationToken = default )
		=> Volatile.Read( ref loading )?.Task.WaitAsync( cancellationToken ) ?? Task.CompletedTask;

	internal void FailLoading( Exception error )
	{
		if ( Volatile.Read( ref loading ) is { } pending )
			FailLoading( pending, error );
	}

	static void FailLoading( TaskCompletionSource pending, Exception error )
	{
		if ( error is OperationCanceledException canceled ) pending.TrySetCanceled( canceled.CancellationToken );
		else pending.TrySetException( error );
	}

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
		: (int)Math.Clamp( (long)(Application.FrameCount - Math.Max( _markedUsedFrame, _deviceUsedFrame )), 0, 1000 ); // When the device last used it

	/// <summary>
	/// Gets if the texture has UAV access
	/// </summary>
	public bool UAVAccess => Desc.m_nFlags.HasFlag( RuntimeTextureSpecificationFlags.TSPEC_UAV );

	internal RenderMultisampleType MultisampleType
	{
		get
		{
			if ( !native.IsValid ) return RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
			return CreatedMultisample;
		}
	}

	/// <summary>What the texture was made with: Source 2's empty device keeps no sample count.</summary>
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
			ForgetNamed( n.GetBindingPtr().ToInt64() );

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
	public Vector4 SequenceData => RenderBackend.Default.SheetData( this );

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

	// Read on first use: what it was made with, else its file's (FileHeader)
	bool sheetRead;
	TextureSheet sheet;

	/// <summary>Its sprite sheet, or null: its file's, or the sequences it was made with (<see cref="Texture2DBuilder.AddSequence"/>).</summary>
	internal TextureSheet Sheet
	{
		get
		{
			ReadSheet();
			return sheet;
		}
	}

	/// <summary>Made at run time with sequences: they're its sheet.</summary>
	internal void MadeWithSheet( TextureSheet made )
	{
		madeSheet = made;
		sheetRead = false;
	}

	TextureSheet madeSheet;

	private void ReadSheet()
	{
		if ( sheetRead ) return;

		var read = describedAtCreation ? madeSheet : TextureSheet.From( FileHeader?.Sheet );
		var animated = false;
		SequenceInfo[] infos = null;
		if ( read is { Sequences.Length: > 0 } )
		{
			infos = new SequenceInfo[read.Sequences.Length];
			for ( int i = 0; i < infos.Length; i++ )
			{
				var sequence = read.Sequences[i];
				infos[i] = new SequenceInfo { Length = sequence.TotalTime, FrameCount = sequence.Frames.Length, Looped = !sequence.Clamp };
				animated |= infos[i].FrameCount > 1;
			}
		}

		sheet = read;
		sequences = infos;
		sequenceCount = infos?.Length ?? 0;
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

	// The frame another device than Source 2's last used it, for LastUsed; set from any thread
	ulong _deviceUsedFrame;

	/// <summary>Used by the device drawing (when it isn't Source 2's, which keeps its own count) this frame.</summary>
	internal void UsedOnDevice() => _deviceUsedFrame = Application.FrameCount;

	/// <summary>
	/// Records the current frame as the texture's last use, including when sampling it through a bindless index.
	/// </summary>
	/// <param name="requiredMipSize">Retained for source compatibility; it does not request residency.</param>
	public void MarkUsed( int requiredMipSize = 0 ) => _markedUsedFrame = Application.FrameCount;

	internal bool IsRenderTarget => Desc.m_nFlags.Contains( RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET );

	/// <summary>
	/// Mark this texture as loading, create a texture in a task, replace this texture with it, mark it as loaded.
	/// This is for situations where you create a placeholder texture then replace it with the real texture later.
	/// </summary>
	internal async Task ReplacementAsync( Task<Texture> task )
	{
		var pending = BeginLoading();
		try
		{
			var texture = await task;
			if ( texture is not null && texture != this )
			{
				this.CopyFrom( texture );

				// Retarget animated images to the public placeholder.
				if ( Animations.FirstOrDefault( x => x.Texture.TryGetTarget( out var t ) && ReferenceEquals( t, texture ) ) is { } animation )
					animation.Texture.SetTarget( this );

				texture.Dispose();
			}

			pending.TrySetResult();
		}
		catch ( Exception error )
		{
			FailLoading( pending, error );
			throw;
		}
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
