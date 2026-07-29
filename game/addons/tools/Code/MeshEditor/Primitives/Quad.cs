
namespace Editor.MeshEditor;

/// <summary>
/// Creates a flat rectangular surface.
/// </summary>
[Title( "Quad" ), Icon( "rectangle" )]
public class QuadPrimitive : PrimitiveBuilder
{
	[Hide] public override bool Is2D => true;
	[Hide] private BBox _box;

	[WideMode, Description( "Create a second face pointing in the opposite direction." )]
	public bool HasBackface { get; set; } = false;

	public override void SetFromBox( BBox box ) => _box = box;

	public override void Build( PolygonMesh mesh )
	{
		var mins = _box.Mins;
		var maxs = _box.Maxs;

		if ( _box.Size.x <= 0.0f )
		{
			// YZ plane
			mesh.AddFace(
				new Vector3( _box.Center.x, mins.y, mins.z ),
				new Vector3( _box.Center.x, maxs.y, mins.z ),
				new Vector3( _box.Center.x, maxs.y, maxs.z ),
				new Vector3( _box.Center.x, mins.y, maxs.z )
			);
			if ( HasBackface )
			{
				mesh.AddFace(
					new Vector3( _box.Center.x, mins.y, mins.z ),
					new Vector3( _box.Center.x, mins.y, maxs.z ),
					new Vector3( _box.Center.x, maxs.y, maxs.z ),
					new Vector3( _box.Center.x, maxs.y, mins.z )
				);
			}
		}
		else if ( _box.Size.y <= 0.0f )
		{
			// XZ Plane
			mesh.AddFace(
				new Vector3( mins.x, _box.Center.y, mins.z ),
				new Vector3( mins.x, _box.Center.y, maxs.z ),
				new Vector3( maxs.x, _box.Center.y, maxs.z ),
				new Vector3( maxs.x, _box.Center.y, mins.z )
			);
			if ( HasBackface )
			{
				mesh.AddFace(
					new Vector3( mins.x, _box.Center.y, mins.z ),
					new Vector3( maxs.x, _box.Center.y, mins.z ),
					new Vector3( maxs.x, _box.Center.y, maxs.z ),
					new Vector3( mins.x, _box.Center.y, maxs.z )
				);
			}
		}
		else
		{
			// XY Plane
			mesh.AddFace(
				new Vector3( mins.x, mins.y, _box.Center.z ),
				new Vector3( maxs.x, mins.y, _box.Center.z ),
				new Vector3( maxs.x, maxs.y, _box.Center.z ),
				new Vector3( mins.x, maxs.y, _box.Center.z )
			);
			if ( HasBackface )
			{
				mesh.AddFace(
					new Vector3( mins.x, mins.y, _box.Center.z ),
					new Vector3( mins.x, maxs.y, _box.Center.z ),
					new Vector3( maxs.x, maxs.y, _box.Center.z ),
					new Vector3( maxs.x, mins.y, _box.Center.z )
				);
			}
		}
	}
}
