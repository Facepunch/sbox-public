
namespace Editor.MeshEditor;

[Title( "Block" ), Icon( "rectangle" )]
public class BlockPrimitive : PrimitiveBuilder
{
	[Flags]
	public enum Side
	{
		[Hide]
		None = 0,

		Top = 1 << 0,
		Bottom = 1 << 1,
		Left = 1 << 2,
		Right = 1 << 3,
		Front = 1 << 4,
		Back = 1 << 5,

		[Hide]
		All = Top | Bottom | Left | Right | Front | Back
	}

	public Side Sides { get; set; } = Side.All;
	public bool Hollow { get; set; } = false;

	[Hide] private BBox _box;

	public override void SetFromBox( BBox box ) => _box = box;

	public override void Build( PolygonMesh mesh )
	{
		Vector3 mins;
		Vector3 maxs;

		if ( Hollow )
		{
			mins = _box.Maxs;
			maxs = _box.Mins;
		}
		else
		{
			mins = _box.Mins;
			maxs = _box.Maxs;
		}

		bool Has( Side s ) => (Sides & s) != 0;

		if ( Has( Side.Top ) )
		{
			mesh.AddFace(
				new Vector3( mins.x, mins.y, maxs.z ),
				new Vector3( maxs.x, mins.y, maxs.z ),
				new Vector3( maxs.x, maxs.y, maxs.z ),
				new Vector3( mins.x, maxs.y, maxs.z )
			);
		}

		if ( Has( Side.Bottom ) )
		{
			mesh.AddFace(
				new Vector3( mins.x, maxs.y, mins.z ),
				new Vector3( maxs.x, maxs.y, mins.z ),
				new Vector3( maxs.x, mins.y, mins.z ),
				new Vector3( mins.x, mins.y, mins.z )
			);
		}

		if ( Has( Side.Left ) )
		{
			mesh.AddFace(
				new Vector3( mins.x, maxs.y, mins.z ),
				new Vector3( mins.x, mins.y, mins.z ),
				new Vector3( mins.x, mins.y, maxs.z ),
				new Vector3( mins.x, maxs.y, maxs.z )
			);
		}

		if ( Has( Side.Right ) )
		{
			mesh.AddFace(
				new Vector3( maxs.x, maxs.y, maxs.z ),
				new Vector3( maxs.x, mins.y, maxs.z ),
				new Vector3( maxs.x, mins.y, mins.z ),
				new Vector3( maxs.x, maxs.y, mins.z )
			);
		}

		if ( Has( Side.Front ) )
		{
			mesh.AddFace(
				new Vector3( maxs.x, maxs.y, mins.z ),
				new Vector3( mins.x, maxs.y, mins.z ),
				new Vector3( mins.x, maxs.y, maxs.z ),
				new Vector3( maxs.x, maxs.y, maxs.z )
			);
		}

		if ( Has( Side.Back ) )
		{
			mesh.AddFace(
				new Vector3( maxs.x, mins.y, maxs.z ),
				new Vector3( mins.x, mins.y, maxs.z ),
				new Vector3( mins.x, mins.y, mins.z ),
				new Vector3( maxs.x, mins.y, mins.z )
			);
		}
	}
}
