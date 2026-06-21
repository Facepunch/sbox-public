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

		private struct TriangulateProperties
		{
			[Title( "Quad Method" ), WideMode, Description( "Determines how quads (4 sided faces) are triangulated." ), EnumDropdown]
			public readonly QuadMethod QuadMethod { get => TriangulateQuadMethod; set => TriangulateQuadMethod = value; }
			
			[Title( "N-gon Method" ), WideMode, Description( "Determines how n-gons (faces with 5+ sides) are triangulated." ), EnumDropdown]
			public readonly NgonMethod NgonMethod { get => TriangulateNgonMethod; set => TriangulateNgonMethod = value; }
			
			[Title( "Minimum Vertices" ), Range(4, 10, false, true), WideMode, Description( "Ignores faces with less than this many sides.")]
			public readonly int MinimumVertices { get => TriangulateMinimumVertices; set => TriangulateMinimumVertices = Math.Max( 4, value ); }
		}

		[InlineEditor( Label = false )]
		private readonly TriangulateProperties _triangulateProperties = new();
		
		public TriangulateToolWidget( TriangulateTool tool ) : base()
		{
			_tool = tool;
			
			AddTitle( "Triangulate Tool", "details" );
			
			{
				var group = AddGroup( "Properties" );
				var row = group.AddRow();
				row.Spacing = 8;

				var sheet = new ControlSheet();
				var control = sheet.AddRow( this.GetSerialized().GetProperty( nameof( _triangulateProperties ) ) );
				control.OnChildValuesChanged += _ => UpdateMesh();
				row.Add( sheet );

				row = group.AddRow();
				row.Spacing = 4;

				var apply = new Button( "Apply", "done" );
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.inset-apply" ) + "]";
				row.Add( apply );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.inset-cancel" ) + "]";
				row.Add( cancel );
			}
			
			Layout.AddStretchCell();

			UpdateMesh();
		}
		
		void UpdateMesh()
		{
			_tool.UpdateTriangulation( TriangulateQuadMethod, TriangulateNgonMethod, TriangulateMinimumVertices );
		}

		[Shortcut( "mesh.triangulate-apply", "enter", ShortcutType.Application )]
		void Apply()
		{
			_tool.Apply();
		}

		[Shortcut( "mesh.triangulate-cancel", "ESC", ShortcutType.Application )]
		void Cancel()
		{
			_tool.Cancel();
		}
	}
}
