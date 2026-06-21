namespace Editor.MeshEditor;

partial class TriangulateTool
{
	public static QuadMethod TriangulateQuadMethod { get; set; } = QuadMethod.Fixed;
	public static NgonMethod TriangulateNgonMethod { get; set; } = NgonMethod.Fan;
	public static int TriangulateMinimumVertices { get; set; } = 4;

	public override Widget CreateToolSidebar()
	{
		return new TriangulateToolWidget( this );
	}

	public class TriangulateToolWidget : ToolSidebarWidget
	{
		private readonly TriangulateTool _tool;

		[InlineEditor( Label = false )] private readonly TriangulateProperties _triangulateProperties = new();

		public TriangulateToolWidget( TriangulateTool tool )
		{
			_tool = tool;

			AddTitle( "Triangulate Tool", "details" );

			{
				Layout group = AddGroup( "Properties" );
				Layout row = group.AddRow();
				row.Spacing = 8;

				ControlSheet sheet = new();
				ControlWidget control =
					sheet.AddRow( this.GetSerialized().GetProperty( nameof(_triangulateProperties) ) );
				control.OnChildValuesChanged += _ => UpdateMesh();
				row.Add( sheet );

				row = group.AddRow();
				row.Spacing = 4;

				Button apply = new("Apply", "done");
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.inset-apply" ) + "]";
				row.Add( apply );

				Button cancel = new("Cancel", "close");
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.inset-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();

			UpdateMesh();
		}

		private void UpdateMesh()
		{
			_tool.UpdateTriangulation( TriangulateQuadMethod, TriangulateNgonMethod, TriangulateMinimumVertices );
		}

		[Shortcut( "mesh.triangulate-apply", "enter", ShortcutType.Application )]
		private void Apply()
		{
			_tool.Apply();
		}

		[Shortcut( "mesh.triangulate-cancel", "ESC", ShortcutType.Application )]
		private void Cancel()
		{
			_tool.Cancel();
		}

		private struct TriangulateProperties
		{
			[Title( "Quad Method" )]
			[WideMode]
			[Description( "Determines how quads (4 sided faces) are triangulated." )]
			[EnumDropdown]
			public readonly QuadMethod QuadMethod
			{
				get => TriangulateQuadMethod;
				set => TriangulateQuadMethod = value;
			}

			[Title( "N-gon Method" )]
			[WideMode]
			[Description( "Determines how n-gons (faces with 5+ sides) are triangulated." )]
			[EnumDropdown]
			public readonly NgonMethod NgonMethod
			{
				get => TriangulateNgonMethod;
				set => TriangulateNgonMethod = value;
			}

			[Title( "Minimum Vertices" )]
			[Range( 4, 10, false )]
			[WideMode]
			[Description( "Ignores faces with less than this many sides." )]
			public readonly int MinimumVertices
			{
				get => TriangulateMinimumVertices;
				set => TriangulateMinimumVertices = Math.Max( 4, value );
			}
		}
	}
}
