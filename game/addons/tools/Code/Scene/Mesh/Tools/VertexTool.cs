
namespace Editor.MeshEditor;

/// <summary>
/// Select and edit vertices.
/// </summary>
[Title( "Vertex Tool" )]
[Icon( "meshtools/sub-tools/vertex_tool.png" )]
[Alias( "tools.vertex-tool" )]
[Group( "1" )]
public sealed partial class VertexTool( MeshTool tool ) : SelectionTool<MeshVertex>( tool )
{
	public override bool DrawVertices => true;

	public override void BuildSceneContextMenu( Menu menu, Ray ray, SceneTraceResult? trace )
	{
		base.BuildSceneContextMenu( menu, ray, trace );

		int count = Selection.OfType<MeshVertex>().Count( x => x.IsValid() );
		if ( count == 0 ) return;

		menu.AddSeparator();

		var ops = menu.AddMenu( "Vertex Operations", "build" );
		AddMenuOption( ops, "Merge Verts", "meshtools/vertex_tools/merge.png", "mesh.merge", count > 1 );
		AddMenuOption( ops, "Connect Vertex Path", "meshtools/vertex_tools/connect.png", "mesh.connect", count > 1 );
		AddMenuOption( ops, "Bevel Verts", "meshtools/vertex_tools/bevel.png", "mesh.bevel", true );

		var sel = menu.AddMenu( "Vertex Selection", "select_all" );
		AddMenuOption( sel, "Select Loop", "all_out", "mesh.select-loop", count > 1 );
		AddMenuOption( sel, "Invert Selection", "swap_vert", InvertCurrentSelection, "mesh.invert-selection", true );
		sel.AddOption( "Select All", "select_all", () => InvokeShortcut( "mesh.select-all" ), "mesh.select-all" );
	}

	public override void OnUpdate()
	{
		base.OnUpdate();

		using var scope = Gizmo.Scope( "VertexTool" );

		var closestVertex = MeshTrace.GetClosestVertex( 8, out var closestFace );
		if ( closestVertex.IsValid() )
			Gizmo.Hitbox.TrySetHovered( closestVertex.PositionWorld );
		else if ( Gizmo.IsDoubleClicked && closestFace.IsValid() && MeshTrace.TraceFace( out var hitPosition ).IsValid() )
			Gizmo.Hitbox.TrySetHovered( hitPosition );

		if ( Gizmo.IsHovered && Tool.MoveMode.AllowSceneSelection && !IsLassoSelecting )
		{
			SelectVertex( closestVertex );
		}

		UpdateMoveMode();
		DrawSelectedElements();

		if ( ShowSelectionBounds )
			DrawBounds();
	}

	private void DrawBounds()
	{
		using ( Gizmo.Scope( "Vertex Size" ) )
		{
			var box = CalculateSelectionBounds();
			DimensionDisplay.DrawBounds( box );
		}
	}

	protected override IEnumerable<MeshVertex> ConvertSelectionToCurrentType()
	{
		foreach ( var face in Selection.OfType<MeshFace>() )
		{
			if ( !face.IsValid() )
				continue;

			var mesh = face.Component.Mesh;
			mesh.GetVerticesConnectedToFace( face.Handle, out var vertices );

			foreach ( var vertex in vertices )
			{
				if ( vertex.IsValid )
					yield return new MeshVertex( face.Component, vertex );
			}
		}

		foreach ( var edge in Selection.OfType<MeshEdge>() )
		{
			if ( !edge.IsValid() )
				continue;

			var mesh = edge.Component.Mesh;
			mesh.GetEdgeVertices( edge.Handle, out var vertexA, out var vertexB );

			if ( vertexA.IsValid )
				yield return new MeshVertex( edge.Component, vertexA );

			if ( vertexB.IsValid )
				yield return new MeshVertex( edge.Component, vertexB );
		}
	}

	private void SelectVertex( MeshVertex vertex )
	{
		if ( vertex.IsValid() )
		{
			using ( Gizmo.ObjectScope( vertex.Component.GameObject, vertex.Transform ) )
			{
				using ( Gizmo.Scope( "Vertex Hover" ) )
				{
					Gizmo.Draw.IgnoreDepth = true;
					Gizmo.Draw.Color = Color.White;
					Gizmo.Draw.Sprite( vertex.PositionLocal, 8, null, false );
				}
			}
		}

		UpdateSelection( vertex );
	}

	protected override bool OnDoubleClick( IMeshElement element )
	{
		var vertex = element is MeshVertex target ? target : default;
		if ( !vertex.IsValid() )
		{
			var face = MeshTrace.TraceFace();
			if ( !face.IsValid() )
				return false;
			vertex = new MeshVertex( face.Component, face.Component.Mesh.GetFaceVertices( face.Handle ).First() );
		}

		var mesh = vertex.Component.Mesh;
		var queue = new Queue<HalfEdgeMesh.VertexHandle>();
		var visited = new HashSet<HalfEdgeMesh.VertexHandle> { vertex.Handle };
		var vertices = new List<IMeshElement>();
		queue.Enqueue( vertex.Handle );
		while ( queue.TryDequeue( out var current ) )
		{
			vertices.Add( new MeshVertex( vertex.Component, current ) );
			mesh.GetEdgesConnectedToVertex( current, out var edges );
			foreach ( var edge in edges )
			{
				if ( mesh.IsEdgeHidden( edge ) )
					continue;
				mesh.GetEdgeVertices( edge, out var a, out var b );
				var next = a == current ? b : a;
				if ( !mesh.IsVertexHidden( next ) && visited.Add( next ) )
					queue.Enqueue( next );
			}
		}

		ApplyElementSelection( vertices, Gizmo.KeyboardModifiers, "Select Connected Vertices" );
		return true;
	}

	public override List<MeshFace> ExtrudeSelection( Vector3 delta = default )
	{
		var groups = Selection.OfType<MeshVertex>()
			.GroupBy( v => v.Component );

		var connectingFaces = new List<MeshFace>();
		if ( !groups.Any() )
			return connectingFaces;

		var selectedVertices = new List<MeshVertex>();
		var components = groups.Select( x => x.Key );

		using ( SceneEditorSession.Active.UndoScope( "Extrude Vertices" ).WithComponentChanges( components ).Push() )
		{
			var extrudeWidth = EditorScene.GizmoSettings.GridSpacing;

			foreach ( var group in groups )
			{
				var component = group.Key;
				var mesh = component.Mesh;
				var vertices = group.Select( x => x.Handle ).ToList();

				mesh.ExtendOrExtrudeVertices( vertices, 0.0f, extrudeWidth,
					out var modifiedVertices, out _, out var newFaces );

				foreach ( var hVertex in modifiedVertices )
					selectedVertices.Add( new MeshVertex( component, hVertex ) );

				foreach ( var hFace in newFaces )
					connectingFaces.Add( new MeshFace( component, hFace ) );
			}
		}

		Selection.Clear();

		foreach ( var vertex in selectedVertices )
			Selection.Add( vertex );

		CalculateSelectionVertices();

		return connectingFaces;
	}

	protected override IEnumerable<IMeshElement> GetAllSelectedElements()
	{
		foreach ( var component in SelectedMeshes )
		{
			foreach ( var hVertex in component.Mesh.VertexHandles )
				yield return new MeshVertex( component, hVertex );
		}
	}

	protected override IEnumerable<MeshVertex> GetConnectedSelectionElements()
	{
		var unique = new HashSet<MeshVertex>();

		foreach ( var component in Selection.OfType<GameObject>()
			.Select( x => x.GetComponent<MeshComponent>() )
			.Where( x => x.IsValid() ) )
		{
			foreach ( var vertex in component.Mesh.VertexHandles )
			{
				unique.Add( new MeshVertex( component, vertex ) );
			}
		}

		foreach ( var face in Selection.OfType<MeshFace>() )
		{
			face.Component.Mesh.GetVerticesConnectedToFace( face.Handle, out var vertices );

			foreach ( var vertex in vertices )
			{
				unique.Add( new MeshVertex( face.Component, vertex ) );
			}
		}

		foreach ( var edge in Selection.OfType<MeshEdge>() )
		{
			edge.Component.Mesh.GetVerticesConnectedToEdge( edge.Handle, out var vertexA, out var vertexB );

			unique.Add( new MeshVertex( edge.Component, vertexA ) );
			unique.Add( new MeshVertex( edge.Component, vertexB ) );
		}

		return unique;
	}
}
