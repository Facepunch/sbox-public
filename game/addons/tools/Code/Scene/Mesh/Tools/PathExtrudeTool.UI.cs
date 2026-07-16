namespace Editor.MeshEditor;

partial class PathExtrudeTool
{
	public static PathExtrudeOrigin ProfileOrigin { get; set; } = PathExtrudeOrigin.ObjectLocal;
	public static bool DeleteSource { get; set; } = false;

	public override Widget CreateToolSidebar()
	{
		return new PathExtrudeToolWidget( this );
	}

	public class PathExtrudeToolWidget : ToolSidebarWidget
	{
		private readonly PathExtrudeTool _tool;

		private struct PathExtrudeProperties
		{
			[Title( "Delete Source" ), WideMode]
			public readonly bool Delete { get => DeleteSource; set => DeleteSource = value; }

			[Title( "Profile Origin" ), WideMode]
			public readonly PathExtrudeOrigin Origin { get => ProfileOrigin; set => ProfileOrigin = value; }
		}

		[InlineEditor( Label = false )]
		readonly PathExtrudeProperties _properties = new();

		public PathExtrudeToolWidget( PathExtrudeTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Path Extrude", "route" );

			var group = AddGroup( "Properties" );

			var row = group.AddRow();
			row.Spacing = 8;

			var sheet = new ControlSheet();
			var control = sheet.AddRow( this.GetSerialized().GetProperty( nameof( _properties ) ) );
			control.OnChildValuesChanged += _ => _tool.UpdateMesh();
			row.Add( sheet );

			row = group.AddRow();
			row.Spacing = 4;

			var apply = new Button( "Apply", "done" );
			apply.Clicked = () => _tool.Apply();
			row.Add( apply );

			var cancel = new Button( "Cancel", "close" );
			cancel.Clicked = () => _tool.Cancel();
			row.Add( cancel );

			Layout.AddStretchCell();

			_tool.UpdateMesh();
		}

		[Shortcut( "mesh.path-extrude-apply", "enter", typeof( SceneViewWidget ) )]
		private void ApplyShortcut() => _tool.Apply();

		[Shortcut( "mesh.path-extrude-cancel", "ESC", typeof( SceneViewWidget ) )]
		private void CancelShortcut() => _tool.Cancel();
	}
}
