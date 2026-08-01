using System;

namespace Editor.MeshEditor;

/// <summary>
/// Creates a cylinder fitted to the drawn bounds.
/// </summary>
[Title( "Cylinder" ), Icon( "circle" )]
internal class CylinderPrimitive : PrimitiveBuilder
{
	[Title( "Number of sides" ), WideMode, Description( "Controls how round the cylinder is." )]
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
			points[i] = Center + new Vector3( MathF.Sin( angle ), MathF.Cos( angle ), -1.0f ) * halfSize;
		}

		mesh.AddFace( points.Select( x => x.WithZ( Center.z + halfSize.z ) ).Reverse().ToArray() ); // top face
		mesh.AddFace( points.Select( x => x.WithZ( Center.z - halfSize.z ) ).ToArray() ); // bottom face

		// sides
		for ( int i = 0; i < NumberOfSides; i++ )
		{
			var nextIndex = (i + 1) % NumberOfSides;
			mesh.AddFace(
				points[i].WithZ( Center.z + halfSize.z ),
				points[nextIndex].WithZ( Center.z + halfSize.z ),
				points[nextIndex].WithZ( Center.z - halfSize.z ),
				points[i].WithZ( Center.z - halfSize.z )
			);
		}
	}
}
