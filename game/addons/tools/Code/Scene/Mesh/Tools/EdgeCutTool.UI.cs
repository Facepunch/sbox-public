
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

			AddTitle( "Edge Cut Tool", "content_cut" );

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
		}

		[Shortcut( "mesh.edge-cut-apply", "enter", typeof( SceneDock ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.edge-cut-cancel", "ESC", typeof( SceneDock ) )]
		void Cancel() => _tool.Cancel();
	}
}
