namespace Editor.MeshEditor;

public partial class QuadrangulateTool
{
	public static float QuadrangulateMaxFaceAngle { get; set { field = value.Clamp( 0, 180f ); } } = 40.0f;
	public static float QuadrangulateMaxShapeAngle { get; set { field = value.Clamp( 0, 180f ); } } = 40.0f;
	public static float QuadrangulateTopologyInfluence { get; set { field = value.Clamp( 0.0f, 2.0f ); } } = 1.0f;
	public static bool QuadrangulateCompareUVs { get; set; } = true;
	public static bool QuadrangulateCompareVertexColor { get; set; } = true;
	public static bool QuadrangulateCompareVertexBlend { get; set; } = true;
	public static bool QuadrangulateCompareFaceMaterial { get; set; } = true;
	public static bool QuadrangulateCompareSmoothing { get; set; } = true;
	
	public override Widget CreateToolSidebar()
	{
		return new QuadrangulateToolWidget( this );
	}

	public class QuadrangulateToolWidget : ToolSidebarWidget
	{
		private readonly QuadrangulateTool _tool;

		private struct QuadrangulateProperties()
		{
			[Title( "Max Face Angle" ), Range( 0.0f, 180.0f, true, true ), Step( 0.1f ), WideMode, Description( "The tolerance for difference in face normals in order to be quadrangulated.")]
			public readonly float MaxFaceAngle { get => QuadrangulateMaxFaceAngle; set => QuadrangulateMaxFaceAngle = value; }
			
			[Title( "Max Shape Angle" ), Range( 0.0f, 180.0f, true, true ), Step( 0.1f ), WideMode, Description( "The interior angle tolerance for created quads. 0 means only perfect 90 degree interior angles." )]
			public readonly float MaxShapeAngle { get => QuadrangulateMaxShapeAngle; set => QuadrangulateMaxShapeAngle = value; }
			
			[Title( "Topology Influence" ), Range( 0.0f, 2.0f, true, true ), Step( 0.001f ), WideMode, Description( "How heavily to prioritize keeping faces planar.")]
			public readonly float TopologyInfluence { get => QuadrangulateTopologyInfluence; set => QuadrangulateTopologyInfluence = value; }
			
			[Title( "Compare UVs" ), Description( "Limit by UV seams" )]
			public readonly bool CompareUVs { get => QuadrangulateCompareUVs; set => QuadrangulateCompareUVs = value; }
			
			[Title( "Compare Vertex Color" ), Description( "Limit by vertex color split" )]
			public readonly bool CompareVertexColor { get => QuadrangulateCompareVertexColor; set => QuadrangulateCompareVertexColor = value; }
			
			[Title( "Compare Vertex Blend" ), Description( "Limit by vertex material blend." )]
			public readonly bool CompareVertexBlend { get => QuadrangulateCompareVertexBlend; set => QuadrangulateCompareVertexBlend = value; }
			
			[Title( "Compare Material" ), Description( "Limit by different face materials.")]
			public readonly bool CompareFaceMaterial { get => QuadrangulateCompareFaceMaterial; set => QuadrangulateCompareFaceMaterial = value; }
			
			[Title( "Compare Smoothing" ), Description( "Limit by hard edge smoothing." )]
			public readonly bool CompareSmoothing { get => QuadrangulateCompareSmoothing; set => QuadrangulateCompareSmoothing = value; }
		}
		
		[InlineEditor( Label = false )]
		readonly QuadrangulateProperties _quadrangulateProperties = new();
		
		public QuadrangulateToolWidget( QuadrangulateTool tool ) : base()
		{
			_tool = tool;
			
			AddTitle( "Quadrangulate Tool", "crop_square" );
			
			{
				var group = AddGroup( "Properties" );
				var row = group.AddRow();
				row.Spacing = 8;

				var sheet = new ControlSheet();
				var control = sheet.AddRow( this.GetSerialized().GetProperty( nameof( _quadrangulateProperties ) ) );
				control.OnChildValuesChanged += _ => UpdateMesh();
				row.Add( sheet );

				row = group.AddRow();
				row.Spacing = 4;

				var apply = new Button( "Apply", "done" );
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.quadrangulate-apply" ) + "]";
				row.Add( apply );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.quadrangulate-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();

			UpdateMesh();
		}
		
		void UpdateMesh()
		{
			_tool.UpdateQuadrangulate( QuadrangulateMaxFaceAngle, QuadrangulateMaxShapeAngle, 
				QuadrangulateTopologyInfluence, QuadrangulateCompareUVs, QuadrangulateCompareVertexColor, 
				QuadrangulateCompareVertexBlend, QuadrangulateCompareFaceMaterial, QuadrangulateCompareSmoothing );
		}

		[Shortcut( "mesh.quadrangulate-apply", "enter", ShortcutType.Application )]
		void Apply()
		{
			_tool.Apply();
		}

		[Shortcut( "mesh.quadrangulate-cancel", "ESC", ShortcutType.Application )]
		void Cancel()
		{
			_tool.Cancel();
		}
	}
}
