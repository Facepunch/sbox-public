namespace Editor.MeshEditor;

partial class TopologyTool
{
	public static TopologyOperationType ToolOperationType { get; set; } = TopologyOperationType.Triangulate;
	
	// Triangulate
	public static QuadMethod TriangulateQuadMethod { get; set; } = QuadMethod.Fixed;
	public static NgonMethod TriangulateNgonMethod { get; set; } = NgonMethod.Fan;
	public static int TriangulateMinimumVertices { get; set; } = 4;
	
	//Quadrangulate
	public static float QuadrangulateMaxFaceAngle { get; set => field = value.Clamp( 0, 180f ); } = 40.0f;
	public static float QuadrangulateMaxShapeAngle { get; set => field = value.Clamp( 0, 180f ); } = 40.0f;
	public static bool QuadrangulateCompareUVs { get; set; } = true;
	public static bool QuadrangulateCompareVertexColor { get; set; } = true;
	public static bool QuadrangulateCompareVertexBlend { get; set; } = true;
	public static bool QuadrangulateCompareFaceMaterial { get; set; } = true;
	public static bool QuadrangulateCompareSmoothing { get; set; } = true;
	
	public override Widget CreateToolSidebar()
	{
		return new TopologyToolWidget( this );
	}

	public struct TopologyProperties
	{
		[Title( "Operation" )]
		[WideMode]
		[EnumButtonGroup]
		public readonly TopologyOperationType OperationType
		{
			get => ToolOperationType; 
			set => ToolOperationType = value;
		}
			
		[Title( "Quad Method" )]
		[WideMode]
		[Description( "Determines how quads (4 sided faces) are triangulated." )]
		[EnumDropdown]
		[ShowIf("OperationType", TopologyOperationType.Triangulate)]
		public readonly QuadMethod QuadMethod
		{
			get => TriangulateQuadMethod;
			set => TriangulateQuadMethod = value;
		}

		[Title( "N-gon Method" )]
		[WideMode]
		[Description( "Determines how n-gons (faces with 5+ sides) are triangulated." )]
		[EnumDropdown]
		[ShowIf("OperationType", TopologyOperationType.Triangulate)]
		public readonly NgonMethod NgonMethod
		{
			get => TriangulateNgonMethod;
			set => TriangulateNgonMethod = value;
		}

		[Title( "Minimum Vertices" )]
		[Range( 4, 10, false )]
		[WideMode]
		[Description( "Ignores faces with less than this many sides." )]
		[ShowIf("OperationType", TopologyOperationType.Triangulate)]
		public readonly int MinimumVertices
		{
			get => TriangulateMinimumVertices;
			set => TriangulateMinimumVertices = Math.Max( 4, value );
		}
		
		[Title( "Max Face Angle" )]
		[Range( 0.0f, 180.0f, true )]
		[Step( 0.1f )]
		[WideMode]
		[Description( "The tolerance for difference in face normals in order to be quadrangulated." )]
		[ShowIf("OperationType", TopologyOperationType.Quadrangulate)]
		public readonly float MaxFaceAngle
		{
			get => QuadrangulateMaxFaceAngle;
			set => QuadrangulateMaxFaceAngle = value;
		}

		[Title( "Max Shape Angle" )]
		[Range( 0.0f, 180.0f, true )]
		[Step( 0.1f )]
		[WideMode]
		[Description(
			"The interior angle tolerance for created quads. 0 means only perfect 90 degree interior angles are processed." )]
		[ShowIf("OperationType", TopologyOperationType.Quadrangulate)]
		public readonly float MaxShapeAngle
		{
			get => QuadrangulateMaxShapeAngle;
			set => QuadrangulateMaxShapeAngle = value;
		}

		[Title( "Compare UVs" )]
		[Description( "Limit by non-contiguous UVs" )]
		[ShowIf("OperationType", TopologyOperationType.Quadrangulate)]
		public readonly bool CompareUVs { get => QuadrangulateCompareUVs; set => QuadrangulateCompareUVs = value; }

		[Title( "Compare Vertex Color" )]
		[Description( "Limit by vertex color." )]
		[ShowIf("OperationType", TopologyOperationType.Quadrangulate)]
		public readonly bool CompareVertexColor
		{
			get => QuadrangulateCompareVertexColor;
			set => QuadrangulateCompareVertexColor = value;
		}

		[Title( "Compare Vertex Blend" )]
		[Description( "Limit by vertex blending." )]
		[ShowIf("OperationType", TopologyOperationType.Quadrangulate)]
		public readonly bool CompareVertexBlend
		{
			get => QuadrangulateCompareVertexBlend;
			set => QuadrangulateCompareVertexBlend = value;
		}

		[Title( "Compare Material" )]
		[Description( "Limit by different face materials." )]
		[ShowIf("OperationType", TopologyOperationType.Quadrangulate)]
		public readonly bool CompareFaceMaterial
		{
			get => QuadrangulateCompareFaceMaterial;
			set => QuadrangulateCompareFaceMaterial = value;
		}

		[Title( "Compare Smoothing" )]
		[Description( "Limit by edges marked as hard normals." )]
		[ShowIf("OperationType", TopologyOperationType.Quadrangulate)]
		public readonly bool CompareSmoothing
		{
			get => QuadrangulateCompareSmoothing;
			set => QuadrangulateCompareSmoothing = value;
		}
	}

	public class TopologyToolWidget : ToolSidebarWidget
	{
		private readonly TopologyTool _tool;
		
		[InlineEditor( Label = false )] private readonly TopologyProperties _topologyProperties = new();

		public TopologyToolWidget( TopologyTool tool )
		{
			_tool = tool;

			AddTitle( "Topology Tool", "details" );

			{
				Layout group = AddGroup( "Properties" );
				Layout row = group.AddRow();
				row.Spacing = 8;

				ControlSheet sheet = new();
				ControlWidget control =
					sheet.AddRow( this.GetSerialized().GetProperty( nameof( _topologyProperties ) ) );
				control.OnChildValuesChanged += _ => UpdateMesh();
				row.Add( sheet );

				row = group.AddRow();
				row.Spacing = 4;

				Button apply = new( "Apply", "done" );
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.topology-apply" ) + "]";
				row.Add( apply );

				Button cancel = new( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.topology-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();

			UpdateMesh();
		}
		
		private void UpdateMesh()
		{
			_tool.UpdateTopology( _topologyProperties );
		}

		[Shortcut( "mesh.topology-apply", "enter", ShortcutType.Application )]
		private void Apply()
		{
			_tool.Apply();
		}

		[Shortcut( "mesh.topology-cancel", "ESC", ShortcutType.Application )]
		private void Cancel()
		{
			_tool.Cancel();
		}
	}
}
