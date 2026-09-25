namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Retains geometry for frequently redrawn solid polylines. Colour and drawing transforms remain live.
	/// </summary>
	internal sealed class CachedLine
	{
		Vector2[] _points = [];
		float _width;
		Path.Data _data;
		Rect _bounds;

		internal void Draw( Painter painter, ReadOnlySpan<Vector2> points, Color color, float width = 1 )
		{
			if ( width != _width || !points.SequenceEqual( _points ) )
			{
				_points = points.ToArray();
				_width = width;
				_data = Path.BuildSolidLine( points, width, out _bounds );
			}
			if ( _data is null ) return;
			var context = painter.ActiveContext;
			new Fill( color ).CreateDescriptor( _bounds, context, out var descriptor, clipFill: false );
			descriptor.BorderShapeData = _data.Shape;
			descriptor.PathData = _data;
			Add( context, descriptor );
		}
	}

	/// <summary>
	/// Retains filled polygon geometry while allowing its colour and drawing transforms to change.
	/// </summary>
	internal sealed class CachedPolygon
	{
		Vector2[] _points = [];
		Path.Data _data;
		Rect _bounds;

		internal void Draw( Painter painter, ReadOnlySpan<Vector2> points, Color color )
		{
			if ( !points.SequenceEqual( _points ) )
			{
				_points = points.ToArray();
				_data = Path.BuildPolygon( points, out _bounds );
			}
			if ( _data is null ) return;
			var context = painter.ActiveContext;
			new Fill( color ).CreateDescriptor( _bounds, context, out var descriptor );
			descriptor.BorderShapeData = _data.Shape;
			descriptor.PathData = _data;
			Add( context, descriptor );
		}
	}

	internal static partial class Path
	{
		internal static Data BuildPolygon( ReadOnlySpan<Vector2> points, out Rect bounds )
		{
			bounds = default;
			if ( points.Length < 3 || !GetBounds( points, out bounds ) || bounds.Width <= 0 || bounds.Height <= 0 ) return null;
			using var edgeBuffer = new PooledSpan<UICssBoxBatched.PathPrimitive>( points.Length );
			var edges = edgeBuffer.Span;
			PolygonEdges( points, bounds, edges );
			return new Data( new() { Kind = UICssBoxBatched.ShapeKind.PolygonPath }, edges );
		}

		static void PolygonEdges( ReadOnlySpan<Vector2> points, Rect bounds, Span<UICssBoxBatched.PathPrimitive> edges )
		{
			for ( int i = 0; i < points.Length; i++ )
			{
				edges[i] = new() { Kind = UICssBoxBatched.PathPrimitiveKind.Segment, A = Pack( points[i] - bounds.Position, points[(i + 1) % points.Length] - bounds.Position ) };
			}
		}

		internal static Data BuildSolidLine( ReadOnlySpan<Vector2> points, float width, out Rect bounds )
		{
			bounds = default;
			if ( points.Length < 2 || !ValidWidth( width ) || !GetBounds( points, out _ ) ) return null;
			using var primitiveBuffer = new PooledSpan<UICssBoxBatched.PathPrimitive>( checked(points.Length * 2) );
			var primitives = primitiveBuffer.Span;
			int primitiveCount = 0;
			AddRun( primitives, ref primitiveCount, points, Stroke.Solid( Color.White, width ), false );
			primitives = primitives[..primitiveCount];
			if ( !GetPrimitiveBounds( primitives, width * 0.5f, out bounds ) ) return null;
			var shape = new UICssBoxBatched.BorderShape
			{
				Kind = UICssBoxBatched.ShapeKind.StrokePath,
				Circle = new Vector4( bounds.Left, bounds.Top, width, 0 )
			};
			return new Data( shape, primitives );
		}
	}
}
