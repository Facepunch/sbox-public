using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox.SceneRenderer.Gpu;

/// <summary>
/// Per-frame transforms and per-view instance IDs (<c>transform_buffer.fxc</c>).
/// Objects write entries once; views reference them through <c>ids[first + i]</c>. See <c>docs/native/instancing.md</c>.
/// </summary>
internal sealed class TransformBuffer : IDisposable
{
	/// <summary>
	/// Native <c>SceneSystemTransformEntry_t</c>: row-major 3x4 transform and instance data.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct Entry
	{
		public Vector4 Row0;
		public Vector4 Row1;
		public Vector4 Row2;
		public float Alpha;
		public uint TintRgb888;
		public uint VertexCacheOffset;

		/// <summary>
		/// Nonzero enables skinning with this many weights; bone matrices start two entries later.
		/// </summary>
		public uint BlendWeightCount;
	}

	readonly UploadRing<Entry> ring = new( "SceneRenderer transforms" );
	readonly UploadRing<uint> idRing = new( "SceneRenderer instance ids", GpuBuffer.UsageFlags.Vertex );
	Entry[] entries = new Entry[256];
	uint[] ids = new uint[256];

	/// <summary>
	/// Uploaded instance IDs. Owned here to avoid the scenesystem's fixed capacity.
	/// </summary>
	public GpuBuffer<uint> InstanceIds => idRing.Current;

	/// <summary>
	/// Entries written this frame.
	/// </summary>
	public int Count { get; private set; }

	/// <summary>
	/// Instance ids written this frame.
	/// </summary>
	public int InstanceCount { get; private set; }

	/// <summary>
	/// The buffer this frame's entries went into, after <see cref="Upload"/>.
	/// </summary>
	public GpuBuffer<Entry> Current => ring.Current;

	/// <summary>
	/// Globally unique frame generation, preventing slot reuse across cameras' buffers.
	/// </summary>
	public int Generation { get; private set; }

	static int lastGeneration;

	public void Clear()
	{
		Count = 0;
		InstanceCount = 0;
		Generation = Interlocked.Increment( ref lastGeneration );
	}

	/// <summary>
	/// An object's entry this frame: written the first time a view asks for it, the same slot after that.
	/// </summary>
	public int SlotFor( RenderObject obj, in Matrix m, Color tint )
	{
		if ( obj.SlotGeneration == Generation ) return obj.Slot;

		obj.SlotGeneration = Generation;
		obj.Slot = Add( m, tint );
		return obj.Slot;
	}

	/// <summary>
	/// Write or reuse rigid per-draw entries across all LODs, combining object and draw tints
	/// (<c>CBaseSceneObjectDesc</c>). Returns the first slot.
	/// </summary>
	public int TintedSlotsFor( RenderObject obj, in Matrix m, Color tint, RenderMesh mesh )
	{
		if ( obj.SlotGeneration == Generation ) return obj.Slot;

		obj.SlotGeneration = Generation;
		obj.Slot = Count;
		for ( int lod = 0; lod < mesh.LodCount; lod++ )
		{
			foreach ( var draw in mesh.DrawsForLod( lod ) )
			{
				var drawTint = draw.Tint ?? Vector3.One;
				Add( m, new Color( tint.r * drawTint.x, tint.g * drawTint.y, tint.b * drawTint.z, tint.a ) );
			}
		}

		return obj.Slot;
	}

	/// <summary>
	/// Append a transform slot to the instance-ID stream and return its index.
	/// </summary>
	public int AddInstance( int slot )
	{
		if ( InstanceCount == ids.Length ) Array.Resize( ref ids, ids.Length * 2 );

		ids[InstanceCount] = (uint)slot;
		return InstanceCount++;
	}

	/// <summary>
	/// Write an object's transform and tint, returning its slot.
	/// </summary>
	public int Add( in Matrix m, Color tint )
	{
		if ( Count == entries.Length ) Array.Resize( ref entries, entries.Length * 2 );

		entries[Count] = MakeEntry( m, tint );
		return Count++;
	}

	/// <summary>
	/// A rigid object's entry: its matrix and tint, with no skinning.
	/// </summary>
	internal static Entry MakeEntry( in Matrix m, Color tint )
	{
		// Transpose into native column-vector layout.
		return new Entry
		{
			Row0 = new( m.M11, m.M21, m.M31, m.M41 ),
			Row1 = new( m.M12, m.M22, m.M32, m.M42 ),
			Row2 = new( m.M13, m.M23, m.M33, m.M43 ),
			Alpha = tint.a,
			TintRgb888 = PackRgb888( tint ),
		};
	}

	/// <summary>
	/// Append base, morph and bone entries; return the base slot
	/// (<c>CAnimatableSceneObjectDesc::AllocateTransforms</c>, <c>SetupBones</c>). Vertex-cache offset is assigned later.
	/// </summary>
	public int AddSkinned( Color tint, int blendWeights, ReadOnlySpan<Matrix3x4> bones )
	{
		var count = 2 + bones.Length;
		while ( Count + count > entries.Length ) Array.Resize( ref entries, entries.Length * 2 );

		var slot = Count;
		ref var first = ref entries[slot];
		first = default;

		// Match native's base-entry animation fields.
		first.Row0 = new( 0, 0, 1, 1 );
		first.Alpha = tint.a;
		first.TintRgb888 = PackRgb888( tint );
		first.VertexCacheOffset = uint.MaxValue;
		first.BlendWeightCount = (uint)blendWeights;

		entries[slot + 1] = default;

		for ( int i = 0; i < bones.Length; i++ )
		{
			ref var bone = ref entries[slot + 2 + i];
			bone = default;
			bone.Row0 = bones[i].Row0;
			bone.Row1 = bones[i].Row1;
			bone.Row2 = bones[i].Row2;
		}

		// SetupBones copies the extra data into the first bone too
		if ( bones.Length > 0 )
		{
			ref var bone = ref entries[slot + 2];
			bone.Alpha = first.Alpha;
			bone.TintRgb888 = first.TintRgb888;
			bone.VertexCacheOffset = first.VertexCacheOffset;
			bone.BlendWeightCount = first.BlendWeightCount;
		}

		Count += count;
		return slot;
	}

	/// <summary>
	/// Store atlas U/V offsets and ranges in the morph entry (<c>CalculateMorphSubrectData</c>).
	/// </summary>
	public void SetMorphSubrect( int baseSlot, Vector4 subrect )
	{
		ref var entry = ref entries[baseSlot + 1];
		entry.Row0.z = subrect.x;
		entry.Row0.w = subrect.y;
		entry.Row1.w = subrect.z;
		entry.Row2.w = subrect.w;
	}

	/// <summary>
	/// Assign a skinned entry's vertex-cache block before upload.
	/// </summary>
	public void SetVertexCacheOffset( int slot, int offset ) => entries[slot].VertexCacheOffset = (uint)offset;

	/// <summary>
	/// An entry as written - for the tests.
	/// </summary>
	internal Entry this[int slot] => entries[slot];

	/// <summary>
	/// The entry an instance reads - for the tests.
	/// </summary>
	internal int InstanceSlot( int instance ) => (int)ids[instance];

	/// <summary>
	/// Copy this frame's entries and instance ids to the GPU.
	/// </summary>
	public void Upload( RenderContext context )
	{
		ring.Upload( context, entries.AsSpan( 0, Count ), entries.Length );
		idRing.Upload( context, ids.AsSpan( 0, InstanceCount ), ids.Length );
	}

	/// <summary>
	/// Pack RGB with red in bits 16–23; clamp and round ties to even (<c>CreateExtraShaderData_TintRGBA32</c>, <c>RoundFloatToByte</c>).
	/// </summary>
	internal static uint PackRgb888( Color color ) => Channel( color.r ) << 16 | Channel( color.g ) << 8 | Channel( color.b );

	static uint Channel( float value ) => (uint)MathF.Round( Math.Clamp( value, 0, 1 ) * 255.0f );

	public void Dispose()
	{
		ring.Dispose();
		idRing.Dispose();
	}
}
