
namespace Editor.MeshEditor;

/// <summary>
/// Creates a box with individually selectable faces.
/// </summary>
[Title( "Box" ), Icon( "rectangle" )]
public class BlockPrimitive : PrimitiveBuilder
{
	[Flags]
	public enum Side
	{
		[Hide]
		None = 0,

		[Title( "+X" )]
		Right = 1 << 3,

		[Title( "-X" )]
		Left = 1 << 2,

		[Title( "+Y" )]
		Front = 1 << 4,

		[Title( "-Y" )]
		Back = 1 << 5,

		[Title( "+Z" )]
		Top = 1 << 0,

		[Title( "-Z" )]
		Bottom = 1 << 1,

		[Hide]
		All = Top | Bottom | Left | Right | Front | Back
	}

	public enum Option
	{
		[Description( "Create outward-facing polygons." )]
		Solid,

		[Description( "Face the selected sides inward." )]
		Hollow,

		[Title( "Two Sided" ), Description( "Create both inward and outward-facing polygons." )]
		TwoSided
	}

	[Editor( "horizontal-box-sides" ), WideMode, Description( "Choose which sides of the box are created." )]
	public Side Sides { get; set; } = Side.All;

	[EnumButtonGroup, WideMode, Description( "Choose how the selected sides are created." )]
	public Option Options { get; set; }

	[Hide] private BBox _box;

	public override void SetFromBox( BBox box ) => _box = box;

	public override Widget CreatePropertyEditor( SerializedObject properties )
	{
		var widget = new Widget
		{
			Layout = Layout.Column()
		};

		AddControl( properties.GetProperty( nameof( Sides ) ) );
		widget.Layout.AddSpacingCell( 8 );
		AddControl( properties.GetProperty( nameof( Options ) ) );

		return widget;

		void AddControl( SerializedProperty property )
		{
			var control = ControlWidget.Create( property );
			control.ToolTip = ControlSheetFormatter.GetPropertyToolTip( property );
			widget.Layout.Add( control );
		}
	}

	public override void Build( PolygonMesh mesh )
	{
		var mins = _box.Mins;
		var maxs = _box.Maxs;
		var innerMesh = Options is Option.TwoSided ? new PolygonMesh() : null;

		bool HasSide( Side side ) => (Sides & side) != 0;

		void AddFace( params Vector3[] vertices )
		{
			if ( Options is Option.Hollow )
				Array.Reverse( vertices );

			mesh.AddFace( vertices );

			innerMesh?.AddFace( vertices.Reverse().ToArray() );
		}

		if ( HasSide( Side.Top ) )
		{
			AddFace(
				new Vector3( mins.x, mins.y, maxs.z ),
				new Vector3( maxs.x, mins.y, maxs.z ),
				new Vector3( maxs.x, maxs.y, maxs.z ),
				new Vector3( mins.x, maxs.y, maxs.z )
			);
		}

		if ( HasSide( Side.Bottom ) )
		{
			AddFace(
				new Vector3( mins.x, maxs.y, mins.z ),
				new Vector3( maxs.x, maxs.y, mins.z ),
				new Vector3( maxs.x, mins.y, mins.z ),
				new Vector3( mins.x, mins.y, mins.z )
			);
		}

		if ( HasSide( Side.Left ) )
		{
			AddFace(
				new Vector3( mins.x, maxs.y, mins.z ),
				new Vector3( mins.x, mins.y, mins.z ),
				new Vector3( mins.x, mins.y, maxs.z ),
				new Vector3( mins.x, maxs.y, maxs.z )
			);
		}

		if ( HasSide( Side.Right ) )
		{
			AddFace(
				new Vector3( maxs.x, maxs.y, maxs.z ),
				new Vector3( maxs.x, mins.y, maxs.z ),
				new Vector3( maxs.x, mins.y, mins.z ),
				new Vector3( maxs.x, maxs.y, mins.z )
			);
		}

		if ( HasSide( Side.Front ) )
		{
			AddFace(
				new Vector3( maxs.x, maxs.y, mins.z ),
				new Vector3( mins.x, maxs.y, mins.z ),
				new Vector3( mins.x, maxs.y, maxs.z ),
				new Vector3( maxs.x, maxs.y, maxs.z )
			);
		}

		if ( HasSide( Side.Back ) )
		{
			AddFace(
				new Vector3( maxs.x, mins.y, maxs.z ),
				new Vector3( mins.x, mins.y, maxs.z ),
				new Vector3( mins.x, mins.y, mins.z ),
				new Vector3( maxs.x, mins.y, mins.z )
			);
		}

		if ( innerMesh is not null )
			// Keep the inner shell welded to itself but disconnected from the coincident outer shell.
			mesh.AddMesh( innerMesh );
	}
}
