using System;
using HalfEdgeMesh;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	readonly record struct PropCullBox( Transform Transform, BBox Local, BBox World, HashSet<int> Targets )
	{
		public bool Matches( int nodeId, BBox bounds ) =>
			(Targets is null || Targets.Contains( nodeId )) &&
			World.Mins.x <= bounds.Maxs.x && World.Maxs.x >= bounds.Mins.x &&
			World.Mins.y <= bounds.Maxs.y && World.Maxs.y >= bounds.Mins.y &&
			World.Mins.z <= bounds.Maxs.z && World.Maxs.z >= bounds.Mins.z;
	}

	static bool CullsProps( MapLoader.ObjectEntry box ) => box.GetValue<int>( "geometry_type" ) is 0 or 1;

	// Bake directly placed props in this world. Instance definitions are shared by placements
	// and must never lose triangles because one placement overlaps a volume.
	static void CullPlacedProps( SourceNode world, HashSet<SourceNode> definitions )
	{
		var boxes = new List<PropCullBox>();
		var props = new List<SourceNode>();
		void Visit( SourceNode node )
		{
			if ( node.Hidden || node.EditorOnly || definitions.Contains( node ) ) return;
			if ( node.Kind == NodeKind.Entity )
			{
				var data = node.Entity;
				if ( data.TypeName == "prop_static" ) props.Add( node );
				if ( data.TypeName == "info_cull_triangles" && CullsProps( data ) )
				{
					var placement = node.Transform.WithScale( 1 );
					// Hammer contracts only the per-vertex test. Candidate bounds use the full box.
					var size = data.GetValue<Vector3>( "box_size" );
					var local = BBox.FromPositionAndSize( Vector3.Zero, size * 0.998f );
					var bounds = BBox.FromPositionAndSize( Vector3.Zero, size ).Transform( placement );
					var targets = data.GetString( "targets" );
					var ids = string.IsNullOrWhiteSpace( targets ) ? null : targets.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries )
						.Select( token => HammerInteger( token, 0 ) ).ToHashSet();
					boxes.Add( new( placement, local, bounds, ids ) );
				}
			}
			foreach ( var child in node.Children ) Visit( child );
		}
		Visit( world );
		if ( boxes.Count == 0 ) return;

		var matching = new List<PropCullBox>();
		foreach ( var prop in props )
		{
			if ( prop.VariableBindings is not null || prop.Connections is not null || !string.IsNullOrEmpty( prop.Entity.TargetName ) ) continue;
			if ( prop.Entity.GetResource<Model>( "model" ) is not { IsError: false } model ) continue;
			var bounds = model.Bounds.Transform( prop.Transform );
			matching.Clear();
			foreach ( var box in boxes )
				if ( box.Matches( prop.NodeId, bounds ) )
					matching.Add( box );
			if ( matching.Count == 0 ) continue;
			// Hammer runs triangle culling on the world mesh list after flattening eligible props.
			if ( !BakesPropToWorld( prop.Entity, model, prop ) ) continue;
			if ( CullPropMesh( prop, model, matching ) is not { } mesh ) continue;
			prop.CulledProp = true;
			prop.Meshes = mesh.FaceHandles.Any()
				? [new( mesh, 0, SceneMeshPhysics.None, HammerRenderTint( ReadPropTint( prop.Entity.GetString( "rendercolor" ), prop.Entity.GetString( "renderamt" ) ) ),
					!prop.Entity.GetValue<bool>( "disableshadows" ), prop.Entity.GetString( "tags" ) )]
				: [];
		}
	}

	static PolygonMesh CullPropMesh( SourceNode prop, Model model, List<PropCullBox> boxes )
	{
		ReadModelChoices( prop.Entity, out var skin, out _ );
		int group = model.GetMaterialGroupIndex( skin );
		var materials = group > 0 ? model.GetMaterials( group ).ToArray() : null;
		var draws = new List<(Sandbox.Vertex[] Vertices, uint[] Indices, VertexStreams Streams, Material Material, bool[] Inside)>();
		bool removed = false;
		foreach ( var (i, d) in PropDrawCalls( model, prop.Entity ) )
		{
			var draw = model.MeshInfo.Meshes[i].DrawCalls[d];
			if ( draw.Indices < 3 ) continue;
			if ( !model.TryReadDrawCall( i, d, out var vertices, out var indices, out var streams ) ) return null;
			var material = draw.Material;
			if ( materials is not null )
			{
				var slot = model.Materials.IndexOf( material );
				if ( (uint)slot < materials.Length ) material = materials[slot] ?? material;
			}
			bool toolsMaterial = material is not null && material.Flags.GetInt( "tools.toolsmaterial" ) != 0;
			var inside = new bool[indices.Length / 3];
			if ( !toolsMaterial )
			{
				var contained = new bool[vertices.Length];
				foreach ( var box in boxes )
				{
					for ( int v = 0; v < vertices.Length; v++ )
						contained[v] = box.Local.Contains( box.Transform.PointToLocal( prop.Transform.PointToWorld( vertices[v].Position ) ), 0 );
					for ( int t = 0; t < inside.Length; t++ )
						inside[t] |= contained[indices[t * 3]] && contained[indices[t * 3 + 1]] && contained[indices[t * 3 + 2]];
				}
				removed |= inside.AsSpan().Contains( true );
			}
			if ( inside.AsSpan().Contains( false ) )
				draws.Add( (vertices, indices, streams, material, inside) );
		}

		// Bounds can overlap without enclosing a triangle. Keep the original model in that case.
		if ( !removed ) return null;
		var mesh = new PolygonMesh();
		var triangle = new VertexHandle[3];
		foreach ( var (vertices, indices, streams, material, inside) in draws )
		{
			var handles = new VertexHandle[vertices.Length];
			for ( int t = 0; t < inside.Length; t++ )
			{
				if ( inside[t] ) continue;
				var corners = indices.AsSpan( t * 3, 3 );
				for ( int j = 0; j < 3; j++ )
				{
					uint index = corners[j];
					if ( !handles[index].IsValid ) handles[index] = mesh.AddVertex( vertices[index].Position );
					triangle[j] = handles[index];
				}
				var face = mesh.AddFace( triangle );
				if ( !face.IsValid ) return null;
				mesh.SetFaceMaterial( face, material );
				var edge = face.Edge;
				for ( int j = 0; j < 3; j++, edge = edge.NextEdge )
				{
					var v = vertices[corners[j]];
					mesh.SetTextureCoord( edge, new( v.TexCoord0.x, v.TexCoord0.y ) );
					mesh.SetVertexColor( edge, (streams & VertexStreams.Color) == 0 ? Color32.White : v.Color );
					if ( (streams & VertexStreams.Normal) != 0 ) mesh.SetCornerNormal( edge, v.Normal );
				}
			}
		}
		return mesh;
	}
}
