namespace Editor.MeshEditor;

partial class EdgeArchTool
{
	public override Widget CreateToolSidebar() => new EdgeArchToolWidget( this );

	public class EdgeArchToolWidget : ToolSidebarWidget
	{
		private readonly EdgeArchTool _tool;
		private readonly SerializedObject _serialized;
		private readonly SerializedProperty.CustomizableSerializedProperty _parameters;

		public EdgeArchToolWidget( EdgeArchTool tool )
		{
			_tool = tool;

			AddTitle( "Edge Arch Tool", "timeline" );

			var group = AddGroup( "Properties" );
			var row = group.AddRow();
			row.Spacing = 8;

			var sheet = new ControlSheet();
			_serialized = _tool.GetSerialized();
			_parameters = _serialized.GetProperty( nameof( EdgeArchTool.Parameters ) ).GetCustomizable();
			_parameters.AddAttribute( new DefaultValueAttribute( new EdgeArchParameters() ) );
			_serialized.OnPropertyStartEdit += _ => _tool.BeginParameterEdit();
			_serialized.OnPropertyFinishEdit += OnParametersFinished;

			var control = sheet.AddRow( _parameters );
			control.OnChildValuesChanged += _ => _tool.UpdateArch();
			row.Add( sheet );

			row = group.AddRow();
			row.Spacing = 4;

			var apply = new Button( "Apply", "done" );
			apply.Clicked = Apply;
			apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.edge-arch-apply" ) + "]";
			row.Add( apply );

			var cancel = new Button( "Cancel", "close" );
			cancel.Clicked = Cancel;
			cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.edge-arch-cancel" ) + "]";
			row.Add( cancel );

			Layout.AddStretchCell();

			_tool.UpdateArch();
		}

		private void OnParametersFinished( SerializedProperty property )
		{
			_tool.CommitParameterEdit( $"Adjust Edge Arch {property.DisplayName}" );
		}

		[Shortcut( "mesh.edge-arch-increase", "]", typeof( SceneViewWidget ) )]
		private void IncreaseSteps()
		{
			AdjustSteps( 1 );
		}

		[Shortcut( "mesh.edge-arch-decrease", "[", typeof( SceneViewWidget ) )]
		private void DecreaseSteps()
		{
			AdjustSteps( -1 );
		}

		private void AdjustSteps( int delta )
		{
			_tool.BeginParameterEdit();

			var parameters = _tool.Parameters;
			parameters.Steps += delta;
			_tool.Parameters = parameters;

			_tool.CommitParameterEdit( "Adjust Edge Arch Steps" );

			// The sliders don't know we've been at the keyboard
			_serialized.NoteChanged( _parameters );
		}

		[Shortcut( "mesh.edge-arch-cancel", "ESC", typeof( SceneViewWidget ) )]
		private void Cancel()
		{
			_tool.Cancel();
		}

		[Shortcut( "mesh.edge-arch-apply", "enter", typeof( SceneViewWidget ) )]
		private void Apply()
		{
			_tool.Apply();
		}
	}
}
