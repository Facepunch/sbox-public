using Borealis.Gpu;
using Borealis.Source2.Compression;
using Borealis.Source2.Resources;
using Borealis.Source2.Shaders;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Borealis.Source2.Models;

/// <summary>
/// One of a model's meshes (<c>CRenderMesh</c>): its vertex and index buffers, decoded, and its draw calls, bounds and
/// skeleton. Embedded in a <c>.vmdl_c</c> as an <c>MDAT</c> (KV3) and <c>MBUF</c> (buffers) block pair, or a
/// <c>.vmesh_c</c> of its own (<c>DATA</c> and <c>VBIB</c>).
/// </summary>
public sealed class ModelMesh
{
	/// <summary>Its name: <c>body_lod0</c>, the model's file name for single-mesh models.</summary>
	public string Name { get; init; }

	/// <summary>Its index among the model's meshes, what draws name it by.</summary>
	public int Index { get; init; }

	/// <summary>The mesh groups (bits) it belongs to: a body group choice, or all of them.</summary>
	public ulong MeshGroupMask { get; init; }

	/// <summary>The LODs (bits) it draws at; 0xFF for every LOD.</summary>
	public byte LodMask { get; init; }

	/// <summary>Bounds of its draws in the model's space (the union of its scene objects').</summary>
	public Vector3 Mins { get; init; }

	/// <summary>Bounds of its draws in the model's space.</summary>
	public Vector3 Maxs { get; init; }

	/// <summary>Its vertex buffers, decoded.</summary>
	public IReadOnlyList<MeshVertexBuffer> VertexBuffers { get; init; }

	/// <summary>Its index buffers, decoded.</summary>
	public IReadOnlyList<MeshIndexBuffer> IndexBuffers { get; init; }

	/// <summary>Its draw calls, across its scene objects in order (as <c>IModel::GetDrawCallInfo</c> numbers them).</summary>
	public IReadOnlyList<MeshDrawCall> DrawCalls { get; init; }

	/// <summary>The bones its vertices are weighted to, in its own order; empty when it isn't skinned.</summary>
	public IReadOnlyList<MeshBone> Bones { get; init; }

	/// <summary>Bone weights per vertex its skinning uses (<c>m_nBoneWeightCount</c>).</summary>
	public int BoneWeightCount { get; init; }

	/// <summary>Whether it has morph targets (an <c>MRPH</c> block).</summary>
	public bool HasMorphs { get; init; }

	/// <summary>Its morph targets and flex rules, or null when it has none (or they couldn't be read).</summary>
	public MorphSet Morphs { get; init; }

	/// <summary>The compiled morph texture its morphs are baked into, or null.</summary>
	public string MorphTexture { get; init; }

	/// <summary>Whether it's skinned: it has bones.</summary>
	public bool IsSkinned => Bones.Count > 0;

	/// <summary>
	/// Vertices its draws use, as <c>CRenderMesh::CalculateNumTriangles</c> counts them: each draw's count, once per
	/// vertex buffer per scene object.
	/// </summary>
	public int VertexCount
	{
		get
		{
			var count = 0;
			var counted = new HashSet<(int, int)>();
			foreach ( var draw in DrawCalls )
			{
				var added = false;
				foreach ( var (buffer, _) in draw.VertexBuffers ) added |= counted.Add( (draw.SceneObject, buffer) );
				if ( added ) count += draw.VertexCount;
			}

			return count;
		}
	}

	/// <summary>Triangles its draws draw.</summary>
	public int TriangleCount => DrawCalls.Sum( x => x.IndexCount / 3 );

	/// <inheritdoc/>
	public override string ToString() => Name;

	/// <summary>The same mesh over other buffers (as many, laid out the same): a cooked copy's.</summary>
	internal ModelMesh WithBuffers( IReadOnlyList<MeshVertexBuffer> vertexBuffers, IReadOnlyList<MeshIndexBuffer> indexBuffers ) => new()
	{
		Name = Name,
		Index = Index,
		MeshGroupMask = MeshGroupMask,
		LodMask = LodMask,
		Mins = Mins,
		Maxs = Maxs,
		VertexBuffers = vertexBuffers,
		IndexBuffers = indexBuffers,
		DrawCalls = DrawCalls,
		Bones = Bones,
		BoneWeightCount = BoneWeightCount,
		HasMorphs = HasMorphs,
		Morphs = Morphs,
		MorphTexture = MorphTexture,
	};

	/// <summary>
	/// Read a mesh from its KV3 (<c>CRenderMesh</c>) and buffer blocks.
	/// </summary>
	internal static ModelMesh Read( ReadOnlySpan<byte> kv3, ReadOnlySpan<byte> buffers, string name, int index, ulong meshGroups, byte lods, bool morphs, string morphTexture, MorphSet morphSet = null, ReadOnlySpan<byte> meshlets = default )
	{
		if ( Kv3.Read( kv3 ) is not Dictionary<string, object> root ) throw new InvalidDataException( $"Mesh {name} isn't a KV3 object" );
		var (vertexBuffers, indexBuffers) = ReadBuffers( buffers, name );

		// The compiler's meshlets (an MSHL block), each index buffer given its ranges'
		if ( !meshlets.IsEmpty && CookedMeshlets.Read( meshlets, vertexBuffers, indexBuffers ) is { } cooked )
		{
			for ( int i = 0; i < indexBuffers.Length; i++ )
			{
				if ( cooked[i] is not null ) indexBuffers[i] = indexBuffers[i] with { Meshlets = cooked[i] };
			}
		}

		var draws = new List<MeshDrawCall>();
		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );
		var sceneObjects = 0;
		foreach ( var sceneObject in Kv.Objects( root.Get( "m_sceneObjects" ) ) )
		{
			mins = Vector3.Min( mins, Kv.Vector3( sceneObject.Get( "m_vMinBounds" ) ) );
			maxs = Vector3.Max( maxs, Kv.Vector3( sceneObject.Get( "m_vMaxBounds" ) ) );
			foreach ( var draw in Kv.Objects( sceneObject.Get( "m_drawCalls" ) ) ) draws.Add( MeshDrawCall.Read( draw, vertexBuffers, name ) with { SceneObject = sceneObjects } );
			sceneObjects++;
		}

		if ( draws.Count == 0 ) mins = maxs = Vector3.Zero;
		EmptyDraws( draws, vertexBuffers, indexBuffers );
		indexBuffers = IndexUnindexedDraws( draws, indexBuffers );

		var bones = new List<MeshBone>();
		var weights = 4;
		if ( root.Get( "m_skeleton" ) is Dictionary<string, object> skeleton )
		{
			weights = (int)Kv.Number( skeleton.Get( "m_nBoneWeightCount" ), 4 );
			foreach ( var bone in Kv.Objects( skeleton.Get( "m_bones" ) ) )
				bones.Add( new MeshBone( bone.Get( "m_boneName" ) as string, bone.Get( "m_parentName" ) as string, Kv.Matrix3x4( bone.Get( "m_invBindPose" ) ) ) );
		}

		return new ModelMesh
		{
			Name = name,
			Index = index,
			MeshGroupMask = meshGroups,
			LodMask = lods,
			Mins = mins,
			Maxs = maxs,
			VertexBuffers = vertexBuffers,
			IndexBuffers = indexBuffers,
			DrawCalls = draws,
			Bones = bones,
			BoneWeightCount = weights,
			HasMorphs = morphs,
			MorphTexture = string.IsNullOrEmpty( morphTexture ) ? null : morphTexture,
			Morphs = morphSet,
		};
	}

	/// <summary>
	/// An empty mesh (a run-time mesh with no vertices or indices: its buffers are 0 x 0) draws nothing, as native draws it:
	/// a draw whose vertex buffer has no vertices, or whose indices are in an index buffer with none, draws no vertices
	/// and no indices.
	/// </summary>
	internal static void EmptyDraws( List<MeshDrawCall> draws, MeshVertexBuffer[] vertexBuffers, MeshIndexBuffer[] indexBuffers )
	{
		for ( int i = 0; i < draws.Count; i++ )
		{
			var draw = draws[i];
			var vertices = draw.VertexBuffers.Count > 0 ? draw.VertexBuffers[0].Buffer : 0;
			var noVertices = vertices < vertexBuffers.Length && vertexBuffers[vertices].Count == 0;
			var noIndices = draw.IndexCount > 0 && (uint)draw.IndexBuffer.Buffer < (uint)indexBuffers.Length && indexBuffers[draw.IndexBuffer.Buffer].Count == 0;
			if ( noVertices || noIndices ) draws[i] = draw with { VertexCount = 0, IndexCount = 0 };
		}
	}

	/// <summary>
	/// A mesh made in code without indices (<c>Mesh.SetVertexRange</c> alone) draws its vertices in order, as native draws it
	/// unindexed: its draws with no indices become ranges of an index buffer counting up from zero, added to its others.
	/// </summary>
	internal static MeshIndexBuffer[] IndexUnindexedDraws( List<MeshDrawCall> draws, MeshIndexBuffer[] indexBuffers )
	{
		var vertices = 0;
		foreach ( var draw in draws )
		{
			if ( draw.IndexCount <= 0 && draw.VertexCount > 0 ) vertices = Math.Max( vertices, draw.BaseVertex + draw.VertexCount );
		}

		if ( vertices == 0 ) return indexBuffers;

		var data = new byte[vertices * sizeof( uint )];
		var counting = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( data.AsSpan() );
		for ( int i = 0; i < vertices; i++ ) counting[i] = (uint)i;

		var slot = indexBuffers.Length;
		for ( int i = 0; i < draws.Count; i++ )
		{
			var draw = draws[i];
			if ( draw.IndexCount > 0 || draw.VertexCount <= 0 ) continue;
			draws[i] = draw with { StartIndex = draw.BaseVertex, IndexCount = draw.VertexCount, BaseVertex = 0, IndexBuffer = (slot, 0) };
		}

		return [.. indexBuffers, new MeshIndexBuffer( vertices, sizeof( uint ), data ) { Generated = true }];
	}

	/// <summary>
	/// The buffer block (<c>VBIBBlockHeader_t</c>): two resource arrays (<c>int offset</c> from the field, <c>uint
	/// count</c>) of <c>RenderMeshBufferData_t</c>, vertex buffers then index buffers.
	/// </summary>
	static (MeshVertexBuffer[] Vertices, MeshIndexBuffer[] Indices) ReadBuffers( ReadOnlySpan<byte> block, string name )
	{
		var vertices = new MeshVertexBuffer[Count( block, 4 )];
		var indices = new MeshIndexBuffer[Count( block, 12 )];
		var vertexStart = Offset( block, 0 );
		var indexStart = Offset( block, 8 );

		for ( int i = 0; i < vertices.Length; i++ )
		{
			var (count, stride, fields, data) = Buffer( block, vertexStart + i * 24, name );
			vertices[i] = new MeshVertexBuffer( count, stride, fields, data );
		}

		for ( int i = 0; i < indices.Length; i++ )
		{
			var (count, stride, _, data) = Buffer( block, indexStart + i * 24, name, index: true );
			indices[i] = new MeshIndexBuffer( count, stride, data );
		}

		return (vertices, indices);
	}

	static int Offset( ReadOnlySpan<byte> block, int field ) => field + BinaryPrimitives.ReadInt32LittleEndian( block[field..] );

	static int Count( ReadOnlySpan<byte> block, int field )
	{
		var count = BinaryPrimitives.ReadUInt32LittleEndian( block[field..] );
		if ( count > 64 ) throw new InvalidDataException( $"{count} buffers in a mesh" );
		return (int)count;
	}

	/// <summary>
	/// <c>RenderMeshBufferData_t</c>: <c>int elementCount, int elementSize</c>, a resource array of 56-byte
	/// <c>RenderMeshInputLayoutField_t</c>s and one of the data, meshopt encoded when it's shorter than count times size.
	/// </summary>
	static (int Count, int Stride, VertexField[] Fields, byte[] Data) Buffer( ReadOnlySpan<byte> block, int at, string name, bool index = false )
	{
		var count = BinaryPrimitives.ReadInt32LittleEndian( block[at..] );
		var stride = BinaryPrimitives.ReadInt32LittleEndian( block[(at + 4)..] );
		var fieldStart = Offset( block, at + 8 );
		var fieldCount = Count( block, at + 12 );
		var dataStart = Offset( block, at + 16 );
		var dataSize = (int)BinaryPrimitives.ReadUInt32LittleEndian( block[(at + 20)..] );
		// An empty buffer (an empty run-time mesh's, 0 x 0) is a valid one with nothing in it
		if ( count < 0 || stride < 0 || (count > 0 && stride == 0) || (long)count * stride > int.MaxValue ) throw new InvalidDataException( $"Mesh {name} has a buffer of {count} x {stride} bytes" );

		var fields = new VertexField[fieldCount];
		for ( int f = 0; f < fieldCount; f++ )
		{
			var field = block[(fieldStart + f * 56)..];
			var semantic = field[..32];
			var length = semantic.IndexOf( (byte)0 );
			fields[f] = new VertexField(
				Encoding.ASCII.GetString( length < 0 ? semantic : semantic[..length] ),
				BinaryPrimitives.ReadInt32LittleEndian( field[32..] ),
				(ColorFormat)BinaryPrimitives.ReadUInt32LittleEndian( field[36..] ),
				BinaryPrimitives.ReadInt32LittleEndian( field[40..] ),
				BinaryPrimitives.ReadInt32LittleEndian( field[44..] ),
				BinaryPrimitives.ReadInt32LittleEndian( field[48..] ) == 1,
				BinaryPrimitives.ReadInt32LittleEndian( field[52..] ) );
		}

		var source = block.Slice( dataStart, dataSize );
		var size = count * stride;
		byte[] data;
		if ( dataSize >= size ) data = source[..size].ToArray();
		else if ( index ) data = Meshopt.DecodeIndexBuffer( source, count, stride );
		else data = Meshopt.DecodeVertexBuffer( source, count, stride );

		return (count, stride, fields, data);
	}
}

/// <summary>
/// A decoded vertex buffer: <paramref name="Count"/> vertices of <paramref name="Stride"/> bytes laid out as
/// <paramref name="Fields"/> say.
/// </summary>
public sealed record MeshVertexBuffer( int Count, int Stride, VertexField[] Fields, byte[] Data )
{
	/// <summary>The field with a semantic (case doesn't matter, as native matches them) and index, or null.</summary>
	public VertexField? Find( string semantic, int index = 0 )
	{
		foreach ( var field in Fields )
		{
			if ( field.SemanticIndex == index && string.Equals( field.Semantic, semantic, StringComparison.OrdinalIgnoreCase ) ) return field;
		}

		return null;
	}

	/// <summary>
	/// Whether normals and tangents are packed in the 32-bit normal field (<c>DeriveVertexBufferLayoutFromFields</c>):
	/// octahedral normal and diamond tangent in 10:10:10:2, or the older 8:8:8:8 encoding.
	/// </summary>
	public bool CompressedTangentFrame => Find( "NORMAL" )?.Format is ColorFormat.R10G10B10A2_UNORM or ColorFormat.R8G8B8A8_UNORM;

	/// <summary>
	/// Whether the normal field holds the older 8:8:8:8 encoding (<c>PackNormal_UBYTE4</c>), which native repacks once it
	/// knows the material's shader can decode the 10:10:10:2 one (<see cref="TangentFrame.RepackLegacy"/>).
	/// </summary>
	public bool LegacyTangentFrame => Find( "NORMAL" )?.Format is ColorFormat.R8G8B8A8_UNORM;

	/// <summary>A float field's values for a vertex (position, normal, uv...), widened to four.</summary>
	public Vector4 Read( VertexField field, int vertex ) => field.Read( Data.AsSpan( vertex * Stride + field.Offset ) );

	/// <inheritdoc/>
	public override string ToString() => $"{Count} x {Stride} ({string.Join( ", ", Fields )})";
}

/// <summary>A decoded index buffer: <paramref name="Count"/> indices of <paramref name="Size"/> bytes (2 or 4).</summary>
public sealed record MeshIndexBuffer( int Count, int Size, byte[] Data )
{
	/// <summary>Made by the reader, not in the file: the counting indices of a mesh's unindexed draws.</summary>
	public bool Generated { get; init; }

	/// <summary>The meshlets the resource compiler cooked for ranges of it (their indices in meshlet order), or null.</summary>
	public CookedMeshlets Meshlets { get; init; }

	/// <summary>An index.</summary>
	public uint this[int i] => Size == 2 ? BinaryPrimitives.ReadUInt16LittleEndian( Data.AsSpan( i * 2 ) ) : BinaryPrimitives.ReadUInt32LittleEndian( Data.AsSpan( i * 4 ) );

	/// <inheritdoc/>
	public override string ToString() => $"{Count} x {Size}";
}

/// <summary>
/// A vertex buffer field (<c>RenderMeshInputLayoutField_t</c>): its semantic name and index, format, offset, slot and
/// whether it steps per instance.
/// </summary>
public readonly record struct VertexField( string Semantic, int SemanticIndex, ColorFormat Format, int Offset, int Slot, bool PerInstance, int InstanceStepRate )
{
	/// <summary>
	/// The semantic as a vertex shader's attribute map names it (<c>SemanticUsageVulkan_t</c>), or null when it's
	/// one no shader reads.
	/// </summary>
	public VertexSemantic? Usage => Semantic.ToUpperInvariant() switch
	{
		"POSITION" => VertexSemantic.Position,
		"BLENDWEIGHT" or "BLENDWEIGHTS" => VertexSemantic.BlendWeight,
		"BLENDINDICES" => VertexSemantic.BlendIndices,
		"NORMAL" => VertexSemantic.Normal,
		"PSIZE" => VertexSemantic.PointSize,
		"TEXCOORD" => VertexSemantic.TexCoord,
		"TANGENT" => VertexSemantic.Tangent,
		"BINORMAL" => VertexSemantic.Binormal,
		"POSITIONT" => VertexSemantic.PositionT,
		"COLOR" => VertexSemantic.Color,
		_ => null,
	};

	/// <summary>The Borealis.Gpu vertex format it's read as, or null for a format there isn't one for.</summary>
	public VertexFormat? VertexFormat => Format switch
	{
		ColorFormat.R32_FLOAT => Gpu.VertexFormat.Float,
		ColorFormat.R32G32_FLOAT => Gpu.VertexFormat.Float2,
		ColorFormat.R32G32B32_FLOAT => Gpu.VertexFormat.Float3,
		ColorFormat.R32G32B32A32_FLOAT => Gpu.VertexFormat.Float4,
		ColorFormat.R16G16_FLOAT => Gpu.VertexFormat.Half2,
		ColorFormat.R16G16B16A16_FLOAT => Gpu.VertexFormat.Half4,
		ColorFormat.R8G8B8A8_UNORM => Gpu.VertexFormat.UByte4Norm,
		ColorFormat.R8G8B8A8_SNORM => Gpu.VertexFormat.Byte4Norm,
		ColorFormat.R8G8B8A8_UINT => Gpu.VertexFormat.UByte4,
		ColorFormat.R10G10B10A2_UNORM => Gpu.VertexFormat.UInt1010102Norm,
		ColorFormat.R16G16_UNORM => Gpu.VertexFormat.UShort2Norm,
		ColorFormat.R32_UINT => Gpu.VertexFormat.UInt,
		ColorFormat.R32G32_UINT => Gpu.VertexFormat.UInt2,
		ColorFormat.R32G32B32_UINT => Gpu.VertexFormat.UInt3,
		ColorFormat.R32G32B32A32_UINT => Gpu.VertexFormat.UInt4,
		_ => null,
	};

	/// <summary>Read a float-valued field (float, half or unorm formats) from a vertex's bytes, widened to four.</summary>
	public Vector4 Read( ReadOnlySpan<byte> at )
	{
		return Format switch
		{
			ColorFormat.R32_FLOAT => new Vector4( F( at, 0 ), 0, 0, 0 ),
			ColorFormat.R32G32_FLOAT => new Vector4( F( at, 0 ), F( at, 1 ), 0, 0 ),
			ColorFormat.R32G32B32_FLOAT => new Vector4( F( at, 0 ), F( at, 1 ), F( at, 2 ), 0 ),
			ColorFormat.R32G32B32A32_FLOAT => new Vector4( F( at, 0 ), F( at, 1 ), F( at, 2 ), F( at, 3 ) ),
			ColorFormat.R16G16_FLOAT => new Vector4( H( at, 0 ), H( at, 1 ), 0, 0 ),
			ColorFormat.R16G16B16A16_FLOAT => new Vector4( H( at, 0 ), H( at, 1 ), H( at, 2 ), H( at, 3 ) ),
			ColorFormat.R8G8B8A8_UNORM => new Vector4( at[0], at[1], at[2], at[3] ) / 255f,
			ColorFormat.R8G8B8A8_UINT => new Vector4( at[0], at[1], at[2], at[3] ),
			_ => throw new NotSupportedException( $"Reading {Format} on the CPU" ),
		};
	}

	static float F( ReadOnlySpan<byte> at, int i ) => BinaryPrimitives.ReadSingleLittleEndian( at[(i * 4)..] );
	static float H( ReadOnlySpan<byte> at, int i ) => (float)BinaryPrimitives.ReadHalfLittleEndian( at[(i * 2)..] );

	/// <inheritdoc/>
	public override string ToString() => $"{Semantic}{SemanticIndex} {Format} @{Offset}";
}

/// <summary>
/// A draw call (<c>CMaterialDrawDescriptor</c>): a range of one index buffer drawn with a material over one or more
/// vertex buffers.
/// </summary>
public sealed record MeshDrawCall
{
	/// <summary>The scene object (<c>CSceneObjectData</c>) of its mesh it's in; almost always 0.</summary>
	public int SceneObject { get; init; }

	/// <summary>The primitive (<c>RenderPrimitiveType_t</c>): triangles for every compiled model.</summary>
	public PrimitiveKind Primitive { get; init; }

	/// <summary>Added to every index.</summary>
	public int BaseVertex { get; init; }

	/// <summary>Vertices the draw's indices reach.</summary>
	public int VertexCount { get; init; }

	/// <summary>The first index.</summary>
	public int StartIndex { get; init; }

	/// <summary>Indices drawn.</summary>
	public int IndexCount { get; init; }

	/// <summary>Instances (0: not instanced).</summary>
	public int InstanceCount { get; init; }

	/// <summary>Texels per unit, for texture streaming.</summary>
	public float UvDensity { get; init; }

	/// <summary>A linear colour multiplied in for static objects (<c>m_vTintColor</c>), (1, 1, 1) for none.</summary>
	public Vector3 Tint { get; init; }

	/// <summary>The material, <c>materials/dev/primary_white.vmat</c>.</summary>
	public string Material { get; init; }

	/// <summary>The mesh's vertex buffers the draw binds, by index, and the byte offset each is bound at.</summary>
	public IReadOnlyList<(int Buffer, int Offset)> VertexBuffers { get; init; }

	/// <summary>The mesh's index buffer the draw reads, and the byte offset it's bound at.</summary>
	public (int Buffer, int Offset) IndexBuffer { get; init; }

	/// <summary>Flags compiled into the draw (<c>RenderMeshDrawPrimitiveFlags_t</c>, from its <c>m_b*</c> keys).</summary>
	public MeshDrawFlags Flags { get; init; }

	/// <summary>
	/// Whether its first vertex buffer packs normals and tangents into one field, which is what native sets
	/// <c>UncompressedTangentFrame</c> from at load (<c>CRenderMesh::CreateBufferResources</c>), whatever the flag says.
	/// </summary>
	public bool CompressedTangentFrame { get; init; }

	/// <summary>Per-draw culling data's index (<c>m_CullDataIndex</c>), -1 for none.</summary>
	public int CullDataIndex { get; init; }

	/// <summary>
	/// The id the mesh compiler kept from the mesh the draw was made from (<c>m_nMeshID</c>), -1 where none is written. A
	/// world aggregate's fragment names its draw by this id (<see cref="ModelFile.AggregateDrawCall"/>).
	/// </summary>
	public int MeshId { get; init; } = -1;

	internal static MeshDrawCall Read( Dictionary<string, object> draw, MeshVertexBuffer[] vertexBuffers, string mesh )
	{
		var buffers = new List<(int, int)>();
		foreach ( var binding in Kv.Objects( draw.Get( "m_vertexBuffers" ) ) )
			buffers.Add( ((int)Kv.Number( binding.Get( "m_hBuffer" ) ), (int)Kv.Number( binding.Get( "m_nBindOffsetBytes" ) )) );

		var index = draw.Get( "m_indexBuffer" ) as Dictionary<string, object>;
		var flags = MeshDrawFlags.None;
		if ( draw.Get( "m_nFlags" ) is { } legacy )
		{
			flags = (MeshDrawFlags)(int)Kv.Number( legacy );
		}
		else
		{
			foreach ( var (key, flag) in FlagKeys )
			{
				if ( draw.Get( key ) is true ) flags |= flag;
			}
		}

		var compressed = false;
		foreach ( var (buffer, _) in buffers )
		{
			if ( (uint)buffer >= (uint)vertexBuffers.Length ) throw new InvalidDataException( $"A draw in {mesh} binds vertex buffer {buffer} of {vertexBuffers.Length}" );
			compressed = vertexBuffers[buffer].CompressedTangentFrame;
		}

		return new MeshDrawCall
		{
			Primitive = Kv.Primitive( draw.Get( "m_nPrimitiveType" ) ),
			BaseVertex = (int)Kv.Number( draw.Get( "m_nBaseVertex" ) ),
			VertexCount = (int)Kv.Number( draw.Get( "m_nVertexCount" ) ),
			StartIndex = (int)Kv.Number( draw.Get( "m_nStartIndex" ) ),
			IndexCount = (int)Kv.Number( draw.Get( "m_nIndexCount" ) ),
			InstanceCount = (int)Kv.Number( draw.Get( "m_nInstanceCount" ) ),
			UvDensity = (float)Kv.Number( draw.Get( "m_flUvDensity" ) ),
			Tint = draw.Get( "m_vTintColor" ) is null ? Vector3.One : Kv.Vector3( draw.Get( "m_vTintColor" ) ),
			Material = draw.Get( "m_material" ) as string,
			VertexBuffers = buffers,
			IndexBuffer = index is null ? (0, 0) : ((int)Kv.Number( index.Get( "m_hBuffer" ) ), (int)Kv.Number( index.Get( "m_nBindOffsetBytes" ) )),
			Flags = flags,
			CompressedTangentFrame = compressed,
			CullDataIndex = (int)Kv.Number( draw.Get( "m_CullDataIndex" ), -1 ),
			MeshId = (int)Kv.Number( draw.Get( "m_nMeshID" ), -1 ),
		};
	}

	static readonly (string Key, MeshDrawFlags Flag)[] FlagKeys =
	[
		("m_bUseShadowFastPath", MeshDrawFlags.ShadowFastPath),
		("m_bUseCompressedNormalTangent", MeshDrawFlags.CompressedNormalTangent),
		("m_bIsNotMatchedToMaterial", MeshDrawFlags.NotMatchedToMaterial),
		("m_bHasBakedLightingFromVertexStream", MeshDrawFlags.BakedLightingFromVertexStream),
		("m_bHasBakedLightingFromLightMap", MeshDrawFlags.BakedLightingFromLightmap),
		("m_bCanBatchWithDynamicShaderConstants", MeshDrawFlags.CanBatchWithDynamicShaderConstants),
		("m_bDrawLast", MeshDrawFlags.DrawLast),
		("m_bHasCullingData", MeshDrawFlags.HasCullingData),
	];

	/// <inheritdoc/>
	public override string ToString() => $"{Material} [{StartIndex}, +{IndexCount}]";
}

/// <summary>A mesh bone: its name, its parent's name and its inverse bind pose in the model's space.</summary>
public readonly record struct MeshBone( string Name, string Parent, Matrix4x4 InverseBindPose );

/// <summary><c>RenderPrimitiveType_t</c>.</summary>
public enum PrimitiveKind
{
#pragma warning disable CS1591
	Points,
	Lines,
	LinesWithAdjacency,
	LineStrip,
	LineStripWithAdjacency,
	Triangles,
	TrianglesWithAdjacency,
	TriangleStrip,
	TriangleStripWithAdjacency,
	Instanced,
	Unknown = 255,
#pragma warning restore CS1591
}

/// <summary><c>RenderMeshDrawPrimitiveFlags_t</c>.</summary>
[Flags]
public enum MeshDrawFlags
{
#pragma warning disable CS1591
	None = 0,
	ShadowFastPath = 0x1,
	CompressedNormalTangent = 0x2,
	NotMatchedToMaterial = 0x8,
	BakedLightingFromVertexStream = 0x10,
	BakedLightingFromLightmap = 0x20,
	CanBatchWithDynamicShaderConstants = 0x40,
	DrawLast = 0x80,
	HasCullingData = 0x1000,
#pragma warning restore CS1591
}

/// <summary>The vertex field formats compiled meshes use (<c>ColorFormat_t</c>, DXGI's numbering).</summary>
public enum ColorFormat : uint
{
#pragma warning disable CS1591
	Unknown = 0,
	R32G32B32A32_FLOAT = 2,
	R32G32B32A32_UINT = 3,
	R32G32B32A32_SINT = 4,
	R32G32B32_FLOAT = 6,
	R32G32B32_UINT = 7,
	R32G32B32_SINT = 8,
	R16G16B16A16_FLOAT = 10,
	R16G16B16A16_UNORM = 11,
	R16G16B16A16_UINT = 12,
	R16G16B16A16_SNORM = 13,
	R16G16B16A16_SINT = 14,
	R32G32_FLOAT = 16,
	R32G32_UINT = 17,
	R32G32_SINT = 18,
	R10G10B10A2_UNORM = 24,
	R10G10B10A2_UINT = 25,
	R11G11B10_FLOAT = 26,
	R8G8B8A8_UNORM = 28,
	R8G8B8A8_UINT = 30,
	R8G8B8A8_SNORM = 31,
	R8G8B8A8_SINT = 32,
	R16G16_FLOAT = 34,
	R16G16_UNORM = 35,
	R16G16_UINT = 36,
	R16G16_SNORM = 37,
	R16G16_SINT = 38,
	R32_FLOAT = 41,
	R32_UINT = 42,
	R32_SINT = 43,
#pragma warning restore CS1591
}
