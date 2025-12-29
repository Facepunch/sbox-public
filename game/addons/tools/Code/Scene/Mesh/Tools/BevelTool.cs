using System.Text.Json.Serialization;

namespace Editor.MeshEditor;

public struct BevelEdges
{
	[Hide, JsonInclude] public MeshComponent Component { get; set; }
	[Hide, JsonInclude] public PolygonMesh Mesh { get; set; }
	[Hide, JsonInclude] public List<int> Edges { get; set; }
}

[Alias( "tools.bevel-tool" )]
public partial class BevelTool( BevelEdges[] edges ) : EditorTool
{
	public override void OnUpdate()
	{
		if ( edges.Length == 0 ) return;

		var color = new Color( 0.3137f, 0.7843f, 1f, 0.5f );

		foreach ( var group in edges )
		{
			var comp = group.Component;
			if ( !comp.IsValid() ) continue;

			using ( Gizmo.ObjectScope( comp.GameObject, comp.WorldTransform ) )
			using ( Gizmo.Scope( "Edges" ) )
			{
				Gizmo.Draw.Color = color;
				Gizmo.Draw.LineThickness = 2;

				foreach ( var e in comp.Mesh.GetEdges() )
				{
					Gizmo.Draw.Line( e );
				}
			}
		}
	}
}
