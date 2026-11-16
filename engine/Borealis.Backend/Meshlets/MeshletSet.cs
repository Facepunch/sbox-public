using System.Runtime.InteropServices;
using V3 = System.Numerics.Vector3;

namespace Borealis.Backend.Meshlets;

/// <summary>
/// A meshlet's bounds and triangles, as the GPU reads it (<see cref="MeshletSet.RecordBytes"/> bytes): its box's centre and
/// half extents in the mesh's space, and its triangles, a run of the range's triangles in meshlet order.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct MeshletRecord
{
	public V3 Center;
	public uint FirstTriangle;
	public V3 Extents;
	public uint TriangleCount;
}

/// <summary>How meshes are cut into meshlets: at most this many vertices and triangles each.</summary>
internal readonly record struct MeshletConfig( int MaxVertices = 64, int MaxTriangles = 124 )
{
	/// <summary>meshoptimizer's defaults, which <c>-meshlets 64x124</c> names.</summary>
	public static MeshletConfig Default => new( 64, 124 );

	/// <summary>
	/// A config from <c>VxT</c> (<c>64x124</c>, <c>32x32</c>); the default for anything else. meshoptimizer takes triangle
	/// counts that are multiples of 4; a meshlet record holds at most 255.
	/// </summary>
	public static MeshletConfig Parse( string text )
	{
		var parts = text?.Split( 'x' );
		return parts is [var v, var t] && int.TryParse( v, out var vertices ) && int.TryParse( t, out var triangles ) && vertices is >= 3 and <= 256 && triangles is >= 4 and <= 252 && triangles % 4 == 0
			? new MeshletConfig( vertices, triangles )
			: Default;
	}

	public override string ToString() => $"{MaxVertices}x{MaxTriangles}";
}

/// <summary>
/// The meshlets of one index range: what Borealis builds at load (<see cref="MeshletBuilder"/>) and a resource compiler
/// could cook into a model or map instead, so the renderer takes either. <see cref="Indices"/> are the range's own index
/// values in meshlet order, each triangle's corners in their order (its first vertex is what the visibility ids name);
/// each meshlet's triangles are a run of them.
/// </summary>
internal sealed class MeshletSet
{
	/// <summary>Bytes of a <see cref="MeshletRecord"/>.</summary>
	public const int RecordBytes = 32;

	public uint[] Indices { get; init; }
	public MeshletRecord[] Meshlets { get; init; }

	/// <summary>Triangles in the range.</summary>
	public int Triangles => Indices.Length / 3;
}
