using Basalt.Gpu;
using Basalt.Source2.Models;
using Basalt.Source2.Shaders;
using System.Linq;

namespace Basalt.Backend;

/// <summary>
/// What a draw binds: its vertex and index buffers, where in them, and how its vertices are laid out.
/// </summary>
internal readonly record struct DrawGeometry( BufferHandle Vertices, long VertexOffset, BufferHandle Indices, long IndexOffset, IndexFormat IndexFormat, int BaseVertex, VertexLayout Layout, bool UncompressedTangentFrame );

/// <summary>
/// Geometry a <see cref="BackendContext"/> can draw: buffers per model mesh and draw call.
/// </summary>
internal interface IBackendGeometry
{
	/// <summary>What draw <paramref name="drawCall"/> of mesh <paramref name="mesh"/> binds.</summary>
	DrawGeometry For( int mesh, int drawCall );
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
	readonly string key;

	public VertexLayout( int stride, Field[] fields )
	{
		Stride = stride;
		Fields = fields;
		key = $"{stride}:{string.Join( ",", fields )}";
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
/// the file. <see cref="Load"/> makes the <see cref="RenderMesh"/> Basalt draws from it: every LOD's draws with the
/// model's materials, its switch distances and bounds.
/// </summary>
/// <remarks>
/// Skinned models draw in their bind pose: their vertices are stored posed that way, and the mesh is given no skinning,
/// so Basalt draws it as a rigid object (<see cref="DrawsBindPose"/>). Morphs aren't applied.
/// </remarks>
public sealed class BackendModel : GpuMesh, IBackendGeometry
{
	readonly BackendDevice device;
	readonly DrawGeometry[][] draws;
	readonly List<BufferHandle> buffers = new();

	/// <summary>The model as read.</summary>
	public ModelFile File { get; }

	/// <summary>Whether it's a skinned model drawn in its bind pose, since Basalt.Gpu doesn't skin yet.</summary>
	public bool DrawsBindPose => File.IsSkinned;

	/// <inheritdoc/>
	public override string Name => File.Name;

	BackendModel( BackendDevice device, ModelFile file )
	{
		this.device = device;
		File = file;
		var gpu = device.Gpu;
		draws = new DrawGeometry[file.Meshes.Count][];

		for ( int m = 0; m < file.Meshes.Count; m++ )
		{
			var mesh = file.Meshes[m];
			var vertexBuffers = new (BufferHandle Buffer, VertexLayout Layout, bool Compressed)[mesh.VertexBuffers.Count];
			for ( int v = 0; v < vertexBuffers.Length; v++ ) vertexBuffers[v] = Upload( gpu, mesh.VertexBuffers[v], $"{file.Name} {mesh.Name} vertices {v}" );

			var indexBuffers = new (BufferHandle Buffer, IndexFormat Format)[mesh.IndexBuffers.Count];
			for ( int i = 0; i < indexBuffers.Length; i++ )
			{
				var source = mesh.IndexBuffers[i];
				var buffer = gpu.CreateBuffer( new BufferInfo { Size = Math.Max( source.Data.Length, 4 ), Usage = BufferUsage.Index, Name = $"{file.Name} {mesh.Name} indices {i}" } );
				if ( source.Data.Length > 0 ) gpu.WriteBuffer( buffer, source.Data );
				buffers.Add( buffer );
				indexBuffers[i] = (buffer, source.Size == 4 ? IndexFormat.UInt32 : IndexFormat.UInt16);
			}

			draws[m] = new DrawGeometry[mesh.DrawCalls.Count];
			for ( int d = 0; d < mesh.DrawCalls.Count; d++ )
			{
				var call = mesh.DrawCalls[d];

				// One vertex stream: every compiled draw binds one buffer (the second stream is Basalt's instance ids)
				var (vertexIndex, vertexOffset) = call.VertexBuffers.Count > 0 ? call.VertexBuffers[0] : (0, 0);
				var vertices = vertexBuffers[vertexIndex];
				var (indexIndex, indexOffset) = call.IndexBuffer;
				var indices = indexBuffers[indexIndex];
				draws[m][d] = new DrawGeometry( vertices.Buffer, vertexOffset, indices.Buffer, indexOffset, indices.Format, call.BaseVertex, vertices.Layout, !vertices.Compressed );
			}
		}
	}

	/// <summary>
	/// A vertex buffer on the GPU. Normals in the older 8:8:8:8 encoding are repacked as 10:10:10:2, as native does for
	/// shaders that decode it (every s&amp;box shader that reads <c>UncompressedTangentFrame</c>).
	/// </summary>
	(BufferHandle, VertexLayout, bool) Upload( Device gpu, MeshVertexBuffer source, string name )
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

		var buffer = gpu.CreateBuffer( new BufferInfo { Size = Math.Max( data.Length, 4 ), Usage = BufferUsage.Vertex, Name = name } );
		if ( data.Length > 0 ) gpu.WriteBuffer( buffer, data );
		buffers.Add( buffer );
		return (buffer, device.Layout( new VertexLayout( source.Stride, fields.ToArray() ) ), source.CompressedTangentFrame);
	}

	DrawGeometry IBackendGeometry.For( int mesh, int drawCall ) => draws[mesh][drawCall];

	/// <summary>
	/// Load a compiled model from <paramref name="device"/>'s content and make the mesh Basalt draws: per LOD, the draws
	/// of every mesh that draws at it and is in <paramref name="meshGroups"/> (by default the model's default mesh groups),
	/// with materials from <paramref name="materialGroup"/>. Null if the model isn't built.
	/// </summary>
	public static RenderMesh Load( BackendDevice device, string path, ulong? meshGroups = null, int materialGroup = 0 )
	{
		if ( device.Content.TryRead( path ) is not { } data ) return null;
		return Create( device, ModelFile.Read( data, device.Content.TryRead ), meshGroups, materialGroup );
	}

	/// <summary>Make the mesh Basalt draws from a model already read (see <see cref="Load"/>).</summary>
	public static RenderMesh Create( BackendDevice device, ModelFile file, ulong? meshGroups = null, int materialGroup = 0 )
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
					var material = device.LoadMaterial( file.MaterialFor( call.Material, materialGroup ) );
					var translucent = RenderMesh.IsTranslucent( material );
					var alphaTest = RenderMesh.IsAlphaTest( material );
					var fastPath = material is { ShadowFastPath: true } && !translucent && !alphaTest;
					Vector3? tint = call.Tint == System.Numerics.Vector3.One ? null : new Vector3( call.Tint.X, call.Tint.Y, call.Tint.Z );
					var lightmapped = (call.Flags & MeshDrawFlags.BakedLightingFromLightmap) != 0;
					list.Add( new RenderMesh.Draw( m, d, call.StartIndex, call.IndexCount, call.VertexCount, (int)call.Primitive, material, translucent, alphaTest, fastPath, lightmapped, tint, call.UvDensity ) );
				}
			}

			lods[lod] = list.ToArray();
		}

		var bounds = new BBox( new Vector3( file.Mins.X, file.Mins.Y, file.Mins.Z ), new Vector3( file.Maxs.X, file.Maxs.Y, file.Maxs.Z ) );
		return new RenderMesh( geometry, bounds, lods, file.LodSwitchDistances.ToArray() );
	}

	/// <summary>Free its buffers, once the GPU is done with them; otherwise they go with the device.</summary>
	public void Free()
	{
		foreach ( var buffer in buffers ) device.Gpu.Destroy( buffer );
		buffers.Clear();
	}
}
