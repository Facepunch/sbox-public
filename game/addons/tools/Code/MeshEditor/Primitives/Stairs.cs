using System;

namespace Editor.MeshEditor;

/// <summary>
/// Creates a staircase fitted to the drawn bounds.
/// </summary>
[Title( "Stairs" ), Icon( "stairs" )]
public class StairsPrimitive : PrimitiveBuilder
{
	[Title( "Number of steps" ), Range( 2, 64 ), WideMode, Description( "Controls the number of steps in the staircase." )]
	public int NumberOfSteps { get; set; } = 16;

	[Hide] private Vector3 Center;
	[Hide] private Vector3 Size;
	[Hide] private float StepWidth;
	[Hide] private float StepDepth;
	[Hide] private float StepHeight;

	public override void SetFromBox( BBox box )
	{
		NumberOfSteps = Math.Max( NumberOfSteps, 2 );
		Size = box.Size;
		Center = box.Mins;
		StepHeight = Size.z / NumberOfSteps;
		StepDepth = Size.x / NumberOfSteps;
		StepWidth = Size.y;
	}

	public override void Build( PolygonMesh mesh )
	{
		var vertices = new Vector3[(NumberOfSteps + 1) * 2];

		for ( var stepIndex = 0; stepIndex <= NumberOfSteps; stepIndex++ )
		{
			float depth = StepDepth * stepIndex;
			var innerPoint = Vector3.Forward * depth;
			var outerPoint = Vector3.Left * StepWidth + Vector3.Forward * depth;

			vertices[stepIndex * 2] = Center + innerPoint;
			vertices[stepIndex * 2 + 1] = Center + outerPoint;
		}

		for ( var stepIndex = 0; stepIndex < NumberOfSteps; stepIndex++ )
		{
			var lastHeightOffset = Center.z + StepHeight * stepIndex;
			var stepHeightOffset = Center.z + StepHeight * (stepIndex + 1);

			var innerPoint = vertices[stepIndex * 2].WithZ( stepHeightOffset );
			var outerPoint = vertices[stepIndex * 2 + 1].WithZ( stepHeightOffset );

			var innerPoint2 = vertices[(stepIndex + 1) * 2].WithZ( stepHeightOffset );
			var outerPoint2 = vertices[(stepIndex + 1) * 2 + 1].WithZ( stepHeightOffset );

			var lastInnerPoint = vertices[(stepIndex) * 2].WithZ( lastHeightOffset );
			var lastOuterPoint = vertices[(stepIndex) * 2 + 1].WithZ( lastHeightOffset );

			mesh.AddFace(
				innerPoint2,
				outerPoint2,
				outerPoint,
				innerPoint
			);

			mesh.AddFace(
				innerPoint,
				outerPoint,
				lastOuterPoint,
				lastInnerPoint
			);

			mesh.AddFace(
				vertices[(stepIndex + 1) * 2],
				innerPoint2,
				innerPoint,
				vertices[stepIndex * 2]
			);

			mesh.AddFace(
				outerPoint,
				outerPoint2,
				vertices[(stepIndex + 1) * 2 + 1],
				vertices[stepIndex * 2 + 1]
			);
		}

		var backInnerPoint = vertices[^2].WithZ( Center.z + Size.z );
		var backOuterPoint = vertices[^1].WithZ( Center.z + Size.z );

		mesh.AddFace(
			vertices[^2],
			vertices[^1],
			backOuterPoint,
			backInnerPoint
		);
	}
}
