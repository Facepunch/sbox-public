using System;

namespace Editor.MeshEditor;

/// <summary>
/// Creates a pointed cone fitted to the drawn bounds.
/// </summary>
[Title( "Spike" ), Icon( "details" )]
internal class SpikePrimitive : PrimitiveBuilder
{
	[Title( "Number of sides" ), WideMode, Description( "Controls how round the base of the spike is." )]
	public int NumberOfSides { get; set; } = 16;

	[Hide] public Vector3 Center { get; set; }
	[Hide] public Vector3 Size { get; set; }

	public override void SetFromBox( BBox box )
	{
		Center = box.Center;
		Size = box.Size;
	}

	public override void Build( PolygonMesh mesh )
	{
		var points = new Vector3[NumberOfSides];
		var halfSize = Size / 2;

		for ( int i = 0; i < NumberOfSides; i++ )
		{
			var angle = i * (MathF.PI * 2.0f / NumberOfSides);
			var point = new Vector3( MathF.Sin( angle ), MathF.Cos( angle ), -1.0f ) * halfSize;
			points[i] = Center + point;
		}

		mesh.AddFace( points.Select( x => new Vector3( x.x, x.y, Center.z - halfSize.z ) ).ToArray() ); // bottom face

		var topCenter = new Vector3( Center.x, Center.y, Center.z + halfSize.z );

		// sides
		for ( int i = 0; i < NumberOfSides; i++ )
		{
			var nextIndex = (i + 1) % NumberOfSides;
			mesh.AddFace( topCenter, points[nextIndex], points[i] );
		}
	}
}
