using System;

namespace Editor.MeshEditor;

[Title( "Cylinder" ), Icon( "circle" )]
internal class CylinderPrimitive : PrimitiveBuilder
{
	[Title( "Number of sides" )]
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
		var sides = Math.Max( NumberOfSides, 2 );
		var points = new Vector3[sides];
		var halfSize = Size / 2;

		for ( int i = 0; i < sides; i++ )
		{
			var angle = i * (MathF.PI * 2.0f / sides);
			points[i] = Center + new Vector3( MathF.Sin( angle ), MathF.Cos( angle ), -1.0f ) * halfSize;
		}

		mesh.AddFace( points.Select( x => x.WithZ( Center.z + halfSize.z ) ).Reverse().ToArray() ); // top face
		mesh.AddFace( points.Select( x => x.WithZ( Center.z - halfSize.z ) ).ToArray() ); // bottom face

		// sides
		for ( int i = 0; i < sides; i++ )
		{
			var nextIndex = (i + 1) % sides;
			mesh.AddFace(
				points[i].WithZ( Center.z + halfSize.z ),
				points[nextIndex].WithZ( Center.z + halfSize.z ),
				points[nextIndex].WithZ( Center.z - halfSize.z ),
				points[i].WithZ( Center.z - halfSize.z )
			);
		}
	}
}
