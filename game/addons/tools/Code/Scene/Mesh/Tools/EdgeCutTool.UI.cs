
namespace Editor.MeshEditor;

partial class EdgeCutTool
{
	public override Widget CreateToolSidebar()
	{
		return new EdgeCutToolWidget( this );
	}

	public class EdgeCutToolWidget : ToolSidebarWidget
	{
		readonly EdgeCutTool _tool;

		public EdgeCutToolWidget( EdgeCutTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Edge Cut Tool", "polyline" );

			{
				var group = AddGroup( "Loop Mode" );
				var row = group.AddRow();
				row.Spacing = 4;
				row.Add( ControlSheetRow.Create( tool.GetSerialized().GetProperty( nameof( tool.LoopMode ) ) ) ).FixedHeight = Theme.ControlHeight;
				row.AddStretchCell();
			}

			Layout.AddSpacingCell( 8 );

			{
				var row = Layout.AddRow();
				row.Spacing = 4;

				var apply = new Button( "Apply", "done" );
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.edge-cut-apply" ) + "]";
				row.Add( apply );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.edge-cut-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();

			AddShortcuts(
				("Apply Cut", "Enter"),
				("Cancel Cut", "Esc"),
				("Toggle Loop Mode", "V")
			);
		}

		[Shortcut( "mesh.edge-cut-apply", "enter", typeof( SceneViewWidget ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.edge-cut-cancel", "ESC", typeof( SceneViewWidget ) )]
		void Cancel() => _tool.Cancel();

		[Shortcut( "mesh.edge-cut-loop", "V", typeof( SceneViewWidget ) )]
		void ToggleLoopMode() => _tool.LoopMode = !_tool.LoopMode;
	}
}
