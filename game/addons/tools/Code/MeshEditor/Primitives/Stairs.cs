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

	[Title( "Align to Camera" ), WideMode, Description( "Rotate the staircase so it ascends away from the camera." )]
	public bool AlignToCamera { get; set; } = true;

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
		var run = Vector3.Forward;
		var width = Vector3.Left;
		var origin = Center;
		var stepDepth = StepDepth;
		var stepWidth = StepWidth;

		if ( AlignToCamera )
		{
			var forward = CameraForward;
			var maxs = Center + Size;

			if ( MathF.Abs( forward.y ) > MathF.Abs( forward.x ) )
			{
				run = forward.y >= 0.0f ? Vector3.Left : Vector3.Right;
				width = Vector3.Forward;
				origin = forward.y >= 0.0f ? Center : Center.WithY( maxs.y );
				stepDepth = Size.y / NumberOfSteps;
				stepWidth = Size.x;
			}
			else if ( forward.x < 0.0f )
			{
				run = Vector3.Backward;
				origin = Center.WithX( maxs.x );
			}
		}

		var flip = Vector3.Cross( run, width ).z < 0.0f;

		void AddFace( params Vector3[] face )
		{
			if ( flip )
				Array.Reverse( face );

			mesh.AddFace( face );
		}

		var vertices = new Vector3[(NumberOfSteps + 1) * 2];

		for ( var stepIndex = 0; stepIndex <= NumberOfSteps; stepIndex++ )
		{
			float depth = stepDepth * stepIndex;
			var innerPoint = run * depth;
			var outerPoint = width * stepWidth + run * depth;

			vertices[stepIndex * 2] = origin + innerPoint;
			vertices[stepIndex * 2 + 1] = origin + outerPoint;
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

			AddFace(
				innerPoint2,
				outerPoint2,
				outerPoint,
				innerPoint
			);

			AddFace(
				innerPoint,
				outerPoint,
				lastOuterPoint,
				lastInnerPoint
			);

			AddFace(
				vertices[(stepIndex + 1) * 2],
				innerPoint2,
				innerPoint,
				vertices[stepIndex * 2]
			);

			AddFace(
				outerPoint,
				outerPoint2,
				vertices[(stepIndex + 1) * 2 + 1],
				vertices[stepIndex * 2 + 1]
			);
		}

		var backInnerPoint = vertices[^2].WithZ( Center.z + Size.z );
		var backOuterPoint = vertices[^1].WithZ( Center.z + Size.z );

		AddFace(
			vertices[^2],
			vertices[^1],
			backOuterPoint,
			backInnerPoint
		);
	}

	/// <summary>
	/// Forward direction of the scene view camera, used to orient primitives
	/// towards whoever is placing them.
	/// </summary>
	private static Vector3 CameraForward
	{
		get
		{
			var viewport = SceneViewWidget.Current?.LastSelectedViewportWidget;
			return viewport?.State is null
				? Vector3.Forward
				: viewport.State.CameraRotation.Forward;
		}
	}
}
