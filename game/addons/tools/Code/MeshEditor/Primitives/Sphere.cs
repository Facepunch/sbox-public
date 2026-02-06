using System;

namespace Editor.MeshEditor;

[Title( "Sphere" ), Icon( "sports_baseball" )]
internal class SpherePrimitive : PrimitiveBuilder
{
	[Title( "Number of sides" )]
	public int NumberOfSides { get; set; } = 8;

	[Hide] public Vector3 Center { get; set; }
	[Hide] public Vector3 Size { get; set; }

	public override void SetFromBox( BBox box )
	{
		Center = box.Center;
		Size = box.Size;
	}

	public override void Build( PolygonMesh mesh )
	{
		//
		// Build the sphere by building slices at constant angular intervals.
		// 
		// Each slice is a ring of four-sided faces, except for the top and bottom slices,
		// which are flattened cones.
		//
		// Unrolled, a sphere made with 5 'sides' has 25 faces and looks like this:
		//				
		//			/\  /\  /\  /\  /\
		//		   / 0\/ 1\/ 2\/ 3\/ 4\
		//		  |  5|  6|  7|  8|  9| 	
		//		  | 10| 11| 12| 13| 14| 	
		//		  | 15| 16| 17| 18| 19| 	
		//		   \20/\21/\22/\23/\24/
		//			\/  \/  \/  \/  \/
		//

		var halfSize = Size / 2;

		float angle = 0.0f;
		float angleStep = MathF.PI / NumberOfSides;

		for ( int slice = 0; slice < NumberOfSides; slice++ )
		{
			float angle1 = angle + angleStep;

			// Make the upper polygon.
			var upperPoints = new Vector3[NumberOfSides + 1];
			{
				for ( int i = 0; i < NumberOfSides; i++ )
				{
					var angle2 = i * (MathF.PI * 2.0f / NumberOfSides);
					upperPoints[i] = Center + new Vector3( MathF.Sin( angle2 ), MathF.Cos( angle2 ), -1.0f ) * halfSize * MathF.Sin( angle );
				}

				upperPoints[NumberOfSides] = upperPoints[0];
			}

			// Make the lower polygon.
			var lowerPoints = new Vector3[NumberOfSides + 1];
			{
				for ( int i = 0; i < NumberOfSides; i++ )
				{
					var angle2 = i * (MathF.PI * 2.0f / NumberOfSides);
					lowerPoints[i] = Center + new Vector3( MathF.Sin( angle2 ), MathF.Cos( angle2 ), -1.0f ) * halfSize * MathF.Sin( angle1 );
				}

				lowerPoints[NumberOfSides] = lowerPoints[0];
			}

			float upperHeight = Center.z + halfSize.z * MathF.Cos( angle );
			float lowerHeight = Center.z + halfSize.z * MathF.Cos( angle1 );

			for ( int i = 0; i < NumberOfSides; i++ )
			{
				// Top and bottom are cones, not rings
				if ( slice == 0 )
				{
					mesh.AddFace(
						upperPoints[i + 1].WithZ( upperHeight ),
						lowerPoints[i + 1].WithZ( lowerHeight ),
						lowerPoints[i].WithZ( lowerHeight )
					);
				}
				else if ( slice == NumberOfSides - 1 )
				{
					mesh.AddFace(
						upperPoints[i].WithZ( upperHeight ),
						upperPoints[i + 1].WithZ( upperHeight ),
						lowerPoints[i + 1].WithZ( lowerHeight )
					);
				}
				else
				{
					mesh.AddFace(
						upperPoints[i].WithZ( upperHeight ),
						upperPoints[i + 1].WithZ( upperHeight ),
						lowerPoints[i + 1].WithZ( lowerHeight ),
						lowerPoints[i].WithZ( lowerHeight )
					);
				}
			}

			angle += angleStep;
		}
	}
}
