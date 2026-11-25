using System;
using HalfEdgeMesh;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	static void BuildGlassPanel( SourceNode source )
	{
		// Hammer exports the first mesh face as the center plane of the intact pane.
		var child = source.Children.FirstOrDefault( n => n.TypeString == "CMapMesh" && n.Meshes.Count > 0 );
		if ( child is null ) return;
		var part = child.Meshes[0];
		var input = part.Mesh;
		var face = input.FaceHandles.FirstOrDefault();
		if ( !face.IsValid || !input.GetFaceVerticesConnectedToFace( face, out var corners ) || corners.Length < 3 ) return;
		var material = source.Entity.GetResource<Material>( "glass_material" ) ?? input.GetFaceMaterial( face );
		var panel = new PolygonMesh();
		input.ComputeFaceNormal( face, out var normal );
		// Thickness is in world units, even if the authored mesh is scaled.
		float thickness = Math.Clamp( source.Entity.GetValue( "glass_thickness", 1f ), 0.2f, 20f );
		var offset = (normal / child.Transform.SafeScale).Normal / child.Transform.SafeScale * (thickness * 0.5f);
		int count = corners.Length;
		var front = new VertexHandle[count];
		var back = new VertexHandle[count];
		var uvs = new Vector2[count];
		var origin = input.GetVertexPosition( corners[0].Vertex );
		Vector3 previous = default;
		float area = 0;
		for ( int i = 0; i < count; i++ )
		{
			var pos = input.GetVertexPosition( corners[i].Vertex );
			var edge = (pos - origin) * child.Transform.Scale;
			area += 0.5f * previous.Cross( edge ).Length;
			previous = edge;
			front[i] = panel.AddVertex( pos + offset );
			back[count - 1 - i] = panel.AddVertex( pos - offset );
			uvs[i] = input.GetTextureCoord( corners[i] );
		}

		// The original shard model uses this density in engine units per cubic inch.
		source.GlassMass = area * thickness * 0.04226f;

		void AddFace( VertexHandle[] vertices, ReadOnlySpan<Vector2> coordinates )
		{
			var side = panel.AddFace( vertices );
			panel.SetFaceMaterial( side, material );
			var edge = side.Edge;
			foreach ( var uv in coordinates )
			{
				panel.SetTextureCoord( edge, uv );
				edge = edge.NextEdge;
			}
		}
		AddFace( front, uvs );
		Array.Reverse( uvs );
		AddFace( back, uvs );
		for ( int i = 0; i < count; i++ )
		{
			int next = (i + 1) % count;
			AddFace( [front[next], front[i], back[count - 1 - i], back[count - 1 - next]],
				[new( 0.01f, 0.01f ), new( 0.01f, 0 ), new( 0, 0 ), new( 0, 0.01f )] );
		}
		panel.SetSmoothingAngle( 0 );
		// ShatterGlass builds its intact hull directly from the front and back vertices.
		string surfaceName = area < 20 ? "glass.shard" : area > 5000 ? "glass.pane" : "glass";
		child.Meshes = [part with
		{
			Mesh = panel,
			Smoothing = 0,
			Physics = SceneMeshPhysics.SingleHull,
			CollisionError = 0,
			CollisionSurface = Surface.FindByName( surfaceName ) ?? Surface.FindByName( "glass" ),
			Shadows = source.Entity.GetValue<bool>( "enable_shadows" )
		}];
	}
}
