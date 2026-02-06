
namespace Editor.MeshEditor;

/// <summary>
/// Select and edit vertices.
/// </summary>
[Title( "Vertex Tool" )]
[Icon( "workspaces" )]
[Alias( "tools.vertex-tool" )]
[Group( "1" )]
public sealed partial class VertexTool( MeshTool tool ) : SelectionTool<MeshVertex>( tool )
{
	public override bool DrawVertices => true;

	public override bool HasBoxSelectionMode() => true;

	protected override void OnBoxSelect( Frustum frustum, Rect screenRect, bool isFinal )
	{
		HashSet<MeshVertex> selection = [];
		HashSet<MeshVertex> previous = [];

		foreach ( var component in Scene.GetAllComponents<MeshComponent>() )
		{
			var mesh = component.Mesh;
			if ( mesh == null ) continue;

			var bounds = component.GetWorldBounds();
			if ( !frustum.IsInside( bounds, true ) )
			{
				foreach ( var handle in mesh.VertexHandles )
					previous.Add( new MeshVertex( component, handle ) );

				continue;
			}

			var transform = component.Transform.World;

			foreach ( var v in mesh.VertexHandles )
			{
				var worldPos = transform.PointToWorld( mesh.GetVertexPosition( v ) );

				if ( !Tool.SelectionThrough && IsVertexOccluded( worldPos, Gizmo.Camera.Position ) )
				{
					previous.Add( new MeshVertex( component, v ) );
					continue;
				}

				if ( frustum.IsInside( worldPos ) )
				{
					selection.Add( new MeshVertex( component, v ) );
				}
				else
				{
					previous.Add( new MeshVertex( component, v ) );
				}
			}
		}

		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			foreach ( var v in selection )
			{
				if ( Selection.Contains( v ) )
					Selection.Remove( v );
			}
		}
		else
		{
			foreach ( var v in selection )
			{
				if ( !Selection.Contains( v ) )
					Selection.Add( v );
			}

			if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			{
				foreach ( var v in previous )
				{
					if ( Selection.Contains( v ) )
						Selection.Remove( v );
				}
			}
		}
	}

	public override void OnUpdate()
	{
		base.OnUpdate();

		using var scope = Gizmo.Scope( "VertexTool" );

		var closestVertex = MeshTrace.GetClosestVertex( 8 );
		if ( closestVertex.IsValid() )
			Gizmo.Hitbox.TrySetHovered( closestVertex.PositionWorld );

		if ( Gizmo.IsHovered && Tool.MoveMode.AllowSceneSelection )
		{
			SelectVertex();

			if ( Gizmo.IsDoubleClicked )
				SelectAllVertices();
		}

		using ( Gizmo.Scope( "Vertex Selection" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White;

			foreach ( var vertex in Selection.OfType<MeshVertex>() )
				Gizmo.Draw.Sprite( vertex.PositionWorld, 8, null, false );
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

	private void SelectVertex()
	{
		var vertex = MeshTrace.GetClosestVertex( 8 );
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

	private void SelectAllVertices()
	{
		var vertex = MeshTrace.GetClosestVertex( 8 );
		if ( !vertex.IsValid() )
			return;

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			Selection.Clear();

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			foreach ( var hVertex in vertex.Component.Mesh.VertexHandles )
				Selection.Add( new MeshVertex( vertex.Component, hVertex ) );
		}
	}

	protected override IEnumerable<IMeshElement> GetAllSelectedElements()
	{
		foreach ( var group in Selection.OfType<MeshVertex>()
			.GroupBy( x => x.Component ) )
		{
			var component = group.Key;
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
