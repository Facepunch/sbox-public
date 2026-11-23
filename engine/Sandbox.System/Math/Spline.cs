using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Collection of curves in 3D space.
/// Shape and behavior of the curves are controled through points <see cref="Spline.Point"/>, each with customizable handles, roll, scale, and up vectors.
/// Two consecutive points define a segment/curve of the spline.
/// <br /><br />
/// By adjusting the handles both smooth and sharp corners can be created.
/// The spline can also be turned into a loop, combined with linear tangents this can be used to create polygons.
/// Splines can also be used used for animations, camera movements, marking areas, or procedural geometry generation.
/// </summary>
public class Spline
{
	/// <summary>
	/// Point that defines part of the spline.
	/// Two consecutive points define a segment of the spline.
	/// The <see cref="Position" />,  <see cref="In" />/<see cref="Out" /> Handles and <see cref="Mode"></see> / properties are used to define the shape of the spline.
	/// <code>
	///                  P1 (Position)                         
	///       P1 (In)           ▼           P1 (Out)                      
	///               o──────═══X═══──────o                    
	///                  ───/       \───                      
	///               ──/               \──                   
	///             -/                     \-                  
	///            /                         \                 
	///           |                           |
	///       P0  X                           X  P2
	/// </code>
	/// </summary>
	[JsonConverter( typeof( Sandbox.Internal.JsonConvert.SplinePointConverter ) )]
	public struct Point
	{
		/// <summary>
		/// The position of the spline point.
		/// </summary>
		public Vector3 Position;

		/// <summary>
		/// Position of the In handle relative to the point position.
		/// </summary>
		public Vector3 In;

		/// <summary>
		/// Position of the Out handle relative to the point position.
		/// </summary>
		public Vector3 Out;

		/// <summary>
		/// Describes how the spline should behave when entering/leaving a point.
		/// The mmode and the handles In and Out position will determine the transition between segments.
		/// </summary>
		public HandleMode Mode = HandleMode.Auto;

		/// <summary>
		/// Roll/Twist around the tangent axis.
		/// </summary>
		public float Roll = 0f;

		/// <summary>
		/// X = Scale Length, Y = Scale Width, Z = Scale Height
		/// </summary>
		public Vector3 Scale = Vector3.One;

		/// <summary>
		/// Custom up vector at a spline point, can be used to calculate tangent frames (transforms) along the spline.
		/// This allows fine grained control over the orientation of objects following the spline.
		/// </summary>
		public Vector3 Up = Vector3.Up;


		public Point()
		{

		}

		public override string ToString()
		{
			return $"Position: {Position}, In: {In}, Out: {Out}, Mode: {Mode}, Roll: {Roll}, Scale: {Scale}, Up: {Up}";
		}
	}

	/// <summary>
	/// Describes how the spline should behave when entering/leaving a point.
	/// </summary>
	public enum HandleMode
	{
		/// <summary>
		/// Handle positions are calculated automatically
		/// based on the location of adjacent points.
		/// </summary>
		[Icon( "auto_fix_high" )]
		Auto,
		/// <summary>
		/// Handle positions are set to zero, leading to a sharp corner.
		/// </summary>
		[Icon( "show_chart" )]
		Linear,
		/// <summary>
		/// The In and Out handles are user set, but are linked (mirrored).
		/// </summary>
		[Icon( "open_in_full" )]
		Mirrored,
		/// <summary>
		/// The In and Out handle are user set and operate independently.
		/// </summary>
		[Icon( "call_split" )]
		Split,
	}

	// private because we need to ensure the points are always in a valid state
	[JsonInclude, JsonPropertyName( "Points" )]
	private List<Point> _points = new();

	private ReadOnlyCollection<Point> _pointsView;
	private List<Point> _pointsViewOf;

	// One wrapper for the queries instead of one per call. Deserialization can swap the list, so it follows _points.
	private ReadOnlyCollection<Point> PointsView
	{
		get
		{
			var points = _points;
			if ( _pointsViewOf != points )
			{
				_pointsView = points.AsReadOnly();
				_pointsViewOf = points;
			}

			return _pointsView;
		}
	}

	private bool _areDistancesSampled = false;

	/// <summary>
	/// Invoked everytime the spline shape or the properties of the spline change.
	/// </summary>
	public Action SplineChanged;

	private SplineUtils.SplineSampler _distanceSampler = new();

	private void RequiresDistanceResample()
	{
		_areDistancesSampled = false;
		SplineChanged?.Invoke();
	}

	private void SampleDistances()
	{
		_distanceSampler.Sample( PointsView );
		_areDistancesSampled = true;
	}

	private void EnsureSplineIsDistanceSampled()
	{
		if ( _areDistancesSampled )
		{
			return;
		}
		SampleDistances();
	}

	/// <summary>
	/// Whether the spline forms a loop.
	/// </summary>
	[Property, JsonIgnore]
	public bool IsLoop
	{
		get => SplineUtils.IsLoop( PointsView );
		set
		{
			var isAlreadyLoop = SplineUtils.IsLoop( PointsView );
			// We emulate loops by adding an addtional point at the end which matches the first point
			// this might seem hacky at first but it makes things so much easier downstream,
			// because we can handle open splines and looped splines exactly the same when doing complex calculations
			// The fact that the last point exists will be hidden from the user in the Editor and API
			if ( value && !isAlreadyLoop )
			{
				_points.Add( _points[0] );
				RequiresDistanceResample();
			}
			else if ( !value && isAlreadyLoop )
			{
				_points.RemoveAt( _points.Count - 1 );
				RequiresDistanceResample();
			}
		}
	}

	/// <summary>
	/// Information about the spline at a specific distance.
	/// </summary>
	public struct Sample
	{
		public Vector3 Position;
		public Vector3 Tangent;
		public float Roll;
		public Vector3 Scale;
		public Vector3 Up;
		public float Distance;
	}

	/// <summary>
	/// Calculates a bunch of information about the spline at a specific distance.
	/// </summary>
	public Sample SampleAtDistance( float distance )
	{
		EnsureSplineIsDistanceSampled();
		var splineParams = _distanceSampler.CalculateSegmentParamsAtDistance( distance );
		var distanceAlongSegment = distance - _distanceSampler.GetSegmentStartDistance( splineParams.Index );
		var segmentLength = _distanceSampler.GetSegmentLength( splineParams.Index );
		var position = SplineUtils.GetPosition( PointsView, splineParams );
		var tangent = SplineUtils.GetTangent( PointsView, splineParams );
		var roll = MathX.Lerp( _points[splineParams.Index].Roll, _points[splineParams.Index + 1].Roll, distanceAlongSegment / segmentLength );
		var scale = Vector3.Lerp( _points[splineParams.Index].Scale, _points[splineParams.Index + 1].Scale, distanceAlongSegment / segmentLength );
		var upVector = Vector3.Lerp( _points[splineParams.Index].Up, _points[splineParams.Index + 1].Up, distanceAlongSegment / segmentLength );
		return new Sample
		{
			Position = position,
			Tangent = tangent,
			Roll = roll,
			Scale = scale,
			Up = upVector,
			Distance = distance
		};
	}

	/// <summary>
	/// Calculates a bunch of information about the spline at the position closest to the specified position.
	/// </summary>
	public Sample SampleAtClosestPosition( Vector3 position )
	{
		var distance = FindDistanceClosestToPosition( position );
		return SampleAtDistance( distance );
	}

	/// <summary>
	/// Total length of the spline.
	/// </summary>
	[JsonIgnore] // Ignored because we compute it
	public float Length
	{
		get
		{
			EnsureSplineIsDistanceSampled();
			return _distanceSampler.TotalLength();
		}
	}

	/// <summary>
	/// Total bounds of the spline.
	/// </summary>
	[JsonIgnore] // Ignored because we compute it
	public BBox Bounds
	{
		get
		{
			EnsureSplineIsDistanceSampled();
			return _distanceSampler.GetTotalBounds();
		}
	}

	/// <summary>
	/// Fetches how far along the spline a point is.
	/// </summary>
	public float GetDistanceAtPoint( int pointIndex )
	{
		CheckPointIndex( pointIndex );
		EnsureSplineIsDistanceSampled();

		if ( pointIndex == _points.Count - 1 )
		{
			return _distanceSampler.TotalLength();
		}
		return _distanceSampler.GetSegmentStartDistance( pointIndex );
	}

	/// <summary>
	/// Fetches the length of an individual spline segment.
	/// </summary>
	public float GetSegmentLength( int segmentIndex )
	{
		CheckSegmentIndex( segmentIndex );
		EnsureSplineIsDistanceSampled();

		return _distanceSampler.GetSegmentLength( segmentIndex );
	}

	/// <summary>
	/// Bounds of an individual spline segment.
	/// </summary>
	public BBox GetSegmentBounds( int segmentIndex )
	{
		CheckSegmentIndex( segmentIndex );
		EnsureSplineIsDistanceSampled();

		return _distanceSampler.GetSegmentBounds( segmentIndex );
	}

	/// <summary>
	/// Access the information about a spline point.
	/// </summary>
	public Point GetPoint( int pointIndex )
	{
		CheckPointIndex( pointIndex );

		return _points[pointIndex];
	}

	/// <summary>
	/// Number of points in the spline.
	/// </summary>
	[JsonIgnore] // Ignored because we compute it
	public int PointCount => IsLoop ? _points.Count - 1 : _points.Count;

	/// <summary>
	/// Number of segments in the spline, a spline contains one less segment than points.
	/// </summary>
	[JsonIgnore] // Ignored because we compute it
	public int SegmentCount => SplineUtils.SegmentNum( PointsView );

	/// <summary>
	/// Update the information stored at a spline point.
	/// </summary>
	public void UpdatePoint( int pointIndex, Point updatedPoint )
	{
		CheckPointIndex( pointIndex );

		_points[pointIndex] = updatedPoint;

		RecalculateHandlesForPointAndAdjacentPoints( pointIndex );

		RequiresDistanceResample();
	}

	/// <summary>
	/// Adds a point at an index
	/// </summary>
	public void InsertPoint( int pointIndex, Point newPoint )
	{
		CheckInsertPointIndex( pointIndex );

		_points.Insert( pointIndex, newPoint );

		RecalculateHandlesForPointAndAdjacentPoints( pointIndex );

		RequiresDistanceResample();
	}

	/// <summary>
	/// Adds a point to the end of the spline.
	/// </summary>
	public void AddPoint( Point newPoint )
	{
		_points.Add( newPoint );
		RecalculateHandlesForPointAndAdjacentPoints( _points.Count - 1 );
		RequiresDistanceResample();
	}

	/// <summary>
	/// Adds a point at a specific distance along the spline.
	/// Returns the index of the added spline point.
	/// Tangents of the new point and adjacent points will be calculated so the spline shape remains the same.
	/// Unless inferTangentModes is set to true, in which case the tangent modes will be inferred from the adjacent points.
	/// </summary>
	public int AddPointAtDistance( float distance, bool inferTangentModes = false )
	{
		EnsureSplineIsDistanceSampled();

		var splineParams = _distanceSampler.CalculateSegmentParamsAtDistance( distance );

		var distanceParam = (distance - _distanceSampler.GetSegmentStartDistance( splineParams.Index )) / _distanceSampler.GetSegmentLength( splineParams.Index );

		var positionSplitResult = SplineUtils.SplitSegment( PointsView, splineParams, distanceParam );

		// modify points before and after the split
		_points[splineParams.Index] = positionSplitResult.Left;
		_points[splineParams.Index + 1] = positionSplitResult.Right;

		var newPointIndex = splineParams.Index + 1;

		_points.Insert( newPointIndex, positionSplitResult.Mid );

		RecalculateHandlesForPointAndAdjacentPoints( newPointIndex );

		RequiresDistanceResample();

		return newPointIndex;
	}

	/// <summary>
	/// Removes the point at the specified index.
	/// </summary>
	public void RemovePoint( int pointIndex )
	{
		CheckPointIndex( pointIndex );

		_points.RemoveAt( pointIndex );

		if ( pointIndex - 1 >= 0 )
		{
			RecalculateHandlesForPoint( pointIndex - 1 );
		}

		if ( pointIndex < _points.Count )
		{
			RecalculateHandlesForPoint( pointIndex );
		}

		RequiresDistanceResample();
	}

	/// <summary>
	/// Removes all points from the spline.
	/// </summary>
	public void Clear()
	{
		_points.Clear();

		RequiresDistanceResample();
	}

	/// <summary>
	/// Can be used to get information via GetPositionAtDistance and GetTangentAtDistance etc.
	/// </summary>
	private float FindDistanceClosestToPosition( Vector3 position )
	{
		EnsureSplineIsDistanceSampled();

		var splineParamsForClosestPosition = SplineUtils.FindSegmentAndTClosestToPosition( PointsView, position );

		return _distanceSampler.GetDistanceAtSplineParams( splineParamsForClosestPosition );
	}

	/// <summary>
	/// Converts the spline to a polyline, can pass in buffer as parameter to avoid reallocations.
	/// </summary>
	public void ConvertToPolyline( ref List<Vector3> outPolyLine )
	{
		outPolyLine.Clear();

		EnsureSplineIsDistanceSampled();

		SplineUtils.ConvertSplineToPolyLineWithCachedSampler( PointsView, ref outPolyLine, _distanceSampler, 0.1f );
	}

	/// <summary>
	/// Converts the spline to a polyline.
	/// </summary>
	public List<Vector3> ConvertToPolyline()
	{
		var outPolyLine = new List<Vector3>();
		ConvertToPolyline( ref outPolyLine );
		return outPolyLine;
	}

	// Internal for now no need to expose this yet without, spline deformers
	internal Transform[] CalculateTangentFramesUsingUpDir( int frameCount )
	{
		Transform[] frames = new Transform[frameCount];

		float totalSplineLength = Length;

		Sample sample = SampleAtDistance( 0f );
		sample.Up = Vector3.Up;

		// Choose an initial up vector if tangent is parallel to Up
		if ( MathF.Abs( Vector3.Dot( sample.Tangent, sample.Up ) ) > 0.999f )
		{
			sample.Up = Vector3.Right;
		}

		for ( int i = 0; i < frameCount; i++ )
		{
			float t = frameCount > 1 ? (float)i / (frameCount - 1) : 0f;
			float distance = t * totalSplineLength;

			sample = SampleAtDistance( distance );

			// Apply roll
			var newUp = Rotation.FromAxis( sample.Tangent, sample.Roll ) * sample.Up;

			Rotation rotation = Rotation.LookAt( sample.Tangent, newUp );

			frames[i] = new Transform( sample.Position, rotation, sample.Scale );
		}

		return frames;
	}

	// Internal for now no need to expose this yet without spline deformers
	internal Transform[] CalculateRotationMinimizingTangentFrames( int frameCount )
	{
		Transform[] frames = new Transform[frameCount];

		float totalSplineLength = Length;

		// Initialize the up vector
		Sample previousSample = SampleAtDistance( 0f );
		Vector3 up = Vector3.Up;

		// Choose an initial up vector if tangent is parallel to Up
		if ( MathF.Abs( Vector3.Dot( previousSample.Tangent, up ) ) > 0.999f )
		{
			up = Vector3.Right;
		}

		up = Rotation.FromAxis( previousSample.Tangent, previousSample.Roll ) * up;

		frames[0] = new Transform( previousSample.Position, Rotation.LookAt( previousSample.Tangent, up ), previousSample.Scale );

		for ( int i = 1; i < frameCount; i++ )
		{
			float t = (float)i / (frameCount - 1);
			float distance = t * totalSplineLength;

			Sample sample = SampleAtDistance( distance );

			// Parallel transport the up vector
			up = GetRotationMinimizingNormal( previousSample.Position, previousSample.Tangent, up, sample.Position, sample.Tangent );

			// Apply roll
			float deltaRoll = sample.Roll - previousSample.Roll;
			up = Rotation.FromAxis( sample.Tangent, deltaRoll ) * up;

			Rotation rotation = Rotation.LookAt( sample.Tangent, up );

			frames[i] = new Transform( sample.Position, rotation, sample.Scale );

			previousSample = sample;
		}

		// Correct up vectors for looped splines
		if ( IsLoop && frames.Length > 1 )
		{
			Vector3 startUp = frames[0].Rotation.Up;
			Vector3 endUp = frames[^1].Rotation.Up;

			float theta = MathF.Acos( Vector3.Dot( startUp, endUp ) ) / (frames.Length - 1);
			if ( Vector3.Dot( frames[0].Rotation.Forward, Vector3.Cross( startUp, endUp ) ) > 0 )
			{
				theta = -theta;
			}

			for ( int i = 0; i < frames.Length; i++ )
			{
				Rotation R = Rotation.FromAxis( frames[i].Rotation.Forward, (theta * i).RadianToDegree() );
				Vector3 correctedUp = R * frames[i].Rotation.Up;
				frames[i] = new Transform( frames[i].Position, Rotation.LookAt( frames[i].Rotation.Forward, correctedUp ), frames[i].Scale );
			}
		}

		return frames;
	}

	internal static Vector3 GetRotationMinimizingNormal( Vector3 posA, Vector3 tangentA, Vector3 normalA, Vector3 posB, Vector3 tangentB )
	{
		// Source: https://www.microsoft.com/en-us/research/wp-content/uploads/2016/12/Computation-of-rotation-minimizing-frames.pdf
		Vector3 v1 = posB - posA;
		float v1DotV1Half = Vector3.Dot( v1, v1 ) / 2f;
		float r1 = Vector3.Dot( v1, normalA ) / v1DotV1Half;
		float r2 = Vector3.Dot( v1, tangentA ) / v1DotV1Half;
		Vector3 nL = normalA - r1 * v1;
		Vector3 tL = tangentA - r2 * v1;
		Vector3 v2 = tangentB - tL;
		float v2DotV2 = Vector3.Dot( v2, v2 );
		float r3 = v2DotV2 > 0.0001f ? Vector3.Dot( v2, nL ) / v2DotV2 : 0f;
		return (nL - 2f * r3 * v2).Normal;
	}

	private void CheckPointIndex( int pointIndex )
	{
		if ( pointIndex < 0 || pointIndex >= _points.Count || (IsLoop && pointIndex == _points.Count - 1) )
		{
			throw new ArgumentOutOfRangeException( nameof( pointIndex ), "Spline point index out of range." );
		}
	}

	// Edge case: pointIndex > _splinePoints.Count
	private void CheckInsertPointIndex( int pointIndex )
	{
		if ( pointIndex < 0 || pointIndex > _points.Count )
		{
			throw new ArgumentOutOfRangeException( nameof( pointIndex ), "Spline point index out of range." );
		}
	}

	private void CheckSegmentIndex( int segmentIndex )
	{
		if ( segmentIndex < 0 || segmentIndex >= SplineUtils.SegmentNum( PointsView ) )
		{
			throw new ArgumentOutOfRangeException( nameof( segmentIndex ), "Spline segment index out of range." );
		}
	}

	private void RecalculateHandlesForPointAndAdjacentPoints( int pointIndex )
	{
		RecalculateHandlesForPoint( pointIndex );
		if ( pointIndex > 0 )
		{
			RecalculateHandlesForPoint( pointIndex - 1 );
		}

		if ( pointIndex < _points.Count - 1 )
		{
			RecalculateHandlesForPoint( pointIndex + 1 );
		}

		if ( IsLoop )
		{
			if ( pointIndex == 0 )
			{
				RecalculateHandlesForPoint( _points.Count - 2 );
			}

			if ( pointIndex == _points.Count - 2 )
			{
				RecalculateHandlesForPoint( 0 );
			}
		}
	}

	private void RecalculateHandlesForPoint( int index )
	{
		if ( IsLoop && index == _points.Count - 1 )
		{
			index = 0;
		}
		switch ( _points[index].Mode )
		{
			case HandleMode.Auto:
				_points[index] = SplineUtils.CalculateSmoothTangentForPoint( PointsView, index );
				break;
			case HandleMode.Linear:
				_points[index] = SplineUtils.CalculateLinearTangentForPoint( PointsView, index );
				break;
			case HandleMode.Split:
				break;
			case HandleMode.Mirrored:
				_points[index] = _points[index] with { Out = -_points[index].In };
				break;
		}

		if ( IsLoop && index == 0 )
		{
			_points[_points.Count - 1] = _points[0];
		}
	}
}
