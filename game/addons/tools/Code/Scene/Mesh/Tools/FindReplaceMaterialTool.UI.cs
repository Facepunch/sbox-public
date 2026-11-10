namespace Editor.MeshEditor;

partial class FindReplaceMaterialTool
{
	public override Widget CreateToolSidebar()
	{
		return new FindReplaceMaterialWidget( this );
	}

	public class FindReplaceMaterialWidget : ToolSidebarWidget
	{
		readonly FindReplaceMaterialTool _tool;
		readonly Button _applyButton;
		readonly Button _selectButton;
		Label _matchLabel;

		public FindReplaceMaterialWidget( FindReplaceMaterialTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Find / Replace Material", "find_replace" );

			{
				var group = AddGroup( "Materials" );

				var so = tool.GetSerialized();

				var sheet = new ControlSheet();
				sheet.AddRow( so.GetProperty( nameof( FindMaterial ) ) );
				sheet.AddRow( so.GetProperty( nameof( ReplaceMaterial ) ) );
				sheet.AddRow( so.GetProperty( nameof( Scope ) ) );

				var row = group.AddRow();
				row.Add( sheet );
				row.AddSpacingCell( 16 );
			}

			{
				var group = AddGroup( "Matches" );

				var row = group.AddRow();
				row.Spacing = 4;
				row.Margin = 4;

				_matchLabel = new Label( "" );
				row.Add( _matchLabel );
				row.AddStretchCell();
			}

			Layout.AddSpacingCell( 8 );

			{
				var row = Layout.AddRow();
				row.Spacing = 4;
				row.Margin = new Sandbox.UI.Margin( 8, 0 );

				_applyButton = new Button.Primary( "Replace", "done" );
				_applyButton.Clicked = Apply;
				_applyButton.ToolTip = "[Replace " + EditorShortcuts.GetKeys( "mesh.find-replace-material-apply" ) + "]";
				row.Add( _applyButton );

				_selectButton = new Button( "Select", "select_all" );
				_selectButton.Clicked = SelectMatches;
				_selectButton.ToolTip = "Select all matching faces [" + EditorShortcuts.GetKeys( "mesh.find-replace-material-select" ) + "]";
				row.Add( _selectButton );
			}

			Layout.AddSpacingCell( 4 );

			{
				var row = Layout.AddRow();
				row.Spacing = 4;
				row.Margin = new Sandbox.UI.Margin( 8, 0 );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = CloseTool;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.find-replace-material-close" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.find-replace-material-apply", "enter", typeof( SceneViewWidget ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.find-replace-material-select", "shift+enter", typeof( SceneViewWidget ) )]
		void SelectMatches() => _tool.SelectMatches();

		[Shortcut( "mesh.find-replace-material-close", "ESC", typeof( SceneViewWidget ) )]
		void CloseTool() => _tool.Close();

		[EditorEvent.Frame]
		public void Frame()
		{
			_applyButton?.Enabled = _tool.CanApply;
			_selectButton?.Enabled = _tool.CanSelect;
			_matchLabel?.Text = _tool.FindMaterial is null
				? $"{_tool.MatchCount} untextured faces on {_tool.MatchComponentCount} meshes"
				: $"{_tool.MatchCount} faces on {_tool.MatchComponentCount} meshes";
		}
	}
}
