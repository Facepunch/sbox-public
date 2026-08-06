namespace Editor.MeshEditor;

partial class BevelTool
{
	public override Widget CreateToolSidebar() => new BevelToolWidget( this );

	public class BevelToolWidget : ToolSidebarWidget
	{
		private readonly BevelTool _tool;

		public BevelToolWidget( BevelTool tool )
		{
			_tool = tool;

			AddTitle( "Bevel Tool", "square_foot" );

			var group = AddGroup( "Properties" );
			var row = group.AddRow();
			row.Spacing = 8;

			var sheet = new ControlSheet();
			var serialized = _tool.GetSerialized();
			var property = serialized.GetProperty( nameof( BevelTool.Parameters ) ).GetCustomizable();
			property.AddAttribute( new DefaultValueAttribute( new BevelParameters() ) );
			serialized.OnPropertyStartEdit += _ => _tool.BeginParameterEdit();
			serialized.OnPropertyFinishEdit += OnParametersFinished;

			var control = sheet.AddRow( property );
			control.OnChildValuesChanged += _ => _tool.UpdateBevel();
			row.Add( sheet );

			row = group.AddRow();
			row.Spacing = 4;

			var apply = new Button( "Apply", "done" );
			apply.Clicked = Apply;
			apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.edge-bevel-apply" ) + "]";
			row.Add( apply );

			var cancel = new Button( "Cancel", "close" );
			cancel.Clicked = Cancel;
			cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.edge-bevel-cancel" ) + "]";
			row.Add( cancel );

			Layout.AddStretchCell();

			_tool.UpdateBevel();
		}

		private void OnParametersFinished( SerializedProperty property )
		{
			_tool.CommitParameterEdit( $"Adjust Bevel {property.DisplayName}" );
		}

		[Shortcut( "mesh.edge-bevel-cancel", "ESC", typeof( SceneViewWidget ) )]
		private void Cancel()
		{
			_tool.Cancel();
		}

		[Shortcut( "mesh.edge-bevel-apply", "enter", typeof( SceneViewWidget ) )]
		private void Apply()
		{
			_tool.Apply();
		}
	}
}
