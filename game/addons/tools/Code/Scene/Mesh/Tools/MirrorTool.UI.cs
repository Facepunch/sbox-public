
namespace Editor.MeshEditor;

partial class MirrorTool
{
	public override Widget CreateToolSidebar()
	{
		return new MirrorToolWidget( this );
	}

	public class MirrorToolWidget : ToolSidebarWidget
	{
		readonly MirrorTool _tool;

		public MirrorToolWidget( MirrorTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Mirror Tool", "flip" );

			{
				var row = Layout.AddRow();
				row.Spacing = 4;

				var apply = new Button( "Apply", "done" );
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.mirror-apply" ) + "]";
				row.Add( apply );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.mirror-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.mirror-apply", "enter", typeof( SceneViewWidget ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.mirror-cancel", "ESC", typeof( SceneViewWidget ) )]
		void Cancel() => _tool.Cancel();
	}
}
