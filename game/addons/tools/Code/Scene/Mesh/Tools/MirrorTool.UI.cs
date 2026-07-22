
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
		readonly Button _applyButton;

		public MirrorToolWidget( MirrorTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Mirror Tool", "flip" );

			{
				var row = Layout.AddRow();
				row.Spacing = 4;

				_applyButton = new Button( "Apply", "done" );
				_applyButton.Clicked = Apply;
				_applyButton.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.mirror-apply" ) + "]";
				row.Add( _applyButton );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.mirror-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddSpacingCell( 8 );

			AddShortcuts(
				("Draw Mirror Line", "LMB Drag"),
				("Move Line / Handles", "LMB Drag"),
				("Apply Mirror", EditorShortcuts.GetKeys( "mesh.mirror-apply" )),
				("Cancel", EditorShortcuts.GetKeys( "mesh.mirror-cancel" ))
			);

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.mirror-apply", "enter", typeof( SceneViewWidget ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.mirror-cancel", "ESC", typeof( SceneViewWidget ) )]
		void Cancel() => _tool.Cancel();

		[EditorEvent.Frame]
		public void Frame()
		{
			_applyButton?.Enabled = _tool.CanApply;
		}
	}
}
