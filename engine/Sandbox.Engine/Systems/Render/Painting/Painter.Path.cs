using System.Runtime.InteropServices;
using Sandbox.UI;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Builds batched, shader-evaluated paths in layout coordinates.
	/// </summary>
	internal static partial class Path
	{
		static void ValidatePatternSize( double length, double period, int primitivesPerPeriod, int segments )
		{
			// A dash can need a body and two caps, plus a body/join at each crossed vertex.
			// Reject only geometry that cannot fit the actual path buffers, before enumerating periods.
			double primitives = (Math.Ceiling( length / period ) + 2) * primitivesPerPeriod + segments * 2.0;
			if ( primitives > Data.MaxPrimitiveCount )
				throw new ArgumentOutOfRangeException( "stroke", "The pattern's generated geometry exceeds the GPU path buffer capacity." );
		}

		internal static void DrawPolygon( Painter.Context buffer, ReadOnlySpan<Vector2> points, Fill fill )
		{
			ArgumentOutOfRangeException.ThrowIfLessThan( points.Length, 3 );
			if ( !GetBounds( points, out var bounds ) || bounds.Width <= 0 || bounds.Height <= 0 || bounds.Size.Length <= 0 ) return;

			var desc = fill.CreateDescriptor( bounds, buffer );
			if ( points.Length <= BorderShape.MaxPoints )
			{
				Span<Vector2> local = stackalloc Vector2[points.Length];
				for ( int i = 0; i < points.Length; i++ )
					local[i] = points[i] - bounds.Position;
				desc.SetPolygon( local );
				Add( buffer, desc );
				return;
			}

			desc.PathData = BuildPolygon( points, out _ );
			desc.BorderShapeData = desc.PathData.Shape;
			Add( buffer, desc );
		}

		internal static void DrawPolyline( Painter.Context buffer, ReadOnlySpan<Vector2> points, Stroke stroke, bool closed = false )
		{
			if ( stroke.IsDisabled ) return;
			if ( points.Length < 2 || !ValidWidth( stroke.Width ) || !GetBounds( points, out _ ) ) return;

			Span<Vector2> clean = points.Length < 128 ? stackalloc Vector2[points.Length + 1] : new Vector2[points.Length + 1];
			int count = 0;
			foreach ( var point in points )
			{
				if ( count == 0 || point != clean[count - 1] ) clean[count++] = point;
			}
			if ( closed && count > 1 && clean[0] == clean[count - 1] ) count--;
			if ( count < 2 ) return;
			var alignmentMask = closed && stroke.Alignment != Stroke.StrokeAlignment.Center ? PolygonMask( clean[..count] ) : null;
			if ( closed ) clean[count++] = clean[0];
			clean = clean[..count];

			Span<double> lengths = clean.Length <= 128 ? stackalloc double[clean.Length - 1] : new double[clean.Length - 1];
			double total = 0;
			for ( int i = 0; i < lengths.Length; i++ )
			{
				lengths[i] = (clean[i + 1] - clean[i]).Length;
				if ( !double.IsFinite( lengths[i] ) || lengths[i] <= 0 ) return;
				total += lengths[i];
			}

			bool patterned = GetPattern( stroke, out var dash, out var period, out var phase );
			if ( patterned ) ValidatePatternSize( total, period, stroke.Style == BorderStyle.Dashed && stroke.Cap == Stroke.LineCap.Round ? 3 : 1, lengths.Length );
			if ( alignmentMask is not null ) stroke = stroke with { Width = stroke.Width * 2 };
			if ( !ValidWidth( stroke.Width ) ) return;
			var primitives = new List<UICssBoxBatched.PathPrimitive>();
			AddRun( primitives, clean, stroke, closed );
			if ( !GetPrimitiveBounds( primitives, stroke.Width * 0.5f, out var paintBounds ) ) return;
			if ( !patterned )
			{
				EmitStroke( buffer, primitives, stroke, paintBounds, alignmentMask );
				return;
			}
			primitives.Clear();
			if ( stroke.Style == BorderStyle.Dotted )
			{
				int segment = 0;
				double segmentStart = 0;
				foreach ( double distance in DotDistances( stroke, total, closed, period, phase ) )
				{
					while ( segment + 1 < lengths.Length && distance > segmentStart + lengths[segment] )
						segmentStart += lengths[segment++];
					var point = clean[segment] + (clean[segment + 1] - clean[segment]) * (float)((distance - segmentStart) / lengths[segment]);
					AddDisc( primitives, point );
				}
			}
			else
			{
				var runs = new List<List<Vector2>>();
				List<Vector2> run = null;
				for ( int i = 0; i < lengths.Length; i++ )
				{
					double position = 0;
					while ( position < lengths[i] )
					{
						bool on = phase < dash;
						double remaining = (on ? dash : period) - phase;
						double step = Math.Min( lengths[i] - position, remaining );
						double end = position + step;
						if ( end <= position ) return;
						if ( on )
						{
							var a = clean[i] + (clean[i + 1] - clean[i]) * (float)(position / lengths[i]);
							var b = end == lengths[i] ? clean[i + 1] : clean[i] + (clean[i + 1] - clean[i]) * (float)(end / lengths[i]);
							if ( run is null ) { run = [a]; runs.Add( run ); }
							if ( b != run[^1] ) run.Add( b );
						}
						position = end;
						phase += step;
						if ( step == remaining )
						{
							if ( on ) run = null;
							phase = on ? dash : 0;
						}
					}
				}

				// A dash crossing the closing vertex has a join, not two end caps.
				if ( closed && runs.Count > 1 && runs[0][0] == clean[0] && runs[^1][^1] == clean[0] )
				{
					var last = runs[^1];
					last.AddRange( CollectionsMarshal.AsSpan( runs[0] )[1..] );
					runs.RemoveAt( 0 );
				}
				foreach ( var piece in runs )
					AddRun( primitives, CollectionsMarshal.AsSpan( piece ), stroke, closed && piece.Count > 2 && piece[0] == piece[^1] );
			}

			EmitStroke( buffer, primitives, stroke, paintBounds, alignmentMask );
		}

		internal static void DrawArc( Painter.Context buffer, Vector2 center, float radius, float startAngle, float sweepAngle, Stroke stroke, Data alignmentMask = null )
		{
			if ( stroke.IsDisabled ) return;
			if ( !center.IsFinite || !ValidWidth( radius ) || !ValidWidth( stroke.Width ) || !float.IsFinite( startAngle ) || !float.IsFinite( sweepAngle ) || sweepAngle == 0 ) return;
			bool closed = MathF.Abs( sweepAngle ) >= 360;
			alignmentMask = closed && stroke.Alignment != Stroke.StrokeAlignment.Center ? alignmentMask ?? CircleMask( center, radius ) : null;
			double sweep = Math.Clamp( sweepAngle, -360, 360 ) * (Math.PI / 180);
			double start = (startAngle % 360) * (Math.PI / 180);
			double total = Math.Abs( sweep ) * radius;
			double direction = Math.Sign( sweep );
			bool patterned = GetPattern( stroke, out var dash, out var period, out var phase );
			if ( patterned ) ValidatePatternSize( total, period, 1, 1 );
			if ( alignmentMask is not null ) stroke = stroke with { Width = stroke.Width * 2 };
			if ( !ValidWidth( stroke.Width ) ) return;
			var primitives = new List<UICssBoxBatched.PathPrimitive>();
			var paintBounds = ArcBounds( center, radius, start, sweep ).Grow( stroke.Width * (closed ? 0.5f : stroke.Cap == Stroke.LineCap.Arrow ? 1 : stroke.Cap == Stroke.LineCap.Square ? (MathF.Sqrt( 2 ) * 0.5f) : 0.5f) );
			if ( !paintBounds.Position.IsFinite || !paintBounds.Size.IsFinite ) return;

			void AddArc( double from, double to, bool full = false )
			{
				primitives.Add( new UICssBoxBatched.PathPrimitive
				{
					Kind = UICssBoxBatched.PathPrimitiveKind.Arc,
					A = new Vector4( center.x, center.y, radius, (float)(start + direction * from / radius) ),
					B = new Vector4( (float)(direction * (to - from) / radius), 0, 0, 0 ),
					Count = full ? UICssBoxBatched.PathCap.Ring : (int)stroke.Cap,
				} );
			}

			if ( !patterned )
			{
				AddArc( 0, total, closed );
				EmitStroke( buffer, primitives, stroke, paintBounds, alignmentMask );
				return;
			}
			if ( stroke.Style == BorderStyle.Dotted )
			{
				foreach ( double distance in DotDistances( stroke, total, closed, period, phase ) )
				{
					AddDisc( primitives, center + Direction( start + direction * distance / radius ) * radius );
				}
			}
			else
			{
				var intervals = new List<(double Start, double End)>();
				for ( double distance = -phase; distance < total; )
				{
					if ( distance + dash > 0 ) intervals.Add( (Math.Max( 0, distance ), Math.Min( total, distance + dash )) );
					double next = distance + period;
					if ( next <= distance ) return;
					distance = next;
				}
				if ( closed && intervals.Count > 1 && intervals[0].Start == 0 && intervals[^1].End == total )
				{
					intervals[^1] = (intervals[^1].Start, total + intervals[0].End);
					intervals.RemoveAt( 0 );
				}
				foreach ( var interval in intervals )
					AddArc( interval.Start, interval.End, closed && interval.End - interval.Start >= total );
			}

			EmitStroke( buffer, primitives, stroke, paintBounds, alignmentMask );
		}

		static IEnumerable<double> DotDistances( Stroke stroke, double length, bool closed, double period, double phase )
		{
			if ( closed )
			{
				// Fit whole periods around the loop; phase must never create or remove a dot at the seam.
				int count = (int)Math.Max( 1, Math.Round( length / period ) );
				period = length / count;
				phase = float.IsFinite( stroke.Offset ) ? ((stroke.Offset % period) + period) % period : 0;
				double first = (period - phase) % period;
				for ( int i = 0; i < count; i++ )
					yield return Math.Min( length, first + i * period );
				yield break;
			}

			for ( double distance = phase == 0 ? 0 : period - phase; distance <= length; )
			{
				yield return distance;
				double next = distance + period;
				if ( next <= distance ) yield break;
				distance = next;
			}
		}

		static bool GetPattern( Stroke stroke, out double dash, out double period, out double phase )
		{
			dash = stroke.Style == BorderStyle.Dotted ? stroke.Width : stroke.DashLength;
			period = dash + Math.Max( 0, stroke.Gap );
			phase = 0;
			if ( stroke.Style is not (BorderStyle.Dashed or BorderStyle.Dotted) || !float.IsFinite( stroke.Gap ) || !double.IsFinite( dash ) || dash <= 0 || !double.IsFinite( period ) || period <= 0 ) return false;
			if ( stroke.Style != BorderStyle.Dotted && period == dash ) return false;
			phase = float.IsFinite( stroke.Offset ) ? ((stroke.Offset % period) + period) % period : 0;
			return true;
		}

		static void AddRun( List<UICssBoxBatched.PathPrimitive> primitives, ReadOnlySpan<Vector2> points, Stroke stroke, bool closed )
		{
			if ( points.Length < 2 ) return;
			for ( int i = 0; i + 1 < points.Length; i++ )
			{
				if ( points[i] == points[i + 1] ) continue;
				int caps = !closed && stroke.Cap == Stroke.LineCap.Square ? (i == 0 ? UICssBoxBatched.PathCap.SquareStart : 0) | (i + 2 == points.Length ? UICssBoxBatched.PathCap.SquareEnd : 0) : 0;
				bool pointed = !closed && (stroke.Cap == Stroke.LineCap.Triangle || stroke.Cap == Stroke.LineCap.Arrow);
				primitives.Add( new UICssBoxBatched.PathPrimitive
				{
					Kind = UICssBoxBatched.PathPrimitiveKind.Segment,
					A = Pack( points[i], points[i + 1] ),
					B = new Vector4( pointed && i == 0 ? (int)stroke.Cap : (int)Stroke.LineCap.Butt, pointed && i + 2 == points.Length ? (int)stroke.Cap : (int)Stroke.LineCap.Butt, 0, 0 ),
					Count = caps,
				} );
				if ( i > 0 ) AddJoin( primitives, points[i - 1], points[i], points[i + 1], stroke );
			}
			if ( closed )
				AddJoin( primitives, points[^2], points[0], points[1], stroke );
			else if ( stroke.Cap == Stroke.LineCap.Round )
			{
				AddDisc( primitives, points[0] );
				AddDisc( primitives, points[^1] );
			}
		}

		static void AddJoin( List<UICssBoxBatched.PathPrimitive> primitives, Vector2 a, Vector2 b, Vector2 c, Stroke stroke )
		{
			var incoming = (b - a).Normal;
			var outgoing = (c - b).Normal;
			float cross = incoming.x * outgoing.y - incoming.y * outgoing.x;
			float incomingLength = (b - a).Length;
			float outgoingLength = (c - b).Length;
			if ( stroke.Join == Stroke.LineJoin.Round )
			{
				// The disk's far-end constraints keep short adjacent butt segments from growing extra caps.
				// B.xy stores adjacent lengths; C stores incoming/outgoing unit directions.
				primitives.Add( new UICssBoxBatched.PathPrimitive
				{
					Kind = UICssBoxBatched.PathPrimitiveKind.RoundJoin,
					Count = 4,
					A = Pack( b, default ),
					B = new Vector4( incomingLength, outgoingLength, 0, 0 ),
					C = Pack( incoming, outgoing ),
				} );
				return;
			}
			if ( MathF.Abs( cross ) < 0.00001f )
			{
				if ( Vector2.Dot( incoming, outgoing ) > 0 )
				{
					float overlap = MathF.Min( MathF.Min( incomingLength, outgoingLength ) * 0.5f, MathF.Max( stroke.Width, 1 ) );
					primitives.Add( new UICssBoxBatched.PathPrimitive { Kind = UICssBoxBatched.PathPrimitiveKind.Segment, A = Pack( b - incoming * overlap, b + outgoing * overlap ) } );
					return;
				}
				return;
			}

			var n0 = new Vector2( -incoming.y, incoming.x ) * -MathF.Sign( cross );
			var n1 = new Vector2( -outgoing.y, outgoing.x ) * -MathF.Sign( cross );
			var middle = (n0 + n1).Normal;
			float denominator = Vector2.Dot( middle, n0 );
			var miter = denominator > 0.00001f ? middle / denominator : new Vector2( float.PositiveInfinity );
			float limit = float.IsFinite( stroke.MiterLimit ) ? MathF.Max( 1, stroke.MiterLimit ) : 4;
			bool useMiter = stroke.Join == Stroke.LineJoin.Miter && miter.IsFinite && miter.Length <= limit;
			// Interior vertices overlap the adjacent segment boxes without passing their far endpoints.
			var inner0 = outgoing * (MathF.Min( stroke.Width, outgoingLength ) * 0.5f);
			var inner1 = -incoming * (MathF.Min( stroke.Width, incomingLength ) * 0.5f);
			primitives.Add( new UICssBoxBatched.PathPrimitive
			{
				Kind = UICssBoxBatched.PathPrimitiveKind.Join,
				Count = useMiter ? 5 : 4,
				A = Pack( b, n0 ),
				B = Pack( useMiter ? miter : n1, useMiter ? n1 : inner0 ),
				C = Pack( useMiter ? inner0 : inner1, useMiter ? inner1 : default ),
			} );
		}

		static void AddDisc( List<UICssBoxBatched.PathPrimitive> primitives, Vector2 point )
		{
			// Dots and round caps share the same view-independent disk geometry.
			primitives.Add( new UICssBoxBatched.PathPrimitive { Kind = UICssBoxBatched.PathPrimitiveKind.Disc, A = Pack( point, default ) } );
		}
		static Vector4 Pack( Vector2 a, Vector2 b ) => new( a.x, a.y, b.x, b.y );
		static Vector2 Direction( double angle ) => new( (float)Math.Cos( angle ), (float)Math.Sin( angle ) );
		static bool ValidWidth( float width ) => float.IsFinite( width ) && width > 0;

		static bool GetBounds( ReadOnlySpan<Vector2> points, out Rect bounds )
		{
			bounds = default;
			if ( points.IsEmpty ) return false;
			var min = points[0];
			var max = min;
			foreach ( var p in points )
			{
				if ( !p.IsFinite ) return false;
				min = Vector2.Min( min, p );
				max = Vector2.Max( max, p );
			}
			bounds = new Rect( min, max - min );
			return bounds.Size.IsFinite && float.IsFinite( bounds.Size.Length );
		}

		static Rect PrimitiveBounds( UICssBoxBatched.PathPrimitive p, float radius )
		{
			var center = new Vector2( p.A.x, p.A.y );
			if ( p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment )
			{
				var end = new Vector2( p.A.z, p.A.w );
				var bounds = Sandbox.Rect.FromPoints( center, end ).Grow( p.Count == 0 ? radius : radius * MathF.Sqrt( 2 ) );
				if ( p.B.x == (int)Stroke.LineCap.Butt && p.B.y == (int)Stroke.LineCap.Butt ) return bounds;
				var tangent = (end - center).Normal;
				for ( int cap = 0; cap < 2; cap++ )
				{
					var style = (Stroke.LineCap)(cap == 0 ? p.B.x : p.B.y);
					if ( style != Stroke.LineCap.Triangle && style != Stroke.LineCap.Arrow ) continue;
					var endpoint = cap == 0 ? center : end;
					float size = radius * (style == Stroke.LineCap.Arrow ? 2 : 1);
					var tip = endpoint + tangent * (cap == 0 ? -size : size);
					var wing = tangent.Perpendicular * size;
					bounds.Add( tip );
					bounds.Add( endpoint - wing );
					bounds.Add( endpoint + wing );
				}
				return bounds;
			}
			if ( p.Kind == UICssBoxBatched.PathPrimitiveKind.Arc )
				return ArcBounds( center, p.A.z, p.A.w, p.B.x ).Grow( p.Count == (int)Stroke.LineCap.Arrow ? radius * 2 : p.Count == (int)Stroke.LineCap.Square ? radius * MathF.Sqrt( 2 ) : radius );
			if ( p.Kind == UICssBoxBatched.PathPrimitiveKind.Join )
			{
				Span<Vector2> vertices = [new( p.A.z, p.A.w ), new( p.B.x, p.B.y ), new( p.B.z, p.B.w ), new( p.C.x, p.C.y ), new( p.C.z, p.C.w )];
				for ( int i = 0; i < p.Count - 2; i++ ) vertices[i] *= radius;
				GetBounds( vertices[..p.Count], out var bounds );
				return new Rect( center + bounds.Position, bounds.Size );
			}
			return new Rect( center - new Vector2( radius ), new Vector2( radius * 2 ) );
		}

		static Rect ArcBounds( Vector2 center, float radius, double start, double sweep )
		{
			var min = Vector2.Min( Direction( start ), Direction( start + sweep ) );
			var max = Vector2.Max( Direction( start ), Direction( start + sweep ) );
			double low = Math.Min( start, start + sweep );
			double high = Math.Max( start, start + sweep );
			for ( double angle = Math.Ceiling( low / (Math.PI * 0.5) ) * (Math.PI * 0.5); angle <= high; angle += Math.PI * 0.5 )
			{
				min = Vector2.Min( min, Direction( angle ) );
				max = Vector2.Max( max, Direction( angle ) );
			}
			return new Rect( center + min * radius, (max - min) * radius );
		}

		static bool GetPrimitiveBounds( List<UICssBoxBatched.PathPrimitive> primitives, float radius, out Rect bounds )
		{
			bounds = default;
			if ( primitives.Count == 0 ) return false;
			bounds = PrimitiveBounds( primitives[0], radius );
			foreach ( var primitive in primitives )
			{
				var rect = PrimitiveBounds( primitive, radius );
				if ( !rect.Position.IsFinite || !rect.Size.IsFinite || !rect.BottomRight.IsFinite ) return false;
				bounds.Add( rect );
			}
			return bounds.Size.IsFinite && float.IsFinite( bounds.Size.Length ) && bounds.Width > 0 && bounds.Height > 0;
		}

		static Rect SegmentBounds( Vector4 endpoints )
		{
			return Sandbox.Rect.FromPoints( new( endpoints.x, endpoints.y ), new( endpoints.z, endpoints.w ) );
		}

		static Data PolygonMask( ReadOnlySpan<Vector2> points )
		{
			var edges = new UICssBoxBatched.PathPrimitive[points.Length];
			for ( int i = 0; i < points.Length; i++ )
				edges[i] = new() { Kind = UICssBoxBatched.PathPrimitiveKind.Segment, A = Pack( points[i], points[(i + 1) % points.Length] ) };
			return new Data( new() { Kind = UICssBoxBatched.ShapeKind.PolygonPath }, edges );
		}

		internal static Data CircleMask( Vector2 center, float outerRadius, float innerRadius = 0 )
			=> new( new() { Kind = UICssBoxBatched.ShapeKind.Circle, Circle = new Vector4( center.x, center.y, outerRadius, innerRadius ) }, [] );

		static void EmitStroke( Painter.Context buffer, List<UICssBoxBatched.PathPrimitive> primitives, Stroke stroke, Rect paintBounds, Data alignmentMask )
		{
			if ( !GetPrimitiveBounds( primitives, stroke.Width * 0.5f, out var bounds ) ) return;
			if ( alignmentMask is not null && stroke.Alignment == Stroke.StrokeAlignment.Inside && alignmentMask.Shape.Kind < UICssBoxBatched.ShapeKind.FirstAnalytic )
			{
				var circle = alignmentMask.Shape.Circle;
				var maskBounds = alignmentMask.Shape.Kind == UICssBoxBatched.ShapeKind.Circle
					? new Rect( new Vector2( circle.x - circle.z, circle.y - circle.z ), new Vector2( circle.z * 2 ) )
					: new Rect( alignmentMask.Nodes[0].Bounds.x, alignmentMask.Nodes[0].Bounds.y,
						alignmentMask.Nodes[0].Bounds.z - alignmentMask.Nodes[0].Bounds.x,
						alignmentMask.Nodes[0].Bounds.w - alignmentMask.Nodes[0].Bounds.y );
				bounds = Sandbox.Rect.Intersect( bounds, maskBounds );
				paintBounds = Sandbox.Rect.Intersect( paintBounds, maskBounds );
				if ( !ValidBounds( bounds ) || !ValidBounds( paintBounds ) ) return;
			}
			var desc = stroke.Fill.CreateDescriptor( paintBounds, buffer, clipFill: false );
			desc.Rect = bounds;
			desc.BackgroundRect.x += paintBounds.Left - bounds.Left;
			desc.BackgroundRect.y += paintBounds.Top - bounds.Top;
			desc.BorderShapeData = new UICssBoxBatched.BorderShape { Kind = UICssBoxBatched.ShapeKind.StrokePath, Circle = new Vector4( bounds.Left, bounds.Top, stroke.Width, alignmentMask is null ? 0 : stroke.Alignment == Stroke.StrokeAlignment.Inside ? 1 : -1 ) };
			var data = CollectionsMarshal.AsSpan( primitives );
			desc.PathData = new Data( desc.BorderShapeData, data, alignmentMask );
			Add( buffer, desc );
		}
	}
}
