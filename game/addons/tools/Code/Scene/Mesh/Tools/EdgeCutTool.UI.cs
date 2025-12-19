
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

				var snapping = new IconButton( "grid_on", null );
				snapping.IsToggle = true;
				snapping.IsActive = _tool._snappingEnabled;
				snapping.OnToggled = ( e ) => { _tool._snappingEnabled = e; EditorCookie.Set( "edgecut_snapping", e ); };
				snapping.ToolTip = "Toggle snapping for edge cuts";
				row.Add( snapping );

				var showInfo = new IconButton( "info", null );
				showInfo.IsToggle = true;
				showInfo.IsActive = _tool._showSnappingInfo;
				showInfo.OnToggled = ( e ) => { _tool._showSnappingInfo = e; EditorCookie.Set( "edgecut_showinfo", e ); };
				showInfo.ToolTip = "Toggle display of snapping info";
				row.Add( showInfo );
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
		}

		[Shortcut( "mesh.edge-cut-apply", "enter", typeof( SceneDock ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.edge-cut-cancel", "ESC", typeof( SceneDock ) )]
		void Cancel() => _tool.Cancel();
	}
}
