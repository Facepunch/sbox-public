using HalfEdgeMesh;

namespace Editor.MeshEditor;

partial class EdgeArchTool
{
	public static int NumSteps { get; set; } = 4;
	public static float ArcHeight { get; set; } = 16.0f;
	public static float ArcOffset { get; set; } = 0.0f;

	public override Widget CreateToolSidebar()
	{
		return new EdgeArchToolWidget( this, edges );
	}

	public class EdgeArchToolWidget : ToolSidebarWidget
	{
		private readonly EdgeArchTool _tool;
		private readonly EdgeArchEdges[] _edges;

		private struct ArchProperties
		{
			[Title( "Steps" ), Range( 1, 32 ), WideMode]
			public readonly int Steps { get => NumSteps; set => NumSteps = value; }

			[Title( "Arc Height" ), Range( -256.0f, 256.0f ), WideMode]
			public readonly float Height { get => ArcHeight; set => ArcHeight = value; }

			[Title( "Arc Offset" ), Range( -256.0f, 256.0f ), WideMode]
			public readonly float Offset { get => ArcOffset; set => ArcOffset = value; }
		}

		[InlineEditor( Label = false )]
		private readonly ArchProperties _archProperties = new();

		public EdgeArchToolWidget( EdgeArchTool tool, EdgeArchEdges[] edges ) : base()
		{
			_tool = tool;
			_edges = edges;

			AddTitle( "Edge Arch Tool", "timeline" );

			{
				var group = AddGroup( "Properties" );
				var row = group.AddRow();
				row.Spacing = 8;

				var sheet = new ControlSheet();
				var control = sheet.AddRow( this.GetSerialized().GetProperty( nameof( _archProperties ) ) );
				control.OnChildValuesChanged += ( e ) => UpdateMesh();
				row.Add( sheet );

				row = group.AddRow();
				row.Spacing = 4;

				var apply = new Button( "Apply", "done" );
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.edge-arch-apply" ) + "]";
				row.Add( apply );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.edge-arch-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();

			UpdateMesh();
		}

		private void UpdateMesh()
		{
			_tool.UpdateArch( NumSteps, ArcHeight, ArcOffset );
		}

		[Shortcut( "mesh.edge-arch-increase", "]", typeof( SceneViewWidget ) )]
		private void IncreaseSteps()
		{
			NumSteps = Math.Min( 32, NumSteps + 1 );
			UpdateMesh();
		}

		[Shortcut( "mesh.edge-arch-decrease", "[", typeof( SceneViewWidget ) )]
		private void DecreaseSteps()
		{
			NumSteps = Math.Max( 1, NumSteps - 1 );
			UpdateMesh();
		}

		[Shortcut( "mesh.edge-arch-cancel", "ESC", ShortcutType.Application )]
		private void Cancel()
		{
			var components = _edges.Select( x => x.Component ).ToArray();

			using ( SceneEditorSession.Active.UndoScope( "Cancel Edge Arch" )
				.WithComponentChanges( components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var edge in _edges )
				{
					if ( !edge.Component.IsValid() ) continue;

					edge.Component.Mesh = edge.Mesh;
					edge.Component.RebuildMesh();
				}
			}

			EditorToolManager.SetSubTool( nameof( EdgeTool ) );
		}

		[Shortcut( "mesh.edge-arch-apply", "enter", ShortcutType.Application )]
		private void Apply()
		{
			var components = _edges.Select( x => x.Component ).ToArray();

			using ( SceneEditorSession.Active.UndoScope( "Apply Edge Arch" )
				.WithComponentChanges( components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var component in components )
				{
					if ( !component.IsValid() ) continue;

					if ( _tool._newEdges.TryGetValue( component, out var newEdges ) )
					{
						foreach ( var edge in newEdges )
						{
							selection.Add( new MeshEdge( component, edge ) );
						}
					}
				}
			}

			EditorToolManager.SetSubTool( nameof( EdgeTool ) );
		}
	}
}
