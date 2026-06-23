namespace Editor.MeshEditor;

public partial class QuadrangulateTool
{
	public static float QuadrangulateMaxFaceAngle { get; set => field = value.Clamp( 0, 180f ); } = 40.0f;
	public static float QuadrangulateMaxShapeAngle { get; set => field = value.Clamp( 0, 180f ); } = 40.0f;
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
		[InlineEditor( Label = false )] private readonly QuadrangulateProperties _quadrangulateProperties = new();

		private readonly QuadrangulateTool _tool;

		public QuadrangulateToolWidget( QuadrangulateTool tool )
		{
			_tool = tool;

			AddTitle( "Quadrangulate Tool", "crop_square" );

			{
				Layout group = AddGroup( "Properties" );
				Layout row = group.AddRow();
				row.Spacing = 8;

				ControlSheet sheet = new();
				ControlWidget control =
					sheet.AddRow( this.GetSerialized().GetProperty( nameof(_quadrangulateProperties) ) );
				control.OnChildValuesChanged += _ => UpdateMesh();
				row.Add( sheet );

				row = group.AddRow();
				row.Spacing = 4;

				Button apply = new("Apply", "done");
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.quadrangulate-apply" ) + "]";
				row.Add( apply );

				Button cancel = new("Cancel", "close");
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.quadrangulate-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();

			UpdateMesh();
		}

		private void UpdateMesh()
		{
			_tool.UpdateQuadrangulate( QuadrangulateMaxFaceAngle, QuadrangulateMaxShapeAngle, QuadrangulateCompareUVs,
				QuadrangulateCompareVertexColor,
				QuadrangulateCompareVertexBlend, QuadrangulateCompareFaceMaterial, QuadrangulateCompareSmoothing );
		}

		[Shortcut( "mesh.quadrangulate-apply", "enter", ShortcutType.Application )]
		private void Apply()
		{
			_tool.Apply();
		}

		[Shortcut( "mesh.quadrangulate-cancel", "ESC", ShortcutType.Application )]
		private void Cancel()
		{
			_tool.Cancel();
		}

		private struct QuadrangulateProperties
		{
			[Title( "Max Face Angle" )]
			[Range( 0.0f, 180.0f, true )]
			[Step( 0.1f )]
			[WideMode]
			[Description( "The tolerance for difference in face normals in order to be quadrangulated." )]
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
			public readonly float MaxShapeAngle
			{
				get => QuadrangulateMaxShapeAngle;
				set => QuadrangulateMaxShapeAngle = value;
			}

			[Title( "Compare UVs" )]
			[Description( "Limit by non-contiguous UVs" )]
			public readonly bool CompareUVs { get => QuadrangulateCompareUVs; set => QuadrangulateCompareUVs = value; }

			[Title( "Compare Vertex Color" )]
			[Description( "Limit by vertex color." )]
			public readonly bool CompareVertexColor
			{
				get => QuadrangulateCompareVertexColor;
				set => QuadrangulateCompareVertexColor = value;
			}

			[Title( "Compare Vertex Blend" )]
			[Description( "Limit by vertex blending." )]
			public readonly bool CompareVertexBlend
			{
				get => QuadrangulateCompareVertexBlend;
				set => QuadrangulateCompareVertexBlend = value;
			}

			[Title( "Compare Material" )]
			[Description( "Limit by different face materials." )]
			public readonly bool CompareFaceMaterial
			{
				get => QuadrangulateCompareFaceMaterial;
				set => QuadrangulateCompareFaceMaterial = value;
			}

			[Title( "Compare Smoothing" )]
			[Description( "Limit by edges marked as hard normals." )]
			public readonly bool CompareSmoothing
			{
				get => QuadrangulateCompareSmoothing;
				set => QuadrangulateCompareSmoothing = value;
			}
		}
	}
}
