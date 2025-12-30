
namespace Editor.MeshEditor;

partial class ClipTool
{
	public override Widget CreateToolSidebar()
	{
		return new ClipToolWidget( this );
	}

	public class ClipToolWidget : ToolSidebarWidget
	{
		readonly ClipTool _tool;

		public ClipToolWidget( ClipTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Clipping Tool", "content_cut" );

			{
				var group = AddGroup( "Keep Mode" );
				var row = group.AddRow();
				row.Spacing = 4;

				CreateButton( "Keep Front", "hammer/clipper_keep_front.png", null, () => Keep( ClipKeepMode.Front ), true, row );
				CreateButton( "Keep Back", "hammer/clipper_keep_back.png", null, () => Keep( ClipKeepMode.Back ), true, row );
				CreateButton( "Keep Both", "hammer/clipper_keep_both.png", null, () => Keep( ClipKeepMode.Both ), true, row );
			}

			Layout.AddSpacingCell( 8 );

			{
				var so = tool.GetSerialized();
				var c = ControlSheetRow.Create( so.GetProperty( nameof( CapNewSurfaces ) ) );
				Layout.Add( c );
			}

			Layout.AddSpacingCell( 8 );

			{
				var row = Layout.AddRow();
				row.Spacing = 4;

				var apply = new Button( "Apply", "done" );
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.clip-apply" ) + "]";
				row.Add( apply );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.clip-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();
		}

		void Keep( ClipKeepMode keepMode ) => _tool.KeepMode = keepMode;

		[Shortcut( "mesh.clip-apply", "enter", typeof( SceneDock ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.clip-cancel", "ESC", typeof( SceneDock ) )]
		void Cancel() => _tool.Cancel();
	}
}
