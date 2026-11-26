using HalfEdgeMesh;
using System;
using System.IO;
using N = System.Numerics;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	readonly record struct PatchPoint( Vector3 Position, Vector2 UV, Vector4 Tint, Vector4 Blend, Vector4 Secondary )
	{
		public static PatchPoint operator +( PatchPoint a, PatchPoint b ) => new( a.Position + b.Position, a.UV + b.UV, a.Tint + b.Tint, a.Blend + b.Blend, a.Secondary + b.Secondary );
		public static PatchPoint operator *( PatchPoint a, float b ) => new( a.Position * b, a.UV * b, a.Tint * b, a.Blend * b, a.Secondary * b );
		public static PatchPoint Lerp( PatchPoint a, PatchPoint b, float t ) => new(
			a.Position + (b.Position - a.Position) * t, a.UV + (b.UV - a.UV) * t,
			a.Tint + (b.Tint - a.Tint) * t, a.Blend + (b.Blend - a.Blend) * t, a.Secondary + (b.Secondary - a.Secondary) * t );
		public static PatchPoint Bilinear( PatchPoint a, PatchPoint b, PatchPoint c, PatchPoint d, float x, float y ) => Lerp( Lerp( a, c, y ), Lerp( b, d, y ), x );
	}

	static void BakeSubdivision( PolygonMesh mesh, Element data, ReadOnlySpan<N.Vector4> colors, ReadOnlySpan<N.Vector4> blends, ReadOnlySpan<N.Vector4> secondary, Dictionary<HalfEdgeHandle, TileCornerPaint> sourcePaint )
	{
		if ( data.GetValueOrDefault( "subdivisionData" ) is not Element subdivision || !subdivision.ContainsKey( "subdivisionLevels" ) ) return;
		var levels = subdivision.Numbers<int>( "subdivisionLevels" );
		if ( !levels.ContainsAnyExcept( 0 ) ) return;
		var offsets = new int[levels.Length];
		int total = 0;
		for ( int i = 0; i < levels.Length; i++ )
		{
			offsets[i] = total;
			if ( levels[i] == 0 ) continue;
			if ( levels[i] is < 1 or > 8 ) throw new InvalidDataException( $"Invalid subdivision level {levels[i]}." );
			int width = (1 << (levels[i] - 1)) + 1;
			total = checked(total + width * width);
		}

		var streams = subdivision.GetArray<Element>( "streams" );
		ReadOnlySpan<T> Samples<T>( string name, int index = 0 ) where T : unmanaged
		{
			foreach ( var stream in streams )
				if ( stream.Get<string>( "semanticName" ) == name && stream.Get<int>( "semanticIndex" ) == index )
					return stream.Numbers<T>( "data" );
			return default;
		}
		var displacement = Samples<N.Vector3>( "displacement" );
		var uvs = Samples<N.Vector2>( "texcoord" );
		var tint = Samples<N.Vector4>( "VertexPaintTintColor" );
		var blend = Samples<N.Vector4>( "VertexPaintBlendParams" );
		var second = Samples<N.Vector4>( "VertexPaintBlendParams", 1 );
		if ( second.IsEmpty ) second = Samples<N.Vector4>( "VertexPaintBlendParams1" );
		var cornerData = data.Numbers<int>( "edgeVertexDataIndices" );
		var faces = new List<(FaceHandle Face, VertexHandle[] Vertices, int Level, PatchPoint[][] Patches)>();
		var seams = new List<(FaceHandle Face, VertexHandle Start, VertexHandle End, int A, int B)>();
		int cornerCount = 0;
		foreach ( var face in mesh.FaceHandles )
		{
			int level = levels[cornerData[face.Edge.Index]];
			if ( level == 0 ) continue;
			mesh.GetFaceVerticesConnectedToFace( face, out var edges );
			var corners = new PatchPoint[edges.Length];
			var vertices = new VertexHandle[edges.Length];
			var center = new PatchPoint();
			for ( int i = 0; i < edges.Length; i++ )
			{
				var edge = edges[i];
				var neighbor = edge.OppositeEdge.Face;
				if ( neighbor.IsValid && levels[cornerData[neighbor.Edge.Index]] == 0 )
				{
					var end = edge.OppositeEdge;
					var start = mesh.FindFaceVertexConnectedToVertex( edge.Vertex, neighbor );
					seams.Add( (neighbor, start.Vertex, end.Vertex, cornerData[start.Index], cornerData[end.Index]) );
				}
				vertices[i] = edge.Vertex;
				// Interpolate the author's floating-point paint before quantizing the final corners.
				int index = cornerData[edge.Index];
				corners[i] = new( mesh.GetVertexPosition( edge.Vertex ), mesh.GetTextureCoord( edge ),
					colors.IsEmpty ? Vector4.One : colors[index], blends.IsEmpty ? default : blends[index], secondary.IsEmpty ? default : secondary[index] );
				center += corners[i] * (1f / edges.Length);
			}
			mesh.ComputeFaceNormal( face, out var normal );
			var tangent = normal.Cross( center.Position - (corners[0].Position + corners[1].Position) * 0.5f ).Normal;
			var binormal = tangent.Cross( normal ).Normal;
			int steps = 1 << (level - 1);
			cornerCount = checked(cornerCount + 4 * steps * steps * edges.Length);
			var patches = new PatchPoint[edges.Length][];
			for ( int i = 0; i < edges.Length; i++ )
			{
				var origin = corners[i];
				var right = PatchPoint.Lerp( origin, corners[(i + 1) % edges.Length], 0.5f );
				var top = PatchPoint.Lerp( origin, corners[(i + edges.Length - 1) % edges.Length], 0.5f );
				var patch = patches[i] = new PatchPoint[(steps + 1) * (steps + 1)];
				int offset = offsets[cornerData[edges[i].Index]];
				for ( int y = 0; y <= steps; y++ )
					for ( int x = 0; x <= steps; x++ )
					{
						int index = y * (steps + 1) + x;
						var p = PatchPoint.Bilinear( origin, right, top, center, (float)x / steps, (float)y / steps );
						int sample = offset + index;
						Vector3 delta = displacement.IsEmpty ? default : displacement[sample];
						patch[index] = new( p.Position + binormal * delta.x + tangent * delta.y + normal * delta.z,
							uvs.IsEmpty ? p.UV : uvs[sample], tint.IsEmpty ? p.Tint : tint[sample], blend.IsEmpty ? p.Blend : blend[sample],
							second.IsEmpty ? p.Secondary : second[sample] );
					}
			}
			faces.Add( (face, vertices, level, patches) );
		}

		// Neighboring faces share their new edge vertices. Finish all splits before moving them.
		var assignments = new List<(HalfEdgeHandle Edge, PatchPoint[] Patch, int Index, bool Soft)>( cornerCount );
		foreach ( var (face, vertices, level, patches) in faces )
		{
			int stride = (1 << (level - 1)) + 1;
			mesh.SubdivideFace( face, level, 0, vertices, vertices.Length, out _, ( quad, corners, patch, x, y ) =>
			{
				int start = y * stride + x;
				var points = patches[patch];
				void Corner( int i, int index, bool soft ) => assignments.Add( (mesh.FindFaceVertexConnectedToVertex( corners[i], quad ), points, index, soft) );
				Corner( 0, start, x > 0 );
				Corner( 1, start + 1, y > 0 );
				Corner( 2, start + stride + 1, true );
				Corner( 3, start + stride, true );
			} );
		}
		// Unsubdivided neighbors gain corners along the shared edge. Interpolate their original
		// floating-point paint before packing, just as we do for the subdivided side.
		foreach ( var (face, start, end, a, b) in seams )
		{
			var first = mesh.FindFaceVertexConnectedToVertex( start, face );
			int segments = 1;
			for ( var e = first.NextEdge; e.Vertex != end; e = e.NextEdge )
			{
				if ( e == first || !e.IsValid ) throw new InvalidDataException( "Subdivision lost a boundary vertex." );
				segments++;
			}
			var corner = first;
			for ( int i = 1; i < segments; i++ )
			{
				corner = corner.NextEdge;
				float t = (float)i / segments;
				SetCornerPaint( mesh, corner, colors.IsEmpty ? N.Vector4.One : N.Vector4.Lerp( colors[a], colors[b], t ),
					blends.IsEmpty ? default : N.Vector4.Lerp( blends[a], blends[b], t ), sourcePaint );
				if ( !secondary.IsEmpty ) mesh.SetSecondaryVertexBlend( corner, PackVertexPaint( N.Vector4.Lerp( secondary[a], secondary[b], t ) ) );
			}
		}
		var weld = new HashSet<VertexHandle>();
		foreach ( var (edge, patch, index, soft) in assignments )
		{
			ref readonly var p = ref patch[index];
			if ( !edge.IsValid ) throw new InvalidDataException( "Subdivision produced an invalid face corner." );
			mesh.SetVertexPosition( edge.Vertex, p.Position );
			mesh.SetTextureCoord( edge, p.UV );
			SetCornerPaint( mesh, edge, p.Tint, p.Blend, sourcePaint );
			if ( !secondary.IsEmpty || !second.IsEmpty ) mesh.SetSecondaryVertexBlend( edge, PackVertexPaint( p.Secondary ) );
			if ( soft ) mesh.SetEdgeSmoothing( edge, PolygonMesh.EdgeSmoothMode.Soft );
			weld.Add( edge.Vertex );
		}
		mesh.MergeVerticesWithinDistance( weld.ToArray(), 0.000001f, false, true, out _ );
	}
}
