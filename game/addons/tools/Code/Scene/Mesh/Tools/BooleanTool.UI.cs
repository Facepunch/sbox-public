namespace Editor.MeshEditor;

partial class BooleanTool
{
	public override Widget CreateToolSidebar()
	{
		return new BooleanToolWidget( this );
	}

	public class BooleanToolWidget : ToolSidebarWidget
	{
		readonly BooleanTool _tool;
		readonly Button _applyButton;
		readonly Button _swapButton;
		readonly IconButton _unionButton;
		readonly IconButton _subtractButton;
		readonly IconButton _intersectButton;

		public BooleanToolWidget( BooleanTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Boolean Tool", "difference" );

			{
				var group = AddGroup( "Operation" );
				var row = group.AddRow();
				row.Spacing = 4;

				_unionButton = CreateButton( "Union", "join_full", null, () => SetMode( BooleanMode.Union ), true, row );
				_subtractButton = CreateButton( "Subtract", "remove_circle_outline", null, () => SetMode( BooleanMode.Subtract ), true, row );
				_intersectButton = CreateButton( "Intersect", "filter_none", null, () => SetMode( BooleanMode.Intersect ), true, row );
			}

			{
				var group = AddGroup( "Selection" );
				var row = group.AddRow();
				row.Spacing = 4;

				_swapButton = new Button( "Swap A \u2194 B" );
				_swapButton.Clicked = () => _tool.SwapSelection();
				_swapButton.ToolTip = "Swap which object is A and which is B (Subtract only)";
				row.Add( _swapButton );
			}

			Layout.AddSpacingCell( 8 );

			{
				var group = AddGroup( "Options" );
				var row = group.AddRow();

				var deleteCheck = new Checkbox( "Delete Other Mesh" );
				deleteCheck.Value = _tool.DeleteOtherMesh;
				deleteCheck.Toggled = () => _tool.DeleteOtherMesh = deleteCheck.Value;
				row.Add( deleteCheck );
			}

			Layout.AddSpacingCell( 8 );

			{
				var row = Layout.AddRow();
				row.Spacing = 4;

				_applyButton = new Button( "Apply", "done" );
				_applyButton.Clicked = Apply;
				_applyButton.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.boolean-apply" ) + "]";
				row.Add( _applyButton );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.boolean-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();
		}

		void SetMode( BooleanMode mode ) => _tool.Mode = mode;

		[Shortcut( "mesh.boolean-apply", "enter", typeof( SceneViewWidget ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.boolean-cancel", "ESC", typeof( SceneViewWidget ) )]
		void Cancel() => _tool.Cancel();

		[EditorEvent.Frame]
		public void Frame()
		{
			_applyButton.Enabled = _tool.CanApply;
			_swapButton.Enabled = _tool.Mode == BooleanMode.Subtract;
			_unionButton.IsActive = _tool.Mode == BooleanMode.Union;
			_subtractButton.IsActive = _tool.Mode == BooleanMode.Subtract;
			_intersectButton.IsActive = _tool.Mode == BooleanMode.Intersect;
		}
	}
}
