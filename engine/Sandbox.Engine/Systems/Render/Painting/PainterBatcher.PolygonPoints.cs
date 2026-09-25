namespace Sandbox;

internal partial class PainterBatcher
{
	/// <summary>
	/// Largest dynamic contour evaluated directly from points. Larger contours retain their hierarchy.
	/// </summary>
	internal const int MaxPolygonPoints = 128;

	readonly GpuTable<Vector2> _polygonPointTable = new( "PolygonPointBuffer" );

	/// <summary>
	/// Frame-local polygon points, in each contour's local coordinate system.
	/// </summary>
	internal IReadOnlyList<Vector2> PolygonPoints => _polygonPointTable.Items;

	/// <summary>
	/// Appends a dynamic contour without hashing or retaining it between draws. A negative node count
	/// selects the raw-point polygon shader; PathOffset and PathCount address the point buffer.
	/// </summary>
	internal int AddPolygonPoints( ReadOnlySpan<Vector2> points, Vector2 origin )
	{
		GetBufferCapacity<Vector2>( (long)_polygonPointTable.Count + points.Length );
		int offset = _polygonPointTable.Count;
		var destination = _polygonPointTable.Append( points.Length );
		for ( int i = 0; i < points.Length; i++ )
		{
			destination[i] = points[i] - origin;
		}

		return AddShape( new UICssBoxBatched.BorderShape
		{
			Kind = UICssBoxBatched.ShapeKind.PolygonPath,
			PathOffset = offset,
			PathCount = points.Length,
			PathNodeCount = -1
		} );
	}
}
