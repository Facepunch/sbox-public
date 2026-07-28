
namespace Editor.MeshEditor;

partial class EdgeCutTool
{
	public override Widget CreateToolSidebar() => new EdgeCutToolWidget( this );

	public class EdgeCutToolWidget : ToolSidebarWidget
	{
		private readonly EdgeCutTool _tool;

		public EdgeCutToolWidget( EdgeCutTool tool )
		{
			_tool = tool;

			AddTitle( "Edge Cut Tool", "polyline" );

			var group = AddGroup( "Loop Mode" );
			var row = group.AddRow();
			row.Spacing = 4;

			var serialized = tool.GetSerialized();
			var property = serialized.GetProperty( nameof( EdgeCutTool.LoopMode ) );
			row.Add( ControlSheetRow.Create( property ) ).FixedHeight = Theme.ControlHeight;
			row.AddStretchCell();

			Layout.AddSpacingCell( 8 );

			row = Layout.AddRow();
			row.Spacing = 4;

			var apply = new Button( "Apply", "done" );
			apply.Clicked = Apply;
			apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.edge-cut-apply" ) + "]";
			row.Add( apply );

			var cancel = new Button( "Cancel", "close" );
			cancel.Clicked = Cancel;
			cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.edge-cut-cancel" ) + "]";
			row.Add( cancel );

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
		void ToggleLoopMode() => _tool.ToggleLoopMode();
	}
}
