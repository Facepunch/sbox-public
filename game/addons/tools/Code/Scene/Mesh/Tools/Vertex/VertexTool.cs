
namespace Editor.MeshEditor;

/// <summary>
/// Move, rotate and scale mesh vertices
/// </summary>
[EditorTool( "mesh.vertex" )]
[Title( "Vertex Select" )]
[Icon( "workspaces" )]
[Alias( "Vertex" )]
[Group( "Mesh" )]
[Order( 1 )]
public sealed partial class VertexTool : BaseMeshTool
{
	protected override bool DrawVertices => true;

	public override void OnUpdate()
	{
		base.OnUpdate();

		using var scope = Gizmo.Scope( "VertexTool" );

		var closestVertex = GetClosestVertex( 8 );
		if ( closestVertex.IsValid() )
			Gizmo.Hitbox.TrySetHovered( closestVertex.PositionWorld );

		if ( Gizmo.IsHovered )
		{
			SelectVertex();

			if ( Gizmo.IsDoubleClicked )
				SelectAllVertices();
		}

		using ( Gizmo.Scope( "Vertex Selection" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.Yellow;

			foreach ( var vertex in MeshSelection.OfType<MeshVertex>() )
				Gizmo.Draw.Sprite( vertex.PositionWorld, 12, null, false );
		}
	}

	private void SelectVertex()
	{
		var vertex = GetClosestVertex( 8 );
		if ( vertex.IsValid() )
		{
			using ( Gizmo.ObjectScope( vertex.Component.GameObject, vertex.Transform ) )
			{
				using ( Gizmo.Scope( "Vertex Hover" ) )
				{
					Gizmo.Draw.IgnoreDepth = true;
					Gizmo.Draw.Color = Color.Green;
					Gizmo.Draw.Sprite( vertex.PositionLocal, 12, null, false );
				}
			}
		}

		UpdateSelection( vertex );
	}

	private void SelectAllVertices()
	{
		var vertex = GetClosestVertex( 8 );
		if ( !vertex.IsValid() )
			return;

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			MeshSelection.Clear();

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			foreach ( var hVertex in vertex.Component.Mesh.VertexHandles )
				MeshSelection.Add( new MeshVertex( vertex.Component, hVertex ) );
		}
	}

	protected override IEnumerable<IMeshElement> GetAllSelectedElements()
	{
		foreach ( var group in MeshSelection.OfType<MeshVertex>()
			.GroupBy( x => x.Component ) )
		{
			var component = group.Key;
			foreach ( var hVertex in component.Mesh.VertexHandles )
				yield return new MeshVertex( component, hVertex );
		}
	}

	[Shortcut( "mesh.vertex", "1" )]
	public static void ActivateTool()
	{
		EditorToolManager.SetTool( nameof( VertexTool ) );
	}
}
