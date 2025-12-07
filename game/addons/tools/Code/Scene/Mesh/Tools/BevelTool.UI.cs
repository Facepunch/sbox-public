using HalfEdgeMesh;

namespace Editor.MeshEditor;

partial class BevelTool
{
	public override Widget CreateToolSidebar()
	{
		return new BevelToolWidget( edges );
	}

	public class BevelToolWidget : ToolSidebarWidget
	{
		private readonly BevelEdges[] _edges = null;

		private static int BevelSteps { get; set; } = 1;
		private static float BevelShape { get; set; } = 0.5f;
		private static float BevelWidth { get; set; } = 8.0f;

		private struct BevelProperties
		{
			[Title( "Steps" ), Range( 1, 32 ), WideMode]
			public readonly int Steps { get => BevelSteps; set => BevelSteps = value; }

			[Title( "Shape" ), Range( 0.0f, 1.0f ), WideMode]
			public readonly float Shape { get => BevelShape; set => BevelShape = value; }

			[Title( "Width" ), Range( 0.0625f, 256.0f ), WideMode]
			public readonly float Width { get => BevelWidth; set => BevelWidth = value; }
		}

		[InlineEditor( Label = false )]
		private readonly BevelProperties _bevelProperties = new();

		private readonly Dictionary<MeshComponent, List<HalfEdgeHandle>> _newEdges = [];

		public BevelToolWidget( BevelEdges[] edges ) : base()
		{
			_edges = edges;

			AddTitle( "Bevel Tool", "square_foot" );

			{
				var group = AddGroup( "Properties" );
				var row = group.AddRow();
				row.Spacing = 8;

				var sheet = new ControlSheet();
				var c = sheet.AddRow( this.GetSerialized().GetProperty( nameof( _bevelProperties ) ) );
				c.OnChildValuesChanged += ( e ) => UpdateMesh();
				row.Add( sheet );

				row = group.AddRow();
				row.Spacing = 4;

				var apply = new Button( "Apply", "done" );
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.edge-bevel-apply" ) + "]";
				row.Add( apply );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.edge-bevel-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();

			UpdateMesh();
		}

		private void UpdateMesh()
		{
			var steps = BevelSteps % 2 == 0 ? BevelSteps : BevelSteps - 1;

			foreach ( var edge in _edges )
			{
				var mesh = new PolygonMesh();
				mesh.Transform = edge.Mesh.Transform;
				mesh.MergeMesh( edge.Mesh, Transform.Zero, out _, out _, out _ );
				var edges = edge.Edges.Select( x => mesh.HalfEdgeHandleFromIndex( x ) ).ToList();

				var newOuterEdges = new List<HalfEdgeHandle>();
				var newInnerEdges = new List<HalfEdgeHandle>();
				var facesNeedingUVs = new List<FaceHandle>();
				var newFaces = new List<FaceHandle>();
				if ( !mesh.BevelEdges( edges, PolygonMesh.BevelEdgesMode.RemoveClosedEdges, steps, BevelWidth, BevelShape, newOuterEdges, newInnerEdges, newFaces, facesNeedingUVs ) )
					continue;

				foreach ( var hFace in facesNeedingUVs )
				{
					mesh.TextureAlignToGrid( mesh.Transform, hFace );
				}

				mesh.ComputeFaceTextureParametersFromCoordinates( newFaces );

				edge.Component.Mesh = mesh;

				_newEdges[edge.Component] = newOuterEdges.Concat( newInnerEdges ).ToList();
			}
		}

		[Shortcut( "mesh.edge-bevel-cancel", "ESC", ShortcutType.Application )]
		private void Cancel()
		{
			var components = _edges.Select( x => x.Component ).ToArray();

			using ( SceneEditorSession.Active.UndoScope( "Cancel Bevel Edges" )
				.WithComponentChanges( components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var edge in _edges )
				{
					edge.Component.Mesh = edge.Mesh;
					edge.Component.RebuildMesh();
				}
			}

			EditorToolManager.SetSubTool( nameof( EdgeTool ) );
		}

		[Shortcut( "mesh.edge-bevel-apply", "enter", ShortcutType.Application )]
		private void Apply()
		{
			var components = _edges.Select( x => x.Component ).ToArray();

			using ( SceneEditorSession.Active.UndoScope( "Apply Bevel Edges" )
				.WithComponentChanges( components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var edgeGroup in _newEdges )
				{
					foreach ( var edge in edgeGroup.Value )
					{
						selection.Add( new MeshEdge( edgeGroup.Key, edge ) );
					}
				}
			}

			EditorToolManager.SetSubTool( nameof( EdgeTool ) );
		}
	}
}
