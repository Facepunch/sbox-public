namespace Sandbox.Rendering;

using NativeEngine;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// This object renders every sprite registered to it in a single draw call. It takes care of sorting, sampling, and the whole pipeline regarding sprites.
/// The SceneSpriteSystem is responsible for pushing sprites into this object depending on its properties.
/// </summary>
internal sealed class SpriteBatchSceneObject : SceneCustomObject
{
	internal readonly record struct SpriteGroup( SpriteData[] SharedSprites, int Offset, int Count, BBox Bounds );
	public bool Sorted { get; set; } = false;
	public bool Filtered { get; set; } = false;
	public bool Additive { get; set; } = false;
	public bool Opaque { get; set; } = false;

	internal Dictionary<Guid, SpriteRenderer> Components = new();

	private static ComputeShader SpriteComputeShader = new( "sprite/sprite_cs" );
	private static ComputeShader SortComputeShader = new( "sort_cs" );
	private static readonly uint[] ZeroUint = [0];
	private readonly GpuBuffer<uint> SpriteAtomicCounter;
	private readonly CommandList _commandList = new( "SpriteBatch" );

	private static Material SpriteMaterial = Material.FromShader( "sprite/sprite_ps.shader" );

	/// <summary>
	/// Whether any sprite in the batch is lit, as of its last upload.
	/// </summary>
	internal bool HasLitSprites { get; private set; }

	internal Dictionary<Guid, SpriteGroup> SpriteGroups = [];

	internal readonly SamplerState sampler = new()
	{
		AddressModeU = TextureAddressMode.Clamp,
		AddressModeV = TextureAddressMode.Clamp,
	};

	/// <summary>
	/// Flags for sprite rendering, must match SpriteFlags in sprite_ps.shader
	/// </summary>
	[Flags]
	public enum SpriteFlags
	{
		None = 0x0,
		CastShadows = 0x1,
		FlipX = 0x2,
		FlipY = 0x4,
		SnapToFrame = 0x8,
		Text = 0x10,
		VelocityStretch = 0x40
	}

	// GPU Resident representation of a sprite
	[StructLayout( LayoutKind.Sequential, Pack = 16 )]
	public struct SpriteData
	{
		public Vector3 Position;
		public Vector3 Rotation;
		public Vector2 Scale;
		public uint TintColor;      // Packed RGBA8
		public uint OverlayColor;   // Packed RGBA8
		public int TextureHandle;   // a text sprite carries its block placement in TextureHandle, SamplerIndex, Sequence and SequenceTime, see IBatchedParticleSpriteRenderer
		public int RenderFlags;
		public uint BillboardMode;
		public uint FogStrengthCutout;  // Lower 16 bits: fog, upper 16 bits: alpha cutout
		public uint Lighting;
		public float DepthFeather;
		public int SamplerIndex;
		public int Splots = 0;
		public int Sequence = 0;
		public float SequenceTime = 0;
		public float RotationOffset = -1.0f;
		public Vector4 MotionBlur;
		public Vector3 Velocity = Vector3.Zero;
		public Vector4 BlendSheetUV;
		public Vector2 Offset;
		public uint CameraFade;     // Two halves: lower 16 bits near distance, upper 16 bits far distance

		/// <summary>
		/// Bindless linear normal texture, or zero for ordinary sprites in the same batch.
		/// </summary>
		public int NormalTextureHandle;

		/// <summary>
		/// Maximum distortion in render pixels before normal-map scaling, independent of opacity.
		/// </summary>
		public float RefractionStrength;
		public SpriteData()
		{

		}

		// Helper method to pack Color to RGBA8
		internal static uint PackColor( Color color )
		{
			byte r = (byte)(color.r * 255f);
			byte g = (byte)(color.g * 255f);
			byte b = (byte)(color.b * 255f);
			byte a = (byte)(color.a * 255f);
			return (uint)(r | (g << 8) | (b << 16) | (a << 24));
		}

		// Pack fog strength and alpha cutout into a single uint
		internal static uint PackFogAndAlphaCutout( float fogStrength, float alphaCutout )
		{
			ushort fogPacked = (ushort)(fogStrength.Clamp( 0f, 1f ) * 65535f);
			ushort alphaPacked = (ushort)(alphaCutout.Clamp( 0f, 1f ) * 65535f);
			return (uint)(fogPacked | (alphaPacked << 16));
		}

		// Pack the two camera fade distances (world units) into a single uint as halves
		internal static uint PackCameraFade( float near, float far )
		{
			uint nearPacked = BitConverter.HalfToUInt16Bits( (Half)MathF.Max( near, 0f ) );
			uint farPacked = BitConverter.HalfToUInt16Bits( (Half)MathF.Max( far, 0f ) );
			return nearPacked | (farPacked << 16);
		}
	}
	struct SpriteVertex
	{
		public Vector4 position;
		public Vector4 normal;
		public Vector2 uv;

		public SpriteVertex( Vector3 pos, Vector3 norm, Vector2 inUv )
		{
			position = new Vector4( pos, 1.0f );
			normal = new Vector4( norm, 0.0f );
			uv = inUv;
		}
	}

	const int DefaultBufferSize = 16;
	int CurrentBufferSize = DefaultBufferSize;

	int _splotCount = 0;
	int SplotCount
	{
		get
		{
			if ( _splotCount != 0 )
			{
				return _splotCount;
			}

			// Use precomputed splot counts to avoid iteration
			int splotCount = 0;
			foreach ( var group in SpriteGroups )
			{
				if ( _precomputedSplotCounts.TryGetValue( group.Key, out int precomputedCount ) )
				{
					splotCount += precomputedCount;
				}
				else
				{
					// Fallback
					var spriteGroup = group.Value;

					for ( int i = 0; i < spriteGroup.Count; i++ )
					{
						int index = spriteGroup.Offset + i;
						int leading = spriteGroup.SharedSprites[index].MotionBlur.x > 1 ? 2 : 1;
						splotCount += spriteGroup.SharedSprites[index].Splots * leading;
					}
				}
			}

			_splotCount = splotCount;

			return _splotCount;
		}
	}

	int SpriteCount
	{
		get
		{
			int sum = Components.Count;
			foreach ( var group in SpriteGroups )
			{
				sum += group.Value.Count;
			}

			return sum;
		}
	}

	bool GPUUploadQueued = false;

	GpuBuffer<SpriteData> SpriteBuffer;
	GpuBuffer<SpriteData> SpriteBufferOut;

	GpuBuffer<SpriteVertex> VertexBuffer;
	GpuBuffer<int> IndexBuffer;
	GpuBuffer<uint> GPUSortingBuffer;
	GpuBuffer<float> GPUDistanceBuffer;

	SpriteData[] SpriteDataBuffer = null!;
	bool SpriteDataBufferRented = false;


	bool _pendingHasText;
	bool _pendingFacesVelocity;

	/// <summary>
	/// What a frame's draw of the batch reads (<see cref="RenderSceneObject"/>), written by <see cref="UploadOnHost"/>, with the
	/// buffers the frame's command list was recorded with: a frame drawing on the render thread while the main thread uploads
	/// the next one reads its own (<see cref="ForDrawing"/>, <see cref="ForWriting"/>), as its command list does.
	/// </summary>
	sealed class DrawState
	{
		// Set after building the staging buffer; an early-out guard
		public int SpriteCount;
		public int InstanceCount;
		public bool HasText;
		public ulong Frame; // the text buffers the sprites' placements point into
		public bool IsSorted, Filtered, Additive, Opaque, Refraction;

		// Whether the compute's results depend on the view drawing them, and whether it would leave the sprites as uploaded
		public bool PerView, Unprocessed;
		public ulong ExecutedFrame;

		public GpuBuffer<SpriteData> Sprites, SpritesOut;
		public GpuBuffer<uint> Sorting;

		public void CopyFrom( DrawState other )
		{
			(SpriteCount, InstanceCount, HasText, Frame) = (other.SpriteCount, other.InstanceCount, other.HasText, other.Frame);
			(IsSorted, Filtered, Additive, Opaque, Refraction) = (other.IsSorted, other.Filtered, other.Additive, other.Opaque, other.Refraction);
			(PerView, Unprocessed, ExecutedFrame) = (other.PerView, other.Unprocessed, other.ExecutedFrame);
			(Sprites, SpritesOut, Sorting) = (other.Sprites, other.SpritesOut, other.Sorting);
		}
	}

	DrawState _draw = new(), _drawHeld;
	long _drawSplitEpoch = -1, _drawSplitPublished = -1;

	/// <summary>
	/// The draw state the main thread writes. While a frame handed over may be drawing on the render thread
	/// (<see cref="RenderSync.Overlapping"/>), the first write since that hand-over keeps the state for that frame and goes on in
	/// a copy of it, as <see cref="CommandList"/> keeps its recording.
	/// </summary>
	DrawState ForWriting()
	{
		if ( !RenderSync.Overlapping || RenderSync.OnRenderThread ) return _draw;
		var epoch = RenderSync.Epoch;
		if ( _drawSplitEpoch == epoch ) return _draw;
		_drawSplitEpoch = epoch;

		// The other state isn't read in this frame. Written in this order for ForDrawing, which reads them in the other order
		var held = _draw;
		var fresh = _drawHeld ?? new DrawState();
		fresh.CopyFrom( held );
		Volatile.Write( ref _drawHeld, held );
		Volatile.Write( ref _drawSplitPublished, epoch );
		Volatile.Write( ref _draw, fresh );
		return fresh;
	}

	/// <summary>The draw state a draw on this thread reads: on the render thread, its frame's as it was handed over.</summary>
	DrawState ForDrawing()
	{
		var current = Volatile.Read( ref _draw );
		if ( !RenderSync.OnRenderThread ) return current;
		return Volatile.Read( ref _drawSplitPublished ) == RenderSync.Epoch ? Volatile.Read( ref _drawHeld ) : current;
	}

	// Buffers and staging arrays a frame handed over may still draw with, kept until the next hand-over (that frame has been
	// joined by then), then disposed or given back (DisposeRetired)
	static readonly List<(long Epoch, GpuBuffer Buffer, SpriteData[] Staging)> _retired = new();

	/// <summary>Dispose <paramref name="buffer"/>, or give <paramref name="staging"/> back, once no frame handed over can use it.</summary>
	static void Retire( GpuBuffer buffer, SpriteData[] staging = null )
	{
		if ( buffer is null && staging is null ) return;
		if ( !RenderSync.Overlapping )
		{
			buffer?.Dispose();
			if ( staging is not null ) ArrayPool<SpriteData>.Shared.Return( staging, clearArray: false );
			return;
		}

		_retired.Add( (RenderSync.Epoch, buffer, staging) );
	}

	/// <summary>On the main thread, before the batches upload: dispose what <see cref="Retire"/> kept once no frame can use it.</summary>
	internal static void DisposeRetired()
	{
		if ( _retired.Count == 0 ) return;
		var epoch = RenderSync.Epoch;
		var overlapping = RenderSync.Overlapping;
		int kept = 0;
		for ( int i = 0; i < _retired.Count; i++ )
		{
			var retired = _retired[i];
			if ( overlapping && retired.Epoch >= epoch )
			{
				_retired[kept++] = retired;
				continue;
			}

			retired.Buffer?.Dispose();
			if ( retired.Staging is not null ) ArrayPool<SpriteData>.Shared.Return( retired.Staging, clearArray: false );
		}

		_retired.RemoveRange( kept, _retired.Count - kept );
	}

	/// <summary>For the tests: buffers and staging arrays waiting for <see cref="DisposeRetired"/>.</summary>
	internal static int RetiredCount => _retired.Count;

	/// <summary>For the tests: the sprite buffer a draw on this thread reads, and the sprites it draws.</summary>
	internal (GpuBuffer Sprites, int SpriteCount) DrawnForTests => (ForDrawing().Sprites, ForDrawing().SpriteCount);

	public SpriteBatchSceneObject( Scene scene ) : base( scene.SceneWorld )
	{
		InitializeSpriteMesh();

		// GPU buffers
		SpriteBuffer = new( CurrentBufferSize );
		SpriteBufferOut = new( CurrentBufferSize );
		GPUSortingBuffer = new( CurrentBufferSize );
		GPUDistanceBuffer = new( CurrentBufferSize );
		SpriteAtomicCounter = new( 1 );
		(_draw.Sprites, _draw.SpritesOut, _draw.Sorting) = (SpriteBuffer, SpriteBufferOut, GPUSortingBuffer);
	}

	/// <summary>
	/// Create the initialize sprite mesh that will be instanced
	/// </summary>
	private void InitializeSpriteMesh()
	{
		// Vertex pulling buffer
		const float spriteSize = 10f;
		SpriteVertex[] vertices =
		[
			new ( new ( -spriteSize, -spriteSize, 0 ), Vector3.Forward, new ( 0, 0 ) ),
			new ( new (  spriteSize, -spriteSize, 0 ), Vector3.Forward, new ( 1, 0 ) ),
			new ( new (  spriteSize,  spriteSize, 0 ), Vector3.Forward, new ( 1, 1 ) ),
			new ( new ( -spriteSize,  spriteSize, 0 ), Vector3.Forward, new ( 0, 1 ) ),
		];
		VertexBuffer = new( 4 );
		VertexBuffer.SetData( vertices.AsSpan() );

		// Index buffer
		int[] indices = { 0, 1, 2, 0, 2, 3 };
		IndexBuffer = new( 6, GpuBuffer.UsageFlags.Index );
		IndexBuffer.SetData( indices );
	}

	~SpriteBatchSceneObject()
	{
		if ( SpriteDataBufferRented )
		{
			ArrayPool<SpriteData>.Shared.Return( SpriteDataBuffer, clearArray: false );
		}
	}


	internal void Destroy()
	{
		Delete(); // native removal waits for frame end, so also empty what RenderSceneObject replays
		ForWriting().SpriteCount = 0;
		_commandList.Reset();

		// A frame handed over may still draw the batch
		Retire( SpriteAtomicCounter );
		Retire( VertexBuffer );
		Retire( IndexBuffer );
		Retire( SpriteBuffer );
		Retire( SpriteBufferOut );
		Retire( GPUSortingBuffer );
		Retire( GPUDistanceBuffer );
		if ( SpriteDataBufferRented )
		{
			Retire( null, SpriteDataBuffer );
			SpriteDataBuffer = null;
			SpriteDataBufferRented = false;
		}
	}

	/// <summary>
	/// Resizes GPU buffers to the nearest power of 2
	/// </summary>
	public void ResizeBuffers()
	{
		int allocationSize = SplotCount + SpriteCount;
		if ( allocationSize <= CurrentBufferSize )
		{
			return;
		}

		ResizeBuffers( allocationSize );
	}

	/// <summary>
	/// Resizes GPU buffers to accommodate the specified allocation size
	/// </summary>
	private void ResizeBuffers( int allocationSize )
	{
		CurrentBufferSize = (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)allocationSize );

		// A frame handed over may still draw with the old ones
		Retire( SpriteBuffer );
		Retire( SpriteBufferOut );
		Retire( GPUSortingBuffer );
		Retire( GPUDistanceBuffer );

		SpriteBuffer = new( CurrentBufferSize );
		SpriteBufferOut = new( CurrentBufferSize );
		GPUSortingBuffer = new( CurrentBufferSize );
		GPUDistanceBuffer = new( CurrentBufferSize );
	}

	private readonly Dictionary<Guid, int> _precomputedSplotCounts = [];

	// Pre-allocated buffer to avoid GC allocations in hot path
	private SpriteRenderer[] _componentBuffer = new SpriteRenderer[16];
	private (Vector3 mins, Vector3 maxs)[] _componentBounds = new (Vector3, Vector3)[16];

	/// <summary>
	/// Write component sprite <paramref name="i"/> into the staging buffer, and its bounds into <see cref="_componentBounds"/>.
	/// </summary>
	void WriteComponentSprite( int i )
	{
		var c = _componentBuffer[i];
		var transform = c.WorldTransform;
		var spriteSize = c.Size;
		var rotation = c.WorldRotation.Angles().AsVector3();

		if ( c.Billboard == SpriteRenderer.BillboardMode.Always || c.Billboard == SpriteRenderer.BillboardMode.YOnly )
		{
			// We only care about roll in this case
			rotation.x = 0;
			rotation.y = 0;
		}

		spriteSize = spriteSize.Abs();

		// Adjust for aspect ratio
		var aspectRatio = (c.Texture?.Width ?? 1) / (float)(c.Texture?.Height ?? 1);
		var size = spriteSize / 2f;
		var pos = transform.Position;
		var scale = new Vector3( transform.Scale.x * size.x, transform.Scale.y, transform.Scale.z * size.y );
		if ( aspectRatio < 1f )
			scale *= new Vector3( aspectRatio, 1f, 1f );
		else
			scale *= new Vector3( 1f, 1f, 1f / aspectRatio );

		pos = pos.RotateAround( transform.Position, transform.Rotation );
		transform = transform.WithScale( scale ).WithPosition( pos );

		var renderFlags = SpriteFlags.None;
		if ( c.FlipHorizontal ) renderFlags |= SpriteFlags.FlipX;
		if ( c.FlipVertical ) renderFlags |= SpriteFlags.FlipY;

		var rgbe = c.Color.ToRgbe();
		var alpha = (byte)(c.Color.a.Clamp( 0.0f, 1.0f ) * 255.0f);
		var tintColor = new Color32( rgbe.r, rgbe.g, rgbe.b, alpha );

		var overlayRgbe = c.OverlayColor.ToRgbe();
		var overlayAlpha = (byte)(c.OverlayColor.a.Clamp( 0.0f, 1.0f ) * 255.0f);
		var overlayColor = new Color32( overlayRgbe.r, overlayRgbe.g, overlayRgbe.b, overlayAlpha );

		int lightingFlag = c.Lighting ? 1 : 0;
		uint packedExponent = (uint)(((byte)lightingFlag) | rgbe.a << 16);

		uint packedFogAndAlpha = SpriteData.PackFogAndAlphaCutout( c.FogStrength, c.Opaque ? c.AlphaCutoff : 0.001f );

		var spritePos = transform.Position;
		var spriteScale = new Vector2( transform.Scale.x, transform.Scale.z );

		SpriteDataBuffer[i] = new SpriteData
		{
			Position = spritePos,
			Rotation = new( rotation.x, rotation.y, rotation.z ),
			Scale = spriteScale,
			TextureHandle = c.Texture is null ? Texture.Invalid.Index : c.Texture.Index,
			TintColor = tintColor.RawInt,
			OverlayColor = overlayColor.RawInt,
			RenderFlags = (int)renderFlags,
			BillboardMode = (uint)c.Billboard,
			FogStrengthCutout = packedFogAndAlpha,
			Lighting = packedExponent,
			DepthFeather = c.DepthFeather,
			CameraFade = SpriteData.PackCameraFade( c.CameraFadeNear, c.CameraFadeFar ),
			NormalTextureHandle = c.IsRefractive ? c.NormalTexture.Index : 0,
			RefractionStrength = MathF.Max( c.RefractionStrength, 0.0f ),
			SamplerIndex = SamplerState.GetBindlessIndex( sampler with { Filter = c.TextureFilter } ),
			Offset = c.Pivot
		};

		var pivot = c.Pivot;
		if ( c.Billboard == SpriteRenderer.BillboardMode.None )
		{
			var right = transform.Rotation.Right;
			var up = transform.Rotation.Up;
			var center = spritePos
				+ right * ((pivot.x - 0.5f) * 2.0f * spriteScale.x)
				+ up * ((pivot.y - 0.5f) * 2.0f * spriteScale.y);
			var extent = right.Abs() * spriteScale.x
				+ up.Abs() * spriteScale.y
				+ transform.Rotation.Forward.Abs() * 0.01f;
			_componentBounds[i] = (center - extent, center + extent);
			return;
		}

		float halfSize = MathF.Max(
			MathF.Max( pivot.x, 1f - pivot.x ) * 2f * spriteScale.x,
			MathF.Max( pivot.y, 1f - pivot.y ) * 2f * spriteScale.y
		);
		var expand = new Vector3( halfSize, halfSize, halfSize );
		_componentBounds[i] = (spritePos - expand, spritePos + expand);
	}

	public void RegisterSprite( Guid ownerId, SpriteData[] sharedSprites, int offset, int count, int splotCount, BBox bounds )
	{
		SpriteGroups[ownerId] = new( sharedSprites, offset, count, bounds );
		_precomputedSplotCounts[ownerId] = splotCount;
		OnChanged();
	}

	public void RegisterSprite( Guid id, SpriteRenderer component )
	{
		Components[id] = component;
		OnChanged();
	}

	public void UnregisterSprite( Guid id )
	{
		Components.Remove( id );
		OnChanged();
	}

	public void UnregisterSpriteGroup( Guid ownerId )
	{
		if ( SpriteGroups.Remove( ownerId ) )
		{
			// Clean up precomputed splot count
			_precomputedSplotCounts.Remove( ownerId );

			OnChanged();
		}
	}

	public void UpdateSprite( Guid id, SpriteRenderer component )
	{
		if ( Components.ContainsKey( id ) )
		{
			Components[id] = component;
			OnChanged();
		}
	}

	public bool ContainsSprite( Guid id )
	{
		return Components.ContainsKey( id );
	}

	public void OnChanged()
	{
		// Clear cached splot count to force recalculation
		_splotCount = 0;

		GPUUploadQueued = true;
	}

	/// <summary>
	/// Copy host buffers onto GPU
	/// </summary>
	public void UploadOnHost()
	{
		if ( !GPUUploadQueued && !Sorted )
		{
			return;
		}

		int spriteCount = SpriteCount;
		int splotCount = SplotCount;
		int componentCount = Components.Count;

		bool hasRefraction = false;
		bool hasLighting = false;

		// Resize GPU buffers here on the main thread, not in OnChanged,
		// because RenderSceneObject may be using them concurrently on a render thread.
		int requiredSize = splotCount + spriteCount;
		if ( requiredSize > CurrentBufferSize )
		{
			ResizeBuffers( requiredSize );
		}

		// Staging buffer only needs to hold component sprites — particle groups
		// are uploaded directly from SharedSprites in RenderSceneObject.
		if ( componentCount > 0 && (SpriteDataBuffer == null || SpriteDataBuffer.Length < componentCount) )
		{
			// The last frame's command list uploads from it, and may not have yet
			if ( SpriteDataBufferRented )
			{
				Retire( null, SpriteDataBuffer );
			}

			SpriteDataBuffer = ArrayPool<SpriteData>.Shared.Rent( componentCount );
			SpriteDataBufferRented = true;
		}

		var boundsMin = new Vector3( float.MaxValue, float.MaxValue, float.MaxValue );
		var boundsMax = new Vector3( float.MinValue, float.MinValue, float.MinValue );

		// Upload sprites
		if ( componentCount > 0 )
		{
			// Use pre-allocated buffer to avoid GC allocation
			if ( _componentBuffer.Length < componentCount )
			{
				_componentBuffer = new SpriteRenderer[componentCount * 2];
			}

			int index = 0;
			foreach ( var component in Components.Values )
			{
				_componentBuffer[index++] = component;
				hasRefraction |= component.IsRefractive;
				hasLighting |= component.Lighting;
			}

			if ( _componentBounds.Length < componentCount )
			{
				_componentBounds = new (Vector3, Vector3)[componentCount * 2];
			}

			JobDispatcher.For( componentCount, this, static ( batch, i ) => batch.WriteComponentSprite( i ) );

			// Combine in a fixed order, so the bounds are the same whichever threads wrote them.
			for ( int i = 0; i < componentCount; i++ )
			{
				boundsMin = Vector3.Min( boundsMin, _componentBounds[i].mins );
				boundsMax = Vector3.Max( boundsMax, _componentBounds[i].maxs );
			}

		}

		_pendingHasText = false;
		_pendingFacesVelocity = false;

		foreach ( var spriteGroup in SpriteGroups.Values )
		{
			boundsMin = Vector3.Min( boundsMin, spriteGroup.Bounds.Mins );
			boundsMax = Vector3.Max( boundsMax, spriteGroup.Bounds.Maxs );

			// A group is one particle renderer, so its first sprite describes text and refraction for the whole group.
			_pendingHasText |= spriteGroup.Count > 0 && (spriteGroup.SharedSprites[spriteGroup.Offset].RenderFlags & (int)SpriteFlags.Text) != 0;
			hasRefraction |= spriteGroup.Count > 0 && spriteGroup.SharedSprites[spriteGroup.Offset].NormalTextureHandle != 0;
			hasLighting |= spriteGroup.Count > 0 && (spriteGroup.SharedSprites[spriteGroup.Offset].Lighting & 0xFF) != 0;
			_pendingFacesVelocity |= spriteGroup.Count > 0 && spriteGroup.SharedSprites[spriteGroup.Offset].RotationOffset > -1;
		}

		HasLitSprites = hasLighting;

		// Mixed batches keep a single sort order. Only request the shared copy and stencil while a sprite needs them.
		Flags.WantsFrameBufferCopy = hasRefraction;

		// Use a degenerate zero-size bounds for empty batches so they can be frustum-culled.
		Bounds = spriteCount > 0 ? new BBox( boundsMin, boundsMax ) : default;

		ForWriting().SpriteCount = spriteCount;
		GPUUploadQueued = false;

		BuildCommandList( spriteCount, splotCount, componentCount );
	}

	private const int GroupSize = 256;
	private const int MaxDimGroups = 1024;
	private const int MaxDimThreads = GroupSize * MaxDimGroups;

	/// <summary>
	/// Build the command list that will be replayed on the render thread.
	/// </summary>
	private void BuildCommandList( int spriteCount, int splotCount, int componentCount )
	{
		_commandList.Reset();

		if ( spriteCount == 0 )
			return;

		// Upload sprite data to GPU (deferred to render thread, zero-copy)
		if ( componentCount > 0 )
		{
			_commandList.SetBufferData( SpriteBuffer, SpriteDataBuffer, 0, componentCount );
		}

		int currentOffset = componentCount;
		foreach ( var group in SpriteGroups.Values )
		{
			_commandList.SetBufferData( SpriteBuffer, group.SharedSprites, group.Offset, group.Count, currentOffset );
			currentOffset += group.Count;
		}

		bool sorted = Sorted;
		bool filtered = Filtered;
		bool additive = Additive;
		bool opaque = Opaque;
		int bufferSize = CurrentBufferSize;
		int totalInstances = spriteCount + splotCount;
		bool didSort = sorted && totalInstances >= 2;

		var draw = ForWriting();
		draw.InstanceCount = totalInstances;
		draw.IsSorted = didSort;
		draw.Filtered = filtered;
		draw.Additive = additive;
		draw.Opaque = opaque;
		draw.Refraction = Flags.WantsFrameBufferCopy;
		draw.HasText = _pendingHasText;
		draw.Frame = Application.FrameCount;
		(draw.Sprites, draw.SpritesOut, draw.Sorting) = (SpriteBuffer, SpriteBufferOut, GPUSortingBuffer);

		// The compute sorts by distance from the camera and turns sprites that face their velocity in the view. Without
		// either its results are the same for every layer and view of a frame, and without motion blur's splots too it would
		// copy the sprites as they are: they're drawn from where they're uploaded
		draw.PerView = didSort || _pendingFacesVelocity;
		draw.Unprocessed = !draw.PerView && splotCount == 0;
		draw.ExecutedFrame = 0;

		if ( draw.Unprocessed )
			return;

		_commandList.SetBufferData( SpriteAtomicCounter, ZeroUint );

		if ( sorted && totalInstances >= 2 )
		{
			_commandList.Attributes.Set( "SortBuffer", (GpuBuffer)GPUSortingBuffer );
			_commandList.Attributes.Set( "DistanceBuffer", (GpuBuffer)GPUDistanceBuffer );
			_commandList.Attributes.Set( "Count", bufferSize );
			_commandList.Attributes.SetCombo( "D_CLEAR", 1 );
			_commandList.DispatchCompute( SortComputeShader, bufferSize, 1, 1 );

			_commandList.ResourceBarrierTransition( (GpuBuffer)GPUSortingBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
			_commandList.ResourceBarrierTransition( (GpuBuffer)GPUDistanceBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
		}

		_commandList.ResourceBarrierTransition( SpriteAtomicCounter, ResourceState.Common );
		_commandList.ResourceBarrierTransition( (GpuBuffer)SpriteBuffer, ResourceState.Common );
		_commandList.ResourceBarrierTransition( (GpuBuffer)SpriteBufferOut, ResourceState.Common );
		_commandList.ResourceBarrierTransition( (GpuBuffer)GPUDistanceBuffer, ResourceState.Common );

		_commandList.Attributes.Set( "Sprites", (GpuBuffer)SpriteBuffer );
		_commandList.Attributes.Set( "SpriteBufferOut", (GpuBuffer)SpriteBufferOut );
		_commandList.Attributes.Set( "SpriteCount", spriteCount );
		_commandList.Attributes.Set( "AtomicCounter", SpriteAtomicCounter );
		_commandList.Attributes.Set( "DistanceBuffer", (GpuBuffer)GPUDistanceBuffer );
		_commandList.DispatchCompute( SpriteComputeShader, spriteCount, 1, 1 );

		_commandList.ResourceBarrierTransition( SpriteAtomicCounter, ResourceState.Common );
		_commandList.ResourceBarrierTransition( (GpuBuffer)SpriteBufferOut, ResourceState.Common );

		if ( sorted && totalInstances >= 2 )
		{
			_commandList.ResourceBarrierTransition( (GpuBuffer)GPUDistanceBuffer, ResourceState.Common );
			_commandList.Attributes.SetCombo( "D_CLEAR", 0 );

			var x = Math.Min( bufferSize, MaxDimThreads );
			var y = (bufferSize + MaxDimThreads - 1) / MaxDimThreads;

			for ( var dim = 2; dim <= bufferSize; dim <<= 1 )
			{
				_commandList.Attributes.Set( "Dim", dim );

				for ( var block = dim >> 1; block > 0; block >>= 1 )
				{
					_commandList.Attributes.Set( "Block", block );
					_commandList.DispatchCompute( SortComputeShader, x, y, 1 );

					_commandList.ResourceBarrierTransition( (GpuBuffer)GPUSortingBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
					_commandList.ResourceBarrierTransition( (GpuBuffer)GPUDistanceBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
				}
			}
		}
	}

	public override void RenderSceneObject()
	{
		base.RenderSceneObject();

		var draw = ForDrawing();
		if ( draw.SpriteCount == 0 || !_commandList.Enabled )
			return;

		Graphics.Attributes.Set( "CameraPosition", Graphics.CameraPosition );

		// A batch draws in several of a view's layers (translucent, bloom, a viewmodel's overlay): one upload and compute serves
		// them all, unless the results are the view's
		if ( draw.PerView || draw.ExecutedFrame != Application.FrameCount )
		{
			draw.ExecutedFrame = Application.FrameCount;
			_commandList.ExecuteOnRenderThread();
		}

		var attributes = RenderAttributes.Pool.Get();
		try
		{
			attributes.SetCombo( "D_BLEND", draw.Additive ? 1 : 0 );
			attributes.SetCombo( "D_OPAQUE", draw.Opaque ? 1 : 0 );
			attributes.SetCombo( "D_TEXT", draw.HasText ? 1 : 0 );
			// The scene binds FrameBufferCopyTexture for readers in translucent layers.
			bool refractive = draw.Refraction && Graphics.LayerType == SceneLayerType.Translucent;
			attributes.SetCombo( "D_REFRACTION", refractive ? 1 : 0 );
			attributes.Set( "IsSorted", draw.IsSorted ? 1 : 0 );
			attributes.Set( "SpriteCount", draw.InstanceCount );
			attributes.Set( "Filtered", draw.Filtered );
			attributes.Set( "Sprites", (GpuBuffer)(draw.Unprocessed ? draw.Sprites : draw.SpritesOut) );
			attributes.Set( "SortLUT", (GpuBuffer)draw.Sorting );
			attributes.Set( "Vertices", (GpuBuffer)VertexBuffer );
			if ( draw.HasText ) GpuFontText.Bind( attributes, draw.Frame );

			Graphics.DrawIndexedInstanced( (GpuBuffer)IndexBuffer, SpriteMaterial, draw.InstanceCount, attributes );
		}
		finally
		{
			RenderAttributes.Pool.Return( attributes );
		}
	}
}
