namespace Sandbox.Rendering;

using NativeEngine;
using System.Buffers;
using System.Runtime.InteropServices;

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

	internal Dictionary<Guid, SpriteGroup> SpriteGroups = [];

	internal readonly SamplerState sampler = new()
	{
		AddressModeU = TextureAddressMode.Clamp,
		AddressModeV = TextureAddressMode.Clamp,
	};

	/// <summary>
	/// A sprite's place in the draw order, compared lexicographically on the GPU. Splitting this
	/// across two integers keeps the comparison exact - a single float could not hold the full
	/// layer/order range alongside a distance without silently losing precision.
	/// </summary>
	[StructLayout( LayoutKind.Sequential, Pack = 4 )]
	internal struct SpriteSortKey
	{
		/// <summary>Sort layer and order in layer, inverted on the GPU so higher draws in front.</summary>
		public uint Coarse;

		/// <summary>Distance along the transparency sort axis, made unsigned-comparable.</summary>
		public uint Fine;
	}

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
		SnapToFrame = 0x8
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
		public int TextureHandle;
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

		/// <summary>
		/// Coarse draw order, packed by <see cref="PackSortKey"/>. Compared before the sort axis
		/// distance, so it wins over any positional ordering.
		/// </summary>
		public uint SortKey = 0;

		/// <summary>
		/// Moves the point this sprite sorts at, relative to <see cref="Position"/>. Set to a
		/// sorting group's origin so every member of the group resolves to one depth and nothing
		/// outside it can be drawn in between them.
		///
		/// Deliberately an offset rather than an absolute position: zero means "sort where I am",
		/// so every sprite built without knowing about sorting groups - particles especially -
		/// keeps sorting exactly as it did.
		/// </summary>
		public Vector3 SortOriginOffset = Vector3.Zero;

		/// <summary>
		/// Order within the sorting group, densely ranked from 0. Occupies the low bits of the
		/// sort key's distance term, so it only ever decides between sprites at the same depth.
		/// </summary>
		public uint SortRank = 0;

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

		/// <summary>Bits of the sort key given over to the layer index. 16k layers is far past useful.</summary>
		internal const int SortKeyLayerBits = 14;

		/// <summary>
		/// Packs a sprite's layer, blend state and order within the layer into a single value that
		/// orders correctly under plain unsigned comparison.
		///
		/// Layer is the most significant term, so it beats everything. Blend comes next, above
		/// order: sprites needing different blend states cannot share a draw call, so grouping
		/// them is what lets a layer be drawn as a small run of draws instead of being broken up.
		/// The cost is that order-in-layer only ranks a sprite against others of the same blend
		/// state - which is the same trade the renderer is already forced into.
		/// </summary>
		internal static uint PackSortKey( int layerIndex, SpriteBlendMode blend, int sortOrder )
		{
			var layer = (uint)Math.Clamp( layerIndex, 0, (1 << SortKeyLayerBits) - 1 );

			// Bias the signed order so that negative orders still compare below positive ones.
			var order = (uint)(Math.Clamp( sortOrder, short.MinValue, short.MaxValue ) - short.MinValue);

			return (layer << 18) | ((uint)blend << 16) | order;
		}

		// Pack fog strength and alpha cutout into a single uint
		internal static uint PackFogAndAlphaCutout( float fogStrength, float alphaCutout )
		{
			ushort fogPacked = (ushort)(fogStrength.Clamp( 0f, 1f ) * 65535f);
			ushort alphaPacked = (ushort)(alphaCutout.Clamp( 0f, 1f ) * 65535f);
			return (uint)(fogPacked | (alphaPacked << 16));
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

	// Set by UploadOnHost after building the staging buffer,
	// consumed by RenderSceneObject for early-out guard.
	int _pendingSpriteCount = 0;

	GpuBuffer<SpriteData> SpriteBuffer;
	GpuBuffer<SpriteData> SpriteBufferOut;

	GpuBuffer<SpriteVertex> VertexBuffer;
	GpuBuffer<int> IndexBuffer;
	GpuBuffer<uint> GPUSortingBuffer;
	GpuBuffer<SpriteSortKey> GPUSortKeyBuffer;

	SpriteData[] SpriteDataBuffer = null!;
	bool SpriteDataBufferRented = false;


	int _renderInstanceCount;
	bool _renderIsSorted;
	bool _renderFiltered;
	bool _renderAdditive;
	bool _renderOpaque;

	public SpriteBatchSceneObject( Scene scene ) : base( scene.SceneWorld )
	{
		InitializeSpriteMesh();

		// GPU buffers
		SpriteBuffer = new( CurrentBufferSize );
		SpriteBufferOut = new( CurrentBufferSize );
		GPUSortingBuffer = new( CurrentBufferSize );
		GPUSortKeyBuffer = new( CurrentBufferSize );
		SpriteAtomicCounter = new( 1 );
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

		SpriteBuffer?.Dispose();
		SpriteBufferOut?.Dispose();
		GPUSortingBuffer?.Dispose();
		GPUSortKeyBuffer?.Dispose();

		SpriteBuffer = new( CurrentBufferSize );
		SpriteBufferOut = new( CurrentBufferSize );
		GPUSortingBuffer = new( CurrentBufferSize );
		GPUSortKeyBuffer = new( CurrentBufferSize );
	}

	private readonly Dictionary<Guid, int> _precomputedSplotCounts = [];

	// Pre-allocated buffer to avoid GC allocations in hot path
	private SpriteRenderer[] _componentBuffer = new SpriteRenderer[16];
	private SortingGroup[] _spriteGroupBuffer = new SortingGroup[16];
	private int[] _bucketLayers = new int[16];
	private SpriteBlendMode[] _bucketBlends = new SpriteBlendMode[16];
	private int[] _bucketCounts = [];
	private readonly HashSet<SortingGroup> _rankedGroups = new();

	/// <summary>
	/// The runs being built by the current upload, on the main thread.
	///
	/// Double buffered against <see cref="_renderBuckets"/> because RenderSceneObject runs on the
	/// render thread: handing it the list being rebuilt lets it walk half-written offsets and draw
	/// sprites from the wrong part of the buffer. Same reason the counts below are snapshotted.
	/// </summary>
	private List<SpriteDrawBucket> _drawBuckets = new();

	/// <summary>
	/// The finished runs, swapped in at the end of an upload and only read by the render thread.
	/// Empty when this batch holds a single blend state, which draws in one call as before.
	/// </summary>
	private List<SpriteDrawBucket> _renderBuckets = new();

	/// <summary>
	/// True when this batch mixes blend states so that sort layers can order across them. Set from
	/// the render group's flags - see <see cref="SceneSpriteSystem"/>.
	/// </summary>
	public bool MixedBlend { get; set; } = false;

	/// <summary>
	/// Works out the runs this batch has to draw, when it holds more than one blend state.
	///
	/// The GPU sort orders by layer first and blend second, so sprites sharing a layer and a blend
	/// state end up next to each other - which means a run's position is just how many sprites
	/// sort ahead of it. Counting that on the CPU is what avoids reading the sort result back.
	///
	/// Leaves the list empty for every other case, and the batch then draws in one call as before.
	/// </summary>
	private void BuildDrawBuckets( int componentCount, int spriteCount, int splotCount, bool sorted )
	{
		_drawBuckets.Clear();

		// Unsorted batches have no meaningful order to divide up, and the runs are only in step
		// with the buffer if every sprite in it came through the component path - particles set no
		// sort key, and motion blur adds instances the count above never saw.
		if ( !MixedBlend || !sorted ) return;
		if ( spriteCount != componentCount || splotCount != 0 ) return;

		var layerCount = ProjectSettings.Sorting.Layers.Count;
		var requiredCounts = Math.Max( layerCount, 1 ) * SpriteDrawPlan.BlendModeCount;

		if ( _bucketCounts.Length < requiredCounts )
		{
			_bucketCounts = new int[requiredCounts];
		}

		SpriteDrawPlan.BuildBuckets(
			_bucketLayers.AsSpan( 0, componentCount ),
			_bucketBlends.AsSpan( 0, componentCount ),
			layerCount,
			_drawBuckets,
			_bucketCounts );
	}

	/// <summary>
	/// Works out which sorting group each sprite belongs to and gets every one of those groups to
	/// rebuild its member ordering.
	///
	/// This has to happen before the parallel upload pass rather than inside it: ranking reads a
	/// whole group's members at once, and walking a sprite's ancestors is not safe to do from
	/// several threads at the same time. Scenes with no sorting groups pay a single null check
	/// per sprite for it.
	/// </summary>
	private void ResolveSortingGroups( int componentCount )
	{
		if ( _spriteGroupBuffer.Length < componentCount )
		{
			_spriteGroupBuffer = new SortingGroup[componentCount * 2];
		}

		_rankedGroups.Clear();

		for ( var i = 0; i < componentCount; i++ )
		{
			var group = SortingGroup.FindFor( _componentBuffer[i] );

			_spriteGroupBuffer[i] = group;

			// Rebuild each group once, however many of its members are in this batch.
			if ( group is not null && _rankedGroups.Add( group ) )
			{
				group.RefreshRanks();
			}
		}
	}
	private readonly object _boundsLock = new();

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
			if ( SpriteDataBufferRented )
			{
				ArrayPool<SpriteData>.Shared.Return( SpriteDataBuffer, clearArray: false );
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
				_bucketLayers = new int[componentCount * 2];
				_bucketBlends = new SpriteBlendMode[componentCount * 2];
			}

			int index = 0;
			foreach ( var component in Components.Values )
			{
				_componentBuffer[index++] = component;
			}

			// Hoisted out of the loop - resolving a layer id to its position in the draw order is a
			// lookup into this, and it must not change underneath the parallel pass.
			var sorting = ProjectSettings.Sorting;

			ResolveSortingGroups( componentCount );

			object boundsLock = _boundsLock;
			Parallel.For<(Vector3 mins, Vector3 maxs)>(
				0, componentCount,
				() => (new Vector3( float.MaxValue, float.MaxValue, float.MaxValue ),
					   new Vector3( float.MinValue, float.MinValue, float.MinValue )),
				( i, _, local ) =>
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

					uint packedFogAndAlpha = SpriteData.PackFogAndAlphaCutout( c.FogStrength, c.AlphaCutoff );

					// Ranks were built in the sequential pass above, so this only reads them.
					var (sortLayer, sortOrder, sortOrigin, sortRank) = c.ResolveSorting( _spriteGroupBuffer[i] );

					// Recorded per sprite so the draw runs can be counted once the loop is done.
					// Each iteration owns its own slot, so this is safe to write from here.
					_bucketLayers[i] = sorting.GetLayerIndex( sortLayer.Id );
					_bucketBlends[i] = SpriteDrawPlan.GetBlendMode( c.Opaque, c.Additive );

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
						SamplerIndex = SamplerState.GetBindlessIndex( sampler with { Filter = c.TextureFilter } ),
						Offset = c.Pivot,
						SortKey = SpriteData.PackSortKey( sorting.GetLayerIndex( sortLayer.Id ), SpriteDrawPlan.GetBlendMode( c.Opaque, c.Additive ), sortOrder ),
						SortOriginOffset = sortOrigin - spritePos,
						SortRank = (uint)sortRank
					};

					var pivot = c.Pivot;
					float halfSize = MathF.Max(
						MathF.Max( pivot.x, 1f - pivot.x ) * 2f * spriteScale.x,
						MathF.Max( pivot.y, 1f - pivot.y ) * 2f * spriteScale.y
					);
					var expand = new Vector3( halfSize, halfSize, halfSize );
					return (Vector3.Min( local.mins, spritePos - expand ), Vector3.Max( local.maxs, spritePos + expand ));
				},
				local =>
				{
					lock ( boundsLock )
					{
						boundsMin = Vector3.Min( boundsMin, local.mins );
						boundsMax = Vector3.Max( boundsMax, local.maxs );
					}
				}
			);

		}

		foreach ( var spriteGroup in SpriteGroups.Values )
		{
			boundsMin = Vector3.Min( boundsMin, spriteGroup.Bounds.Mins );
			boundsMax = Vector3.Max( boundsMax, spriteGroup.Bounds.Maxs );
		}

		// Use a degenerate zero-size bounds for empty batches so they can be frustum-culled.
		Bounds = spriteCount > 0 ? new BBox( boundsMin, boundsMax ) : default;

		_pendingSpriteCount = spriteCount;
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
		BuildDrawBuckets( componentCount, SpriteCount, SplotCount, sorted );
		bool filtered = Filtered;
		bool additive = Additive;
		bool opaque = Opaque;
		int bufferSize = CurrentBufferSize;
		int totalInstances = spriteCount + splotCount;

		_commandList.SetBufferData( SpriteAtomicCounter, ZeroUint );

		if ( sorted && totalInstances >= 2 )
		{
			_commandList.Attributes.Set( "SortBuffer", (GpuBuffer)GPUSortingBuffer );
			_commandList.Attributes.Set( "SortKeyBuffer", (GpuBuffer)GPUSortKeyBuffer );
			_commandList.Attributes.Set( "Count", bufferSize );
			_commandList.Attributes.SetCombo( "D_CLEAR", 1 );
			_commandList.DispatchCompute( SortComputeShader, bufferSize, 1, 1 );

			_commandList.ResourceBarrierTransition( (GpuBuffer)GPUSortingBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
			_commandList.ResourceBarrierTransition( (GpuBuffer)GPUSortKeyBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
		}

		_commandList.ResourceBarrierTransition( SpriteAtomicCounter, ResourceState.Common );
		_commandList.ResourceBarrierTransition( (GpuBuffer)SpriteBuffer, ResourceState.Common );
		_commandList.ResourceBarrierTransition( (GpuBuffer)SpriteBufferOut, ResourceState.Common );
		_commandList.ResourceBarrierTransition( (GpuBuffer)GPUSortKeyBuffer, ResourceState.Common );

		_commandList.Attributes.Set( "Sprites", (GpuBuffer)SpriteBuffer );
		_commandList.Attributes.Set( "SpriteBufferOut", (GpuBuffer)SpriteBufferOut );
		_commandList.Attributes.Set( "SpriteCount", spriteCount );

		// The motion blur trails are placed by an atomic counter rather than by thread index, so the
		// shader has no other way to tell where the buffers end.
		_commandList.Attributes.Set( "BufferCapacity", bufferSize );

		_commandList.Attributes.Set( "AtomicCounter", SpriteAtomicCounter );
		_commandList.Attributes.Set( "SortKeyBuffer", (GpuBuffer)GPUSortKeyBuffer );
		_commandList.DispatchCompute( SpriteComputeShader, spriteCount, 1, 1 );

		_commandList.ResourceBarrierTransition( SpriteAtomicCounter, ResourceState.Common );
		_commandList.ResourceBarrierTransition( (GpuBuffer)SpriteBufferOut, ResourceState.Common );

		if ( sorted && totalInstances >= 2 )
		{
			_commandList.ResourceBarrierTransition( (GpuBuffer)GPUSortKeyBuffer, ResourceState.Common );
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
					_commandList.ResourceBarrierTransition( (GpuBuffer)GPUSortKeyBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
				}
			}
		}

		bool didSort = sorted && totalInstances >= 2;

		_renderInstanceCount = totalInstances;
		_renderIsSorted = didSort;
		_renderFiltered = filtered;
		_renderAdditive = additive;
		_renderOpaque = opaque;

		// Publish the runs alongside the counts they were built against. Swapping the reference
		// hands the render thread a finished list, and gives us the old one back to build into
		// next frame without allocating.
		(_renderBuckets, _drawBuckets) = (_drawBuckets, _renderBuckets);
	}

	public override void RenderSceneObject()
	{
		base.RenderSceneObject();

		if ( _pendingSpriteCount == 0 || !_commandList.Enabled )
			return;

		Graphics.Attributes.Set( "CameraPosition", Graphics.CameraPosition );
		_commandList.ExecuteOnRenderThread();

		var attributes = RenderAttributes.Pool.Get();
		try
		{
			attributes.SetCombo( "D_OPAQUE", _renderOpaque ? 1 : 0 );
			attributes.Set( "IsSorted", _renderIsSorted ? 1 : 0 );
			attributes.Set( "SpriteCount", _renderInstanceCount );
			attributes.Set( "Filtered", _renderFiltered );
			attributes.Set( "Sprites", (GpuBuffer)SpriteBufferOut );
			attributes.Set( "SortLUT", (GpuBuffer)GPUSortingBuffer );
			attributes.Set( "Vertices", (GpuBuffer)VertexBuffer );
			attributes.Set( "g_bNonDirectionalDiffuseLighting", true );

			// Read once - the main thread may swap this reference between uploads.
			var buckets = _renderBuckets;

			if ( buckets.Count == 0 )
			{
				attributes.SetCombo( "D_BLEND", _renderAdditive ? 1 : 0 );
				attributes.Set( "SortLUTOffset", 0 );

				Graphics.DrawIndexedInstanced( (GpuBuffer)IndexBuffer, SpriteMaterial, _renderInstanceCount, attributes );
			}
			else
			{
				// One draw per run, walked in order, so a lower sort layer is finished before a
				// higher one starts. This is the whole reason the batch holds mixed blend states:
				// ordering between scene objects is not ours to control, ordering within one is.
				foreach ( var bucket in buckets )
				{
					attributes.SetCombo( "D_BLEND", bucket.Blend == SpriteBlendMode.Additive ? 1 : 0 );

					// The shader walks the sort table backwards from the end, so a run starting
					// this far into the draw order starts this far back from the end.
					attributes.Set( "SortLUTOffset", bucket.Offset );

					Graphics.DrawIndexedInstanced( (GpuBuffer)IndexBuffer, SpriteMaterial, bucket.Count, attributes );
				}
			}
		}
		finally
		{
			RenderAttributes.Pool.Return( attributes );
		}
	}
}
