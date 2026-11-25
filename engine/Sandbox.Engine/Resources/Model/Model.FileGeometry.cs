using Borealis.Source2.Models;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Sandbox;

public partial class Model
{
	// A file model's compiled geometry, read once, for its vertex and index reads on Borealis.Gpu
	static readonly ConditionalWeakTable<Model, ModelFile> fileGeometry = new();

	/// <summary>
	/// On Borealis.Gpu (<c>-borealisgpu</c>) Source 2's render system is the empty one: a file model's buffers are only
	/// descriptions there, with nothing to read back. Its geometry comes from its compiled file instead, as Borealis.Gpu
	/// draws it. Null with Source 2's device, for a model made in code (its buffers keep their contents) or one with no file.
	/// </summary>
	ModelFile FileGeometry()
	{
		if ( IsProcedural || Graphics.BorealisGpuContent is not { } content ) return null;
		var path = ResourcePath;
		if ( string.IsNullOrEmpty( path ) ) return null;
		if ( fileGeometry.TryGetValue( this, out var known ) ) return known;

		ModelFile file;
		try
		{
			file = content.TryRead( path ) is { } data ? ModelFile.Read( data, content.TryRead ) : null;
		}
		catch ( Exception e )
		{
			Log.Warning( $"Reading {path}'s geometry: {e.Message}" );
			file = null;
		}

		if ( file is not null ) fileGeometry.AddOrUpdate( this, file );
		return file;
	}
}

/// <summary>
/// A compiled model's geometry as <see cref="Model.GetVertices"/> and <see cref="Model.GetIndices"/> return it, read the way
/// native's <c>MeshGlue</c> reads its buffers back: LOD 0's meshes, every vertex buffer in turn; position, normal, tangent,
/// colour and two texture coordinates decoded from whatever formats they're stored in; untouched fields zero and colour white.
/// </summary>
internal static class ModelFileGeometry
{
	static IEnumerable<ModelMesh> Lod0( ModelFile file ) => file.Meshes.Where( m => (m.LodMask & 1) != 0 );

	public static Vertex[] Vertices( ModelFile file )
	{
		var count = Lod0( file ).Sum( m => m.VertexBuffers.Sum( b => b.Count ) );
		if ( count == 0 ) return null;

		var vertices = new Vertex[count];
		var at = 0;
		foreach ( var mesh in Lod0( file ) )
		{
			foreach ( var buffer in mesh.VertexBuffers )
			{
				Read( buffer, vertices.AsSpan( at, buffer.Count ) );
				at += buffer.Count;
			}
		}

		return vertices;
	}

	public static uint[] Indices( ModelFile file )
	{
		var count = Lod0( file ).Sum( m => m.IndexBuffers.Sum( b => b.Count ) );
		if ( count == 0 ) return null;

		// As native: each index buffer's indices offset by the vertices of the vertex buffers before the one it pairs with
		var indices = new uint[count];
		var at = 0;
		uint startVertex = 0;
		foreach ( var mesh in Lod0( file ) )
		{
			for ( int b = 0; b < mesh.IndexBuffers.Count; b++ )
			{
				var buffer = mesh.IndexBuffers[b];
				for ( int i = 0; i < buffer.Count; i++ ) indices[at++] = startVertex + buffer[i];
				if ( b < mesh.VertexBuffers.Count ) startVertex += (uint)mesh.VertexBuffers[b].Count;
			}
		}

		return indices;
	}

	/// <summary>
	/// Decode one vertex buffer's fields into <paramref name="vertices"/>, as native's <c>ReadVertexStream</c> does: vertex
	/// <c>v</c> from the buffer's <paramref name="first"/> + <paramref name="which"/>[v] (or + v without a list). Fields the
	/// buffer doesn't carry are left as they are, except colour, which starts white.
	/// </summary>
	public static void Read( MeshVertexBuffer buffer, Span<Vertex> vertices, int first = 0, ReadOnlySpan<uint> which = default )
	{
		var (position, normal, tangent, color, uv0, uv1) = FieldsOf( buffer );

		for ( int v = 0; v < vertices.Length; v++ )
		{
			var source = first + (which.IsEmpty ? v : (int)which[v]);
			var data = buffer.Data.AsSpan( source * buffer.Stride, buffer.Stride );
			ref var vertex = ref vertices[v];
			vertex.Color = Color32.White;

			if ( position is { } p && Readable( p.Format ) ) vertex.Position = Vector3Of( p.Read( data[p.Offset..] ) );
			if ( tangent is { } t && Readable( t.Format ) ) vertex.Tangent = t.Read( data[t.Offset..] );

			if ( normal is { } n )
			{
				var packed = BinaryPrimitives.ReadUInt32LittleEndian( data[n.Offset..] );
				switch ( n.Format )
				{
					case ColorFormat.R8G8B8A8_UNORM:
						{
							var (nrm, tan) = TangentFrame.UnpackLegacy( packed );
							vertex.Normal = nrm;
							vertex.Tangent = tan;
							break;
						}
					case ColorFormat.R10G10B10A2_UNORM:
						{
							var (nrm, tan) = TangentFrame.UnpackOctDiamond( packed );
							vertex.Normal = nrm;
							vertex.Tangent = tan;
							break;
						}
					default:
						if ( Readable( n.Format ) ) vertex.Normal = Vector3Of( n.Read( data[n.Offset..] ) );
						break;
				}
			}

			if ( color is { } c )
			{
				if ( c.Format == ColorFormat.R8G8B8A8_UNORM ) vertex.Color = new Color32( data[c.Offset], data[c.Offset + 1], data[c.Offset + 2], data[c.Offset + 3] );
				else if ( Readable( c.Format ) )
				{
					var value = c.Read( data[c.Offset..] );
					vertex.Color = new Color32( Byte( value.X ), Byte( value.Y ), Byte( value.Z ), Byte( value.W ) );
				}
			}

			if ( uv0 is { } u0 ) vertex.TexCoord0 = TexCoord( u0, data );
			if ( uv1 is { } u1 ) vertex.TexCoord1 = TexCoord( u1, data );
		}
	}

	/// <summary>One draw call of a compiled mesh as native's <c>ReadDrawCall</c> reads it, with indices rebased to the vertices they reach. False for a draw that isn't an indexed triangle list.</summary>
	public static bool TryReadDrawCall( ModelFile file, int mesh, int drawCall, out Vertex[] vertices, out uint[] indices, out VertexStreams streams )
	{
		vertices = null;
		indices = null;
		streams = 0;

		if ( (uint)mesh >= (uint)file.Meshes.Count ) return false;
		var source = file.Meshes[mesh];
		if ( (uint)drawCall >= (uint)source.DrawCalls.Count ) return false;

		var draw = source.DrawCalls[drawCall];
		if ( draw.IndexCount < 3 || draw.Primitive != PrimitiveKind.Triangles || draw.VertexBuffers.Count == 0 ) return false;

		var (indexBuffer, indexBind) = draw.IndexBuffer;
		var (vertexBuffer, vertexBind) = draw.VertexBuffers[0];
		if ( (uint)indexBuffer >= (uint)source.IndexBuffers.Count || (uint)vertexBuffer >= (uint)source.VertexBuffers.Count ) return false;

		var ib = source.IndexBuffers[indexBuffer];
		var vb = source.VertexBuffers[vertexBuffer];
		var first = draw.StartIndex + indexBind / ib.Size;
		var baseVertex = draw.BaseVertex + vertexBind / vb.Stride;
		if ( first < 0 || (long)first + draw.IndexCount > ib.Count ) return false;

		var read = new uint[draw.IndexCount];
		for ( int i = 0; i < read.Length; i++ ) read[i] = ib[first + i];

		var used = (uint[])read.Clone();
		Array.Sort( used );
		var usedCount = 0;
		for ( int i = 0; i < used.Length; i++ )
		{
			if ( usedCount == 0 || used[usedCount - 1] != used[i] ) used[usedCount++] = used[i];
		}

		if ( baseVertex < 0 || (long)baseVertex + used[usedCount - 1] >= vb.Count ) return false;
		for ( int i = 0; i < read.Length; i++ ) read[i] = (uint)Array.BinarySearch( used, 0, usedCount, read[i] );

		vertices = new Vertex[usedCount];
		Read( vb, vertices, baseVertex, used.AsSpan( 0, usedCount ) );
		indices = read;
		streams = Streams( vb );
		return true;
	}

	/// <summary>Which of a vertex's attributes a buffer carries, as native's <c>GetDrawCallStreams</c> tells them.</summary>
	public static VertexStreams Streams( MeshVertexBuffer buffer )
	{
		var (position, normal, tangent, color, uv0, uv1) = FieldsOf( buffer );
		var streams = (VertexStreams)0;
		if ( position is not null ) streams |= VertexStreams.Position;
		if ( normal is not null ) streams |= VertexStreams.Normal;
		if ( color is not null ) streams |= VertexStreams.Color;
		if ( uv0 is not null ) streams |= VertexStreams.TexCoord0;
		if ( uv1 is not null ) streams |= VertexStreams.TexCoord1;

		// A compressed tangent frame packs the tangent in with the normal
		if ( tangent is not null || normal?.Format is ColorFormat.R8G8B8A8_UNORM or ColorFormat.R10G10B10A2_UNORM ) streams |= VertexStreams.Tangent;
		return streams;
	}

	// The formats VertexField.Read decodes
	static bool Readable( ColorFormat format ) => format is ColorFormat.R32_FLOAT or ColorFormat.R32G32_FLOAT or ColorFormat.R32G32B32_FLOAT or ColorFormat.R32G32B32A32_FLOAT
		or ColorFormat.R16G16_FLOAT or ColorFormat.R16G16B16A16_FLOAT or ColorFormat.R8G8B8A8_UNORM or ColorFormat.R8G8B8A8_UINT;

	static (VertexField? Position, VertexField? Normal, VertexField? Tangent, VertexField? Color, VertexField? Uv0, VertexField? Uv1) FieldsOf( MeshVertexBuffer buffer )
	{
		VertexField? position = null, normal = null, tangent = null, color = null, uv0 = null, uv1 = null;
		foreach ( var field in buffer.Fields )
		{
			switch ( field.Semantic.ToUpperInvariant() )
			{
				case "POSITION": position = field; break;
				case "NORMAL": normal = field; break;
				case "TANGENT": tangent = field; break;
				case "COLOR": color = field; break;
				case "TEXCOORD" when TwoComponents( field.Format ):
					// The lowest semantic index is the first coordinate, the next lowest the second
					if ( uv0 is null || field.SemanticIndex < uv0.Value.SemanticIndex )
					{
						if ( uv0 is not null && (uv1 is null || uv1.Value.SemanticIndex > uv0.Value.SemanticIndex) ) uv1 = uv0;
						uv0 = field;
					}
					else if ( uv1 is null || field.SemanticIndex < uv1.Value.SemanticIndex )
					{
						uv1 = field;
					}

					break;
			}
		}

		return (position, normal, tangent, color, uv0, uv1);
	}

	static bool TwoComponents( ColorFormat format ) => format is ColorFormat.R32G32_FLOAT or ColorFormat.R16G16_FLOAT or ColorFormat.R16G16_UNORM or ColorFormat.R16G16_SNORM or ColorFormat.R16G16_UINT or ColorFormat.R16G16_SINT or ColorFormat.R32G32_UINT or ColorFormat.R32G32_SINT;

	// Native's ReadTexcoord: half or unorm pairs, else two floats
	static Vector4 TexCoord( VertexField field, ReadOnlySpan<byte> data )
	{
		var at = data[field.Offset..];
		return field.Format switch
		{
			ColorFormat.R16G16_FLOAT => new Vector4( (float)BinaryPrimitives.ReadHalfLittleEndian( at ), (float)BinaryPrimitives.ReadHalfLittleEndian( at[2..] ), 0, 0 ),
			ColorFormat.R16G16_UNORM => new Vector4( BinaryPrimitives.ReadUInt16LittleEndian( at ) / 65535f, BinaryPrimitives.ReadUInt16LittleEndian( at[2..] ) / 65535f, 0, 0 ),
			_ => new Vector4( BinaryPrimitives.ReadSingleLittleEndian( at ), BinaryPrimitives.ReadSingleLittleEndian( at[4..] ), 0, 0 ),
		};
	}

	static byte Byte( float unit ) => (byte)Math.Clamp( unit * 255f + 0.5f, 0, 255 );

	static Vector3 Vector3Of( System.Numerics.Vector4 v ) => new( v.X, v.Y, v.Z );
}
