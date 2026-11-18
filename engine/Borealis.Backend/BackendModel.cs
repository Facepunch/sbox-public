using Borealis.Gpu;
using Borealis.Source2.Models;
using Borealis.Source2.Shaders;
using System.Linq;

namespace Borealis.Backend;

/// <summary>
/// What a draw binds: its vertex and index buffers, where in them, and how its vertices are laid out; and where the
/// same geometry is in <see cref="GpuDrivenTables"/>' pages, when it's there.
/// </summary>
internal readonly record struct DrawGeometry( BufferHandle Vertices, long VertexOffset, BufferHandle Indices, long IndexOffset, IndexFormat IndexFormat, int BaseVertex, VertexLayout Layout, bool UncompressedTangentFrame, PooledGeometry? Pooled = null )
{
	/// <summary>The same vertices' positions alone, when the model has them (<see cref="BackendDevice.PositionStreams"/>).</summary>
	public BufferHandle Positions { get; init; }

	/// <summary>Where the draw's vertex buffer offset lands in <see cref="Positions"/>.</summary>
	public long PositionOffset { get; init; }

	/// <summary>The positions' layout: one 12-byte float position.</summary>
	public VertexLayout PositionLayout { get; init; }

	/// <summary>This draw fetching only positions, or itself when there's no positions-only copy.</summary>
	public DrawGeometry PositionsOnly => Positions.IsValid ? this with { Vertices = Positions, VertexOffset = PositionOffset, Layout = PositionLayout, Positions = default } : this;
}

/// <summary>
/// Geometry a <see cref="BackendContext"/> can draw: buffers per model mesh and draw call.
/// </summary>
internal interface IBackendGeometry
{
	/// <summary>What draw <paramref name="drawCall"/> of mesh <paramref name="mesh"/> binds.</summary>
	DrawGeometry For( int mesh, int drawCall );
}

/// <summary>
/// One vertex buffer of a skinned model mesh, as compute skinning (<c>skinning_cs</c>) reads it: where its vertices go in
/// the mesh's block of the vertex cache, and native's <c>VertexBufferLayout_t</c> (<c>DeriveVertexBufferLayoutFromFields</c>),
/// with -1 for a field it doesn't have.
/// </summary>
internal readonly record struct SkinnedVertexBuffer( BufferHandle Buffer, int VertexCount, int CacheOffset, int Stride, int BoneIndexOffset, int BoneIndexBits,
	int BoneWeightOffset, int NormalOffset, int TangentOffset, bool CompressedTangentFrame )
{
	/// <summary>Native's layout of a vertex buffer's fields for skinning.</summary>
	public static SkinnedVertexBuffer From( BufferHandle buffer, MeshVertexBuffer source, int cacheOffset )
	{
		int boneIndices = -1, boneIndexBits = 8, boneWeights = -1, normal = 0, tangent = -1;
		var compressed = false;
		foreach ( var field in source.Fields )
		{
			switch ( field.Semantic.ToUpperInvariant() )
			{
				case "BLENDINDICES":
					boneIndices = field.Offset;
					if ( field.Format is ColorFormat.R16G16B16A16_SINT or ColorFormat.R16G16_SINT or ColorFormat.R16G16B16A16_UINT or ColorFormat.R16G16_UINT ) boneIndexBits = 16;
					else if ( field.Format == ColorFormat.R10G10B10A2_UINT ) boneIndexBits = 10;
					break;
				case "BLENDWEIGHT" or "BLENDWEIGHTS":
					boneWeights = field.Offset;
					break;
				case "NORMAL":
					normal = field.Offset;
					compressed = field.Format is ColorFormat.R8G8B8A8_UNORM or ColorFormat.R10G10B10A2_UNORM;
					break;
				case "TANGENT":
					tangent = field.Offset;
					break;
			}
		}

		return new SkinnedVertexBuffer( buffer, source.Count, cacheOffset, source.Stride, boneIndices, boneIndexBits, boneWeights, normal, tangent, compressed );
	}
}

/// <summary>
/// A draw of skinned vertices from the vertex cache: its geometry with the cache's layout (<see cref="VertexLayout.VertexCache"/>),
/// and where its two vertex id streams start - its first vertex among the mesh's draws, and its vertex buffer's place in the
/// mesh's block of the cache (native's <c>BindModelDrawCallVertexIds</c>).
/// </summary>
internal readonly record struct VertexCacheDraw( DrawGeometry Geometry, int FirstVertexId, int FirstCacheVertex );

/// <summary>
/// Geometry with skinned meshes: their vertex buffers as compute skinning reads them, and their draws from the vertex cache.
/// </summary>
internal interface ISkinnedGeometry
{
	/// <summary>A skinned mesh's vertex buffers, in order; empty for a mesh that isn't skinned.</summary>
	SkinnedVertexBuffer[] SkinnedBuffers( int mesh );

	/// <summary>How a skinned mesh's draw reads the vertex cache, or null when the mesh isn't skinned.</summary>
	VertexCacheDraw? VertexCacheFor( int mesh, int drawCall );
}

/// <summary>
/// A vertex buffer's layout: its stride and its fields by the semantic shaders match them by. Equal layouts compare equal,
/// so pipelines are made once per variant and layout.
/// </summary>
internal sealed class VertexLayout : IEquatable<VertexLayout>
{
	public readonly record struct Field( VertexSemantic Semantic, int Index, VertexFormat Format, int Offset );

	public int Stride { get; }
	public Field[] Fields { get; }

	/// <summary>
	/// Drawn from the vertex cache: streams 2 and 3 are vertex ids (<c>TEXCOORD14</c>, <c>TEXCOORD15</c>), as native's
	/// <c>INPUT_LAYOUT_VARIATION_STREAM1_INSTANCEID_MORPH_VERT_ID</c> layouts read them, and the default stream moves to 4.
	/// </summary>
	public bool VertexCache { get; }

	readonly string key;

	public VertexLayout( int stride, Field[] fields, bool vertexCache = false )
	{
		Stride = stride;
		Fields = fields;
		VertexCache = vertexCache;
		key = $"{stride}:{string.Join( ",", fields )}{(vertexCache ? ":cache" : "")}";
	}

	/// <summary>The field a shader input reads, or null when the mesh has none (it reads the default stream).</summary>
	public Field? Find( VertexSemantic semantic, int index )
	{
		foreach ( var field in Fields )
		{
			if ( field.Semantic == semantic && field.Index == index ) return field;
		}

		return null;
	}

	public bool Equals( VertexLayout other ) => other is not null && key == other.key;
	public override bool Equals( object obj ) => Equals( obj as VertexLayout );
	public override int GetHashCode() => key.GetHashCode();
	public override string ToString() => key;
}

/// <summary>
/// A compiled s&amp;box model (<c>.vmdl_c</c>) on the GPU: every mesh's decoded vertex and index buffers, as they are in
/// the file. <see cref="Load"/> makes the <see cref="RenderMesh"/> Borealis draws from it: every LOD's draws with the
/// model's materials, its switch distances and bounds.
/// </summary>
/// <remarks>
/// Skinned meshes are given their skinning as native's model gives it (<c>RenderTools.GetModelMeshSkinning</c>): bones,
/// inverse bind poses, the model bone each is and the bind pose. Borealis then poses them by bones, in the vertex shader
/// with one weight and by compute skinning into the vertex cache with more, as native does. Morphs
/// aren't applied.
/// </remarks>
public sealed class BackendModel : GpuMesh, IBackendGeometry, ISkinnedGeometry
{
	readonly BackendDevice device;
	readonly DrawGeometry[][] draws;

	/// <summary>Per draw, whether its meshlets have been asked for (<see cref="FirstMeshletRequest"/>).</summary>
	readonly int[][] meshletRequested;
	readonly SkinnedVertexBuffer[][] skinnedBuffers;
	readonly VertexCacheDraw?[][] vertexCacheDraws;
	readonly List<BufferHandle> buffers = new();

	// Its vertices and indices in the GPU-driven pages (slot, bytes), given back when it's freed
	readonly List<(int Slot, int Bytes)> pageRanges = new();

	// The materials its draws hold (BackendDevice.ModelMaterial), let go when it's freed
	readonly List<string> materialPaths = new();

	/// <summary>Whether this is the first ask for a draw's meshlets: a shadow view asks for a static mesh draw's once.</summary>
	internal bool FirstMeshletRequest( int mesh, int call )
		=> (uint)mesh < (uint)meshletRequested.Length && (uint)call < (uint)meshletRequested[mesh].Length && System.Threading.Interlocked.Exchange( ref meshletRequested[mesh][call], 1 ) == 0;

	/// <summary>The model as read.</summary>
	public ModelFile File { get; }

	/// <inheritdoc/>
	public override string Name => File.Name;

	BackendModel( BackendDevice device, ModelFile file )
	{
		this.device = device;
		File = file;
		var gpu = device.Gpu;
		draws = new DrawGeometry[file.Meshes.Count][];
		meshletRequested = new int[file.Meshes.Count][];
		skinnedBuffers = new SkinnedVertexBuffer[file.Meshes.Count][];
		vertexCacheDraws = new VertexCacheDraw?[file.Meshes.Count][];

		// The vertex ids its vertex-cache draws read, made here rather than while a frame records
		if ( file.IsSkinned ) _ = device.VertexIds;

		for ( int m = 0; m < file.Meshes.Count; m++ )
		{
			var mesh = file.Meshes[m];
			var skinned = mesh.IsSkinned;

			// Static meshes also go in the GPU-driven pages, when the device keeps them (skinned ones draw the classic way)
			var tables = skinned ? null : device.GpuDrivenTables;
			var vertexBuffers = new (BufferHandle Buffer, long Offset, VertexLayout Layout, bool Compressed, int PageSlot)[mesh.VertexBuffers.Count];
			for ( int v = 0; v < vertexBuffers.Length; v++ ) vertexBuffers[v] = Upload( gpu, mesh.VertexBuffers[v], $"{file.Name} {mesh.Name} vertices {v}", skinned, tables );

			// Positions alone, for depth-only draws that read nothing else (skinned meshes draw depth from the vertex cache)
			var positions = new (BufferHandle Buffer, long Offset, int Slot)[vertexBuffers.Length];
			for ( int v = 0; v < vertexBuffers.Length; v++ )
			{
				positions[v] = (default, 0, -1);
				if ( device.PositionStreams && !skinned ) positions[v] = UploadPositions( gpu, mesh.VertexBuffers[v], $"{file.Name} {mesh.Name} positions {v}", tables );
			}

			// Compute skinning reads a skinned mesh's vertex buffers by byte address, and writes each into the mesh's block
			// of the vertex cache after the ones before it (CRenderMesh's m_VBSizeOffsets)
			skinnedBuffers[m] = [];
			if ( skinned )
			{
				skinnedBuffers[m] = new SkinnedVertexBuffer[vertexBuffers.Length];
				var cacheOffset = 0;
				for ( int v = 0; v < vertexBuffers.Length; v++ )
				{
					skinnedBuffers[m][v] = SkinnedVertexBuffer.From( vertexBuffers[v].Buffer, mesh.VertexBuffers[v], cacheOffset );
					cacheOffset += mesh.VertexBuffers[v].Count;
				}
			}

			// Indices likewise: in a page of their size when the device keeps them, which the classic path binds too
			var indexBuffers = new (BufferHandle Buffer, long Offset, IndexFormat Format, BufferHandle Page, int PageSlot, uint PageFirst)[mesh.IndexBuffers.Count];
			for ( int i = 0; i < indexBuffers.Length; i++ )
			{
				var source = mesh.IndexBuffers[i];
				var format = source.Size == 4 ? IndexFormat.UInt32 : IndexFormat.UInt16;
				if ( tables is not null )
				{
					var (page, slot, first) = tables.AddIndices( source.Data, format );
					pageRanges.Add( (slot, source.Data.Length) );
					indexBuffers[i] = (page, first * (long)source.Size, format, page, slot, first);
					continue;
				}

				var buffer = gpu.CreateBuffer( new BufferInfo { Size = Math.Max( source.Data.Length, 4 ), Usage = BufferUsage.Index, Name = $"{file.Name} {mesh.Name} indices {i}" } );
				if ( source.Data.Length > 0 ) gpu.WriteBuffer( buffer, source.Data );
				buffers.Add( buffer );
				indexBuffers[i] = (buffer, 0, format, default, -1, 0);
			}

			draws[m] = new DrawGeometry[mesh.DrawCalls.Count];
			meshletRequested[m] = new int[mesh.DrawCalls.Count];
			vertexCacheDraws[m] = new VertexCacheDraw?[mesh.DrawCalls.Count];
			var firstVertexId = 0;
			for ( int d = 0; d < mesh.DrawCalls.Count; d++ )
			{
				var call = mesh.DrawCalls[d];

				// One vertex stream: every compiled draw binds one buffer (the second stream is Borealis's instance ids)
				var (vertexIndex, vertexOffset) = call.VertexBuffers.Count > 0 ? call.VertexBuffers[0] : (0, 0);
				var vertices = vertexBuffers[vertexIndex];
				var (indexIndex, indexOffset) = call.IndexBuffer;
				var indices = indexBuffers[indexIndex];

				// The positions-only copy: in the GPU-driven pages when the model's there, so both paths read the same bytes
				var position = positions[vertexIndex];
				var positionOffset = position.Offset + vertexOffset / Math.Max( vertices.Layout.Stride, 1 ) * PositionStride;

				PooledGeometry? pooled = null;
				if ( tables is not null && indices.Page.IsValid && vertices.PageSlot >= 0 )
				{
					var vertexCount = (int)((mesh.VertexBuffers[vertexIndex].Data.Length - vertexOffset) / Math.Max( vertices.Layout.Stride, 1 ));
					var record = tables.Geometry( vertices.PageSlot, vertices.Offset + vertexOffset, vertices.Layout, vertexCount );
					var positionsRecord = position.Slot >= 0 ? tables.Geometry( position.Slot, positionOffset, device.Layout( PositionLayout ), vertexCount ) : 0u;
					var indexSize = indices.Format == IndexFormat.UInt32 ? 4 : 2;
					pooled = new PooledGeometry( record, indices.Page, indices.PageSlot, indices.PageFirst + (uint)(indexOffset / indexSize), indices.Format, positionsRecord, vertexCount );
				}

				draws[m][d] = new DrawGeometry( vertices.Buffer, vertices.Offset + vertexOffset, indices.Buffer, indices.Offset + indexOffset, indices.Format, call.BaseVertex, vertices.Layout, !vertices.Compressed, pooled );
				if ( position.Buffer.IsValid )
				{
					draws[m][d] = draws[m][d] with
					{
						Positions = position.Buffer,
						PositionOffset = positionOffset,
						PositionLayout = device.Layout( PositionLayout ),
					};
				}

				// From the cache: vertex ids from the draw's first vertex among the mesh's draws, and from its buffer's block
				if ( skinned )
				{
					var cacheLayout = device.Layout( new VertexLayout( vertices.Layout.Stride, vertices.Layout.Fields, vertexCache: true ) );
					vertexCacheDraws[m][d] = new VertexCacheDraw( draws[m][d] with { Layout = cacheLayout }, firstVertexId, skinnedBuffers[m][vertexIndex].CacheOffset );
				}

				firstVertexId += call.VertexCount;
			}
		}
	}

	/// <summary>
	/// A vertex buffer on the GPU, and where it starts there: in a GPU-driven page when <paramref name="tables"/> is given,
	/// which both paths draw from, else a buffer of its own. Normals in the older
	/// 8:8:8:8 encoding are repacked as 10:10:10:2, as native does for shaders that decode it (every s&amp;box shader that
	/// reads <c>UncompressedTangentFrame</c>). Skinned meshes' buffers are storage buffers too, which compute skinning reads.
	/// </summary>
	(BufferHandle Buffer, long Offset, VertexLayout Layout, bool Compressed, int PageSlot) Upload( Device gpu, MeshVertexBuffer source, string name, bool storage, GpuDrivenTables tables )
	{
		var data = source.Data;
		var fields = new List<VertexLayout.Field>();
		foreach ( var field in source.Fields )
		{
			var format = field.Format;
			if ( string.Equals( field.Semantic, "NORMAL", StringComparison.OrdinalIgnoreCase ) && format == ColorFormat.R8G8B8A8_UNORM )
			{
				data = (byte[])data.Clone();
				format = TangentFrame.RepackLegacy( data, source.Count, source.Stride, field.Offset );
			}

			if ( field.Usage is not { } semantic || (field with { Format = format }).VertexFormat is not { } vertexFormat || field.PerInstance ) continue;
			fields.Add( new VertexLayout.Field( semantic, field.SemanticIndex, vertexFormat, field.Offset ) );
		}

		var layout = device.Layout( new VertexLayout( source.Stride, fields.ToArray() ) );

		// In a GPU-driven page, which the classic path binds too: one copy for both
		if ( tables is not null )
		{
			var (page, offset, slot) = tables.AddVertexStream( data );
			pageRanges.Add( (slot, data.Length) );
			return (page, offset, layout, source.CompressedTangentFrame, slot);
		}

		var buffer = gpu.CreateBuffer( new BufferInfo { Size = Math.Max( data.Length, 4 ), Usage = storage ? BufferUsage.Vertex | BufferUsage.Storage : BufferUsage.Vertex, Name = name } );
		if ( data.Length > 0 ) gpu.WriteBuffer( buffer, data );
		buffers.Add( buffer );
		return (buffer, 0, layout, source.CompressedTangentFrame, -1);
	}

	internal const int PositionStride = 12;
	internal static readonly VertexLayout PositionLayout = new( PositionStride, [new VertexLayout.Field( VertexSemantic.Position, 0, VertexFormat.Float3, 0 )] );

	/// <summary>
	/// A vertex buffer's positions as three floats each, or nothing when it has none: in a GPU-driven vertex page when
	/// <paramref name="tables"/> is given (one copy for both paths, with its slot), else a buffer of its own.
	/// </summary>
	(BufferHandle Buffer, long Offset, int Slot) UploadPositions( Device gpu, MeshVertexBuffer source, string name, GpuDrivenTables tables )
	{
		if ( source.Find( "POSITION" ) is not { } field || source.Count == 0 ) return (default, 0, -1);
		var data = new byte[source.Count * PositionStride];
		if ( field.Format == ColorFormat.R32G32B32_FLOAT && source.Stride >= field.Offset + PositionStride )
		{
			// Float3 positions (nearly every model): copied as they are, without decoding each vertex
			var from = source.Data.AsSpan();
			for ( int v = 0; v < source.Count; v++ ) from.Slice( v * source.Stride + field.Offset, PositionStride ).CopyTo( data.AsSpan( v * PositionStride ) );
		}
		else
		{
			var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( data.AsSpan() );
			for ( int v = 0; v < source.Count; v++ )
			{
				var p = source.Read( field, v );
				floats[v * 3] = p.X;
				floats[v * 3 + 1] = p.Y;
				floats[v * 3 + 2] = p.Z;
			}
		}

		System.Threading.Interlocked.Add( ref device.PositionStreamBytes, data.Length );
		if ( tables is not null )
		{
			var placed = tables.AddVertexStream( data );
			pageRanges.Add( (placed.Slot, data.Length) );
			return placed;
		}

		var buffer = gpu.CreateBuffer( new BufferInfo { Size = data.Length, Usage = BufferUsage.Vertex, Name = name } );
		gpu.WriteBuffer( buffer, data );
		buffers.Add( buffer );
		return (buffer, 0, -1);
	}

	DrawGeometry IBackendGeometry.For( int mesh, int drawCall ) => draws[mesh][drawCall];

	SkinnedVertexBuffer[] ISkinnedGeometry.SkinnedBuffers( int mesh ) => (uint)mesh < (uint)skinnedBuffers.Length ? skinnedBuffers[mesh] : [];

	VertexCacheDraw? ISkinnedGeometry.VertexCacheFor( int mesh, int drawCall ) => vertexCacheDraws[mesh][drawCall];

	/// <summary>
	/// What native's model gives Borealis to skin a mesh with (<c>RenderTools.GetModelMeshSkinning</c>): its bones' inverse bind poses
	/// and the model bones they are, its weights per vertex and its vertices across its buffers. Null when it isn't skinned,
	/// or has no bone table, as native writes nothing for a mesh without one.
	/// </summary>
	static RenderMesh.MeshSkin Skin( ModelFile file, int mesh )
	{
		var source = file.Meshes[mesh];
		if ( !source.IsSkinned ) return null;

		int[] masters;
		try
		{
			masters = file.MeshBoneToModelBone( mesh );
		}
		catch ( System.IO.InvalidDataException )
		{
			return null;
		}

		return new RenderMesh.MeshSkin
		{
			BlendWeightCount = source.BoneWeightCount,
			VertexCount = source.VertexBuffers.Sum( x => x.Count ),
			InverseBindPoses = source.Bones.Select( x => Affine( x.InverseBindPose ) ).ToArray(),
			MasterBones = masters,
			Morphs = source.Morphs is { FlexDescs.Count: > 0, Controllers.Count: > 0 },
		};
	}

	/// <summary>A row-vector affine matrix as <c>matrix3x4_t</c>: rows of the transposed rotation, translation in w.</summary>
	static Matrix3x4 Affine( in System.Numerics.Matrix4x4 m ) => new()
	{
		Row0 = new Vector4( m.M11, m.M21, m.M31, m.M41 ),
		Row1 = new Vector4( m.M12, m.M22, m.M32, m.M42 ),
		Row2 = new Vector4( m.M13, m.M23, m.M33, m.M43 ),
	};

	/// <summary>
	/// Load a compiled model from <paramref name="device"/>'s content and make the mesh Borealis draws: per LOD, the draws
	/// of every mesh that draws at it and is in <paramref name="meshGroups"/> (by default the model's default mesh groups),
	/// with materials from <paramref name="materialGroup"/>. Null if the model isn't built.
	/// </summary>
	public static RenderMesh Load( BackendDevice device, string path, ulong? meshGroups = null, int materialGroup = 0 )
	{
		if ( device.Content.TryRead( path ) is not { } data ) return null;
		return Create( device, ModelFile.Read( data, device.Content.TryRead ), meshGroups, materialGroup );
	}

	/// <summary>
	/// Make the mesh Borealis draws from a model already read (see <see cref="Load"/>). <paramref name="drawFastPath"/> takes
	/// each draw's depth fast path from its own flag (<c>m_bUseShadowFastPath</c>) as well as its material, as native's mesh
	/// system sets it for a model made at run time (a water surface, which moves its vertices in its vertex shader, has it off).
	/// </summary>
	public static RenderMesh Create( BackendDevice device, ModelFile file, ulong? meshGroups = null, int materialGroup = 0, bool drawFastPath = false )
	{
		var geometry = new BackendModel( device, file );
		var mask = meshGroups ?? file.DefaultMeshGroupMask;
		var lods = new RenderMesh.Draw[file.LodCount][];

		for ( int lod = 0; lod < lods.Length; lod++ )
		{
			var list = new List<RenderMesh.Draw>();
			for ( int m = 0; m < file.Meshes.Count; m++ )
			{
				var mesh = file.Meshes[m];
				if ( !file.DrawsAtLod( m, lod ) || (mesh.MeshGroupMask & mask) == 0 ) continue;

				for ( int d = 0; d < mesh.DrawCalls.Count; d++ )
				{
					var call = mesh.DrawCalls[d];
					var materialPath = file.MaterialFor( call.Material, materialGroup );
					var material = device.ModelMaterial( materialPath );
					geometry.materialPaths.Add( materialPath );
					var translucent = RenderMesh.IsTranslucent( material );
					var alphaTest = RenderMesh.IsAlphaTest( material );
					var fastPath = material is { ShadowFastPath: true } && !translucent && !alphaTest && (!drawFastPath || (call.Flags & MeshDrawFlags.ShadowFastPath) != 0);
					Vector3? tint = call.Tint == System.Numerics.Vector3.One ? null : new Vector3( call.Tint.X, call.Tint.Y, call.Tint.Z );
					var lightmapped = (call.Flags & MeshDrawFlags.BakedLightingFromLightmap) != 0;
					list.Add( new RenderMesh.Draw( m, d, call.StartIndex, call.IndexCount, call.VertexCount, (int)call.Primitive, material, translucent, alphaTest, fastPath, lightmapped, tint, call.UvDensity ) );
				}
			}

			lods[lod] = list.ToArray();
		}

		var bounds = new BBox( new Vector3( file.Mins.X, file.Mins.Y, file.Mins.Z ), new Vector3( file.Maxs.X, file.Maxs.Y, file.Maxs.Z ) );
		var renderMesh = new RenderMesh( geometry, bounds, lods, file.LodSwitchDistances.ToArray() );

		// Skinning, and the bind pose in the model's space, where an object with no bones of its own is posed
		var skins = new RenderMesh.MeshSkin[file.Meshes.Count];
		for ( int m = 0; m < skins.Length; m++ ) skins[m] = Skin( file, m );
		var bindPose = file.Skeleton.BindPose().Select( x => Affine( x ) ).ToArray();
		renderMesh.SetSkinning( skins, bindPose );
		return renderMesh;
	}

	/// <summary>Free its buffers, once the GPU is done with them; otherwise they go with the device.</summary>
	public void Free()
	{
		foreach ( var buffer in buffers ) device.Gpu.Destroy( buffer );
		buffers.Clear();
		if ( device.GpuDrivenTables is { } tables )
		{
			foreach ( var (slot, bytes) in pageRanges ) tables.ReleaseRange( slot, bytes );
		}

		pageRanges.Clear();
		foreach ( var path in materialPaths ) device.ReleaseModelMaterial( path );
		materialPaths.Clear();
	}
}
