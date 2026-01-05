namespace Sandbox.Rendering;

using System.Buffers;
using System.Runtime.InteropServices;

/// <summary>
/// This object renders every sprite registered to it in a single draw call. It takes care of sorting, sampling, and the whole pipeline regarding sprites.
/// The SceneSpriteSystem is responsible for pushing sprites into this object depending on its properties.
/// </summary>
internal sealed class SpriteBatchSceneObject : SceneCustomObject
{
	internal readonly record struct SpriteGroup( SpriteData[] SharedSprites, int Offset, int Count );
	public bool Sorted { get; set; } = false;
	public bool Filtered { get; set; } = false;
	public bool Additive { get; set; } = false;
	public bool Opaque { get; set; } = false;

	internal Dictionary<Guid, SpriteRenderer> Components = new();

	private readonly ComputeShader SpriteComputeShader = new( "sprite/sprite_cs" );
	private readonly ComputeShader SortComputeShader = new( "sort_cs" );
	private readonly GpuBuffer<uint> SpriteAtomicCounter;

	private readonly Material SpriteMaterial;

	internal Dictionary<Guid, SpriteGroup> SpriteGroups = [];

	internal readonly SamplerState sampler = new()
	{
		AddressModeU = TextureAddressMode.Clamp,
		AddressModeV = TextureAddressMode.Clamp,
	};

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

	public SpriteBatchSceneObject( Scene scene ) : base( scene.SceneWorld )
	{
		SpriteMaterial = Material.FromShader( "sprite/sprite_ps.shader" );

		InitializeSpriteMesh();

		// GPU buffers
		SpriteBuffer = new( CurrentBufferSize );
		SpriteBufferOut = new( CurrentBufferSize );
		GPUSortingBuffer = new( CurrentBufferSize );
		GPUDistanceBuffer = new( CurrentBufferSize );
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
		GPUSortingBuffer?.Dispose();
		GPUDistanceBuffer?.Dispose();

		SpriteBuffer = new( CurrentBufferSize );
		SpriteBufferOut = new( CurrentBufferSize );
		GPUSortingBuffer = new( CurrentBufferSize );
		GPUDistanceBuffer = new( CurrentBufferSize );
	}

	private readonly Dictionary<Guid, int> _precomputedSplotCounts = [];

	// Pre-allocated buffer to avoid GC allocations in hot path
	private SpriteRenderer[] _componentBuffer = new SpriteRenderer[16];

	public void RegisterSprite( Guid ownerId, SpriteData[] sharedSprites, int offset, int count, int splotCount )
	{
		SpriteGroups[ownerId] = new( sharedSprites, offset, count );
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
		int requiredSize = SplotCount + SpriteCount;

		// Clear cached splot count to force recalculation
		_splotCount = 0;

		// Only resize if we actually need more space
		if ( requiredSize > CurrentBufferSize )
		{
			ResizeBuffers( requiredSize );
		}

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

		if ( SpriteDataBuffer == null || SpriteDataBuffer.Length < spriteCount )
		{
			if ( SpriteDataBufferRented )
			{
				ArrayPool<SpriteData>.Shared.Return( SpriteDataBuffer, clearArray: false );
			}

			SpriteDataBuffer = ArrayPool<SpriteData>.Shared.Rent( spriteCount );
			SpriteDataBufferRented = true;
		}

		// Upload sprites
		int componentCount = Components.Count;
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
			}

			Parallel.For( 0, componentCount, i =>
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

				var flipFlags = SpriteRenderer.FlipFlags.None;
				if ( c.FlipHorizontal ) flipFlags |= SpriteRenderer.FlipFlags.FlipX;
				if ( c.FlipVertical ) flipFlags |= SpriteRenderer.FlipFlags.FlipY;

				var rgbe = c.Color.ToRgbe();
				var alpha = (byte)(c.Color.a.Clamp( 0.0f, 1.0f ) * 255.0f);
				var tintColor = new Color32( rgbe.r, rgbe.g, rgbe.b, alpha );

				var overlayRgbe = c.OverlayColor.ToRgbe();
				var overlayAlpha = (byte)(c.OverlayColor.a.Clamp( 0.0f, 1.0f ) * 255.0f);
				var overlayColor = new Color32( overlayRgbe.r, overlayRgbe.g, overlayRgbe.b, overlayAlpha );

				int lightingFlag = c.Lighting ? 1 : 0;
				uint packedExponent = (uint)(((byte)lightingFlag) | rgbe.a << 16);

				uint packedFogAndAlpha = SpriteData.PackFogAndAlphaCutout( c.FogStrength, c.AlphaCutoff );

				SpriteDataBuffer[i] = new SpriteData
				{
					Position = transform.Position,
					Rotation = new( rotation.x, rotation.y, rotation.z ),
					Scale = new( transform.Scale.x, transform.Scale.z ),
					TextureHandle = c.Texture is null ? Texture.Invalid.Index : c.Texture.Index,
					TintColor = tintColor.RawInt,
					OverlayColor = overlayColor.RawInt,
					RenderFlags = (int)flipFlags,
					BillboardMode = (uint)c.Billboard,
					FogStrengthCutout = packedFogAndAlpha,
					Lighting = packedExponent,
					DepthFeather = c.DepthFeather,
					SamplerIndex = SamplerState.GetBindlessIndex( sampler with { Filter = c.TextureFilter } ),
					Offset = c.Pivot
				};
			} );
		}

		// Upload components to GPU first
		if ( Components.Count > 0 )
		{
			SpriteBuffer.SetData( SpriteDataBuffer );
		}

		// Upload each particle group directly to GPU with offset
		int currentOffset = Components.Count;
		foreach ( var spriteGroup in SpriteGroups.Values )
		{
			unsafe
			{
				var sourceSpan = spriteGroup.SharedSprites.AsSpan( spriteGroup.Offset, spriteGroup.Count );

				// Upload directly to GPU at the correct offset
				SpriteBuffer.SetData( sourceSpan, currentOffset );
				currentOffset += spriteGroup.Count;
			}
		}

		GPUUploadQueued = false;
	}

	private const int GroupSize = 256;
	private const int MaxDimGroups = 1024;
	private const int MaxDimThreads = GroupSize * MaxDimGroups;

	private void PreSort()
	{
		if ( SpriteCount < 2 ) return;

		// First we clear the buffers to prepare for sorting
		SortComputeShader.Attributes.SetCombo( "D_CLEAR", 1 );
		SortComputeShader.Attributes.Set( "SortBuffer", GPUSortingBuffer );
		SortComputeShader.Attributes.Set( "DistanceBuffer", GPUDistanceBuffer );
		SortComputeShader.Attributes.Set( "Count", CurrentBufferSize );
		SortComputeShader.Dispatch( CurrentBufferSize, 1, 1 );

		Graphics.ResourceBarrierTransition( GPUSortingBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
		Graphics.ResourceBarrierTransition( GPUDistanceBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
	}

	/// <summary>
	/// Performs a GPU bitonic sort
	/// </summary>
	private void Sort()
	{
		// Distance buffer is already filled by GPU compute shader, no need to update from CPU
		Graphics.ResourceBarrierTransition( GPUDistanceBuffer, Sandbox.Rendering.ResourceState.Common );

		// Sort
		SortComputeShader.Attributes.SetCombo( "D_CLEAR", 0 );

		var x = Math.Min( CurrentBufferSize, MaxDimThreads );
		var y = (CurrentBufferSize + MaxDimThreads - 1) / MaxDimThreads;
		var z = 1;

		for ( var dim = 2; dim <= CurrentBufferSize; dim <<= 1 )
		{
			SortComputeShader.Attributes.Set( "Dim", dim );

			for ( var block = dim >> 1; block > 0; block >>= 1 )
			{
				SortComputeShader.Attributes.Set( "Block", block );
				SortComputeShader.Dispatch( x, y, z );

				// Make sure sort buffer is ready to use
				Graphics.ResourceBarrierTransition( GPUSortingBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
				Graphics.ResourceBarrierTransition( GPUDistanceBuffer, ResourceState.UnorderedAccess, ResourceState.UnorderedAccess );
			}
		}
	}

	/// <summary>
	/// Rendering logic of the sprites
	/// </summary>
	public override void RenderSceneObject()
	{
		base.RenderSceneObject();

		if ( SpriteCount == 0 )
		{
			return;
		}

		if ( Sorted )
		{
			PreSort();
		}

		// Generate trails and UVs (this is mainly for particles)
		SpriteAtomicCounter.SetData( [0] ); // Reset atomic counter
		Graphics.ResourceBarrierTransition( SpriteAtomicCounter, ResourceState.Common );
		Graphics.ResourceBarrierTransition( SpriteBuffer, ResourceState.Common );
		Graphics.ResourceBarrierTransition( SpriteBufferOut, ResourceState.Common );
		Graphics.ResourceBarrierTransition( GPUDistanceBuffer, ResourceState.Common );

		var attributes = RenderAttributes.Pool.Get();

		attributes.Set( "Sprites", SpriteBuffer );
		attributes.Set( "SpriteBufferOut", SpriteBufferOut );

		attributes.Set( "SpriteCount", SpriteCount );
		attributes.Set( "AtomicCounter", SpriteAtomicCounter );

		// Sorting
		attributes.Set( "DistanceBuffer", GPUDistanceBuffer );
		attributes.Set( "CameraPosition", Graphics.CameraPosition );

		SpriteComputeShader.DispatchWithAttributes( attributes, SpriteCount, 1, 1 );

		RenderAttributes.Pool.Return( attributes );

		// Barried for the new sprites generated
		Graphics.ResourceBarrierTransition( SpriteAtomicCounter, ResourceState.Common );
		Graphics.ResourceBarrierTransition( SpriteBufferOut, ResourceState.Common );

		Graphics.Attributes.SetCombo( "D_BLEND", Additive ? 1 : 0 );
		Graphics.Attributes.SetCombo( "D_OPAQUE", Opaque ? 1 : 0 );

		// Sort
		if ( Sorted )
		{
			Sort();
		}

		// Draw the sprites
		Graphics.Attributes.Set( "IsSorted", Sorted ? 1 : 0 );
		Graphics.Attributes.Set( "SpriteCount", SpriteCount + SplotCount );

		Graphics.Attributes.Set( "Filtered", Filtered );
		Graphics.Attributes.Set( "Sprites", SpriteBufferOut );
		Graphics.Attributes.Set( "SortLUT", GPUSortingBuffer ); // Always bind even if not used

		// Vertex Pulling
		Graphics.Attributes.Set( "Vertices", VertexBuffer );
		Graphics.Attributes.Set( "g_bNonDirectionalDiffuseLighting", true );
		Graphics.DrawIndexedInstanced( IndexBuffer, SpriteMaterial, SpriteCount + SplotCount );
	}
}
