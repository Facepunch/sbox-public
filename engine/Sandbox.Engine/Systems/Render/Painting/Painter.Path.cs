using Sandbox.UI;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Builds batched, shader-evaluated paths in layout coordinates.
	/// </summary>
	internal static partial class Path
	{
		static int GetPatternCapacity( double length, double period, int primitivesPerPeriod, int segments )
		{
			// A dash can need a body and two caps, plus a body/join at each crossed vertex.
			// Reserve an upper bound before enumerating periods, respecting the GPU buffer limit.
			double primitives = (Math.Ceiling( length / period ) + 2) * primitivesPerPeriod + segments * 2.0;
			if ( primitives > Data.MaxPrimitiveCount )
				throw new ArgumentOutOfRangeException( "stroke", "The pattern's generated geometry exceeds the GPU path buffer capacity." );
			return (int)primitives;
		}

		internal static void DrawPolygon( Painter.Context buffer, ReadOnlySpan<Vector2> points, in Fill fill )
		{
			ArgumentOutOfRangeException.ThrowIfLessThan( points.Length, 3 );
			if ( !buffer.State.HasArea || buffer.State.Opacity == 0 ) return;
			if ( !GetBounds( points, out var bounds ) || bounds.Width <= 0 || bounds.Height <= 0 || bounds.Size.Length <= 0 ) return;

			var shapeIndex = AddPolygonGeometry( buffer, points, bounds );
			AddShape( buffer, fill, bounds, bounds, shapeIndex );
		}

		/// <summary>
		/// Records one polygon contour for a fill, a distance-based outline, or both.
		/// </summary>
		static int AddPolygonGeometry( Painter.Context buffer, ReadOnlySpan<Vector2> points, Rect bounds )
		{
			if ( points.Length > BorderShape.MaxPoints && points.Length <= PainterBatcher.MaxPolygonPoints )
				return buffer.Batcher.AddPolygonPoints( points, bounds.Position );

			UICssBoxBatched.BorderShape shape;
			int shapeIndex;
			if ( points.Length <= BorderShape.MaxPoints )
			{
				Span<Vector2> local = stackalloc Vector2[points.Length];
				for ( int i = 0; i < points.Length; i++ )
					local[i] = points[i] - bounds.Position;
				BoxDescriptor.CreatePolygonShape( local, out shape );
				shapeIndex = buffer.Batcher.AddShape( shape );
			}
			else
			{
				shape = new() { Kind = UICssBoxBatched.ShapeKind.PolygonPath };
				using var edgeBuffer = new PooledSpan<UICssBoxBatched.PathPrimitive>( points.Length );
				var edges = edgeBuffer.Span;
				PolygonEdges( points, bounds, edges );
				shapeIndex = buffer.Batcher.AddPath( shape, edges );
			}

			return shapeIndex;
		}

		/// <summary>
		/// Uses a polygon contour for solid round outlines, sharing it with any visible fill.
		/// Short edges retain the general stroke path because its round joins are clipped at
		/// adjacent segment endpoints.
		/// </summary>
		internal static bool TryDrawOutlinedPolygon( Painter.Context buffer, ReadOnlySpan<Vector2> points )
		{
			ref var state = ref buffer.State;
			ref readonly var stroke = ref state.Stroke;
			if ( !HasStroke( stroke ) || stroke.Style is BorderStyle.Dashed or BorderStyle.Dotted
				|| stroke.Join != Stroke.LineJoin.Round ) return false;
			if ( !state.HasArea || state.Opacity == 0 ) return true;

			bool aligned = stroke.Alignment != Stroke.StrokeAlignment.Center;
			float width = aligned ? stroke.Width * 2 : stroke.Width;
			if ( !ValidWidth( width ) || !GetBounds( points, out var bounds ) || !ValidBounds( bounds ) ) return false;

			float radius = width * 0.5f;
			float minimumLength = MathF.Max( radius, 0.001f );
			for ( int i = 0; i < points.Length; i++ )
			{
				float lengthSquared = (points[(i + 1) % points.Length] - points[i]).LengthSquared;
				if ( !float.IsFinite( lengthSquared ) || lengthSquared < minimumLength * minimumLength ) return false;
			}

			bool inside = stroke.Alignment == Stroke.StrokeAlignment.Inside;
			var strokeBounds = inside ? bounds : bounds.Grow( radius );
			if ( !ValidBounds( strokeBounds ) || !strokeBounds.BottomRight.IsFinite ) return false;
			int polygonIndex = AddPolygonGeometry( buffer, points, bounds );
			if ( !state.Fill.IsTransparent ) AddShape( buffer, state.Fill, bounds, bounds, polygonIndex );

			var offset = strokeBounds.Position - bounds.Position;
			int strokeIndex = buffer.Batcher.AddShape( UICssBoxBatched.BorderShape.CreatePolygonStroke( polygonIndex, offset, width, stroke.Alignment ) );
			AddShape( buffer, Stroke.GetFill( in stroke ), strokeBounds, strokeBounds, strokeIndex, clipFill: false );
			return true;
		}

		internal static void DrawPolyline( Painter.Context buffer, ReadOnlySpan<Vector2> points, in Stroke stroke, bool closed = false )
		{
			if ( points.Length == 2 && !closed )
			{
				DrawSimpleLine( buffer, points[0], points[1], stroke );
				return;
			}

			if ( stroke.IsDisabled || !buffer.State.HasArea || buffer.State.Opacity == 0 ) return;
			if ( points.Length < 2 || !ValidWidth( stroke.Width ) || !GetBounds( points, out _ ) ) return;

			DrawGeneralPolyline( buffer, points, stroke, closed );
		}

		// Keep the temporary buffers and patterned stroke work out of the two-point dispatch frame.
		// General paths use pooled spans to avoid a separate large inline buffer for every temporary.
		static void DrawGeneralPolyline( Painter.Context buffer, ReadOnlySpan<Vector2> points, in Stroke originalStroke, bool closed )
		{
			scoped ref readonly var stroke = ref originalStroke;
			Stroke alignedStroke = default;
			using var cleanBuffer = new PooledSpan<Vector2>( checked(points.Length + 1) );
			var clean = cleanBuffer.Span;
			int count = 0;
			foreach ( var point in points )
			{
				if ( count == 0 || point != clean[count - 1] ) clean[count++] = point;
			}
			if ( closed && count > 1 && clean[0] == clean[count - 1] ) count--;
			if ( count < 2 ) return;
			int polygonCount = count;
			if ( closed ) clean[count++] = clean[0];
			clean = clean[..count];

			bool patterned = GetPattern( stroke, out var dash, out var period, out var phase );
			using var lengthBuffer = patterned ? new PooledSpan<double>( clean.Length - 1 ) : default;
			var lengths = lengthBuffer.Span;
			double total = 0;
			for ( int i = 0; i + 1 < clean.Length; i++ )
			{
				float lengthSquared = (clean[i + 1] - clean[i]).LengthSquared;
				if ( !float.IsFinite( lengthSquared ) || lengthSquared <= 0 ) return;
				if ( patterned )
				{
					lengths[i] = MathF.Sqrt( lengthSquared );
					total += lengths[i];
				}
			}

			int capacity = checked(clean.Length * 2);
			if ( patterned ) capacity = Math.Max( capacity, GetPatternCapacity( total, period, stroke.Style == BorderStyle.Dashed && stroke.Cap == Stroke.LineCap.Round ? 3 : 1, lengths.Length ) );
			bool aligned = closed && stroke.Alignment != Stroke.StrokeAlignment.Center;
			if ( aligned )
			{
				alignedStroke = stroke with { Width = stroke.Width * 2 };
				stroke = ref alignedStroke;
			}
			if ( !ValidWidth( stroke.Width ) ) return;
			var alignmentMask = aligned ? PolygonMask( buffer, clean[..polygonCount] ) : (AlignmentMask?)null;
			using var primitiveBuffer = new PooledSpan<UICssBoxBatched.PathPrimitive>( capacity );
			var primitives = primitiveBuffer.Span;
			int primitiveCount = 0;
			AddRun( primitives, ref primitiveCount, clean, stroke, closed );
			if ( !GetPrimitiveBounds( primitives[..primitiveCount], stroke.Width * 0.5f, out var paintBounds ) ) return;
			if ( !patterned )
			{
				EmitStroke( buffer, primitives[..primitiveCount], stroke, paintBounds, alignmentMask, paintBounds );
				return;
			}
			primitiveCount = 0;
			if ( stroke.Style == BorderStyle.Dotted )
			{
				int segment = 0;
				double segmentStart = 0;
				foreach ( double distance in DotDistances( stroke, total, closed, period, phase ) )
				{
					while ( segment + 1 < lengths.Length && distance > segmentStart + lengths[segment] )
						segmentStart += lengths[segment++];
					var point = clean[segment] + (clean[segment + 1] - clean[segment]) * (float)((distance - segmentStart) / lengths[segment]);
					AddDisc( primitives, ref primitiveCount, point );
				}
			}
			else
			{
				// Each run adds two endpoints plus the vertices it crosses. Keep the runs in
				// contiguous temporary storage instead of allocating a List for every dash.
				using var runPointBuffer = new PooledSpan<Vector2>( checked(capacity * 2 + clean.Length) );
				using var runBuffer = new PooledSpan<(int Start, int Count)>( capacity );
				var runPoints = runPointBuffer.Span;
				var runs = runBuffer.Span;
				int pointCount = 0;
				int runCount = 0;
				bool inRun = false;
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
							if ( !inRun )
							{
								runs[runCount++] = (pointCount, 1);
								runPoints[pointCount++] = a;
								inRun = true;
							}
							if ( b != runPoints[pointCount - 1] )
							{
								runPoints[pointCount++] = b;
								runs[runCount - 1].Count++;
							}
						}
						position = end;
						phase += step;
						if ( step == remaining )
						{
							if ( on ) inRun = false;
							phase = on ? dash : 0;
						}
					}
				}

				// A dash crossing the closing vertex has a join, not two end caps.
				runs = runs[..runCount];
				if ( closed && runCount > 1 && runPoints[0] == clean[0] && runPoints[pointCount - 1] == clean[0] )
				{
					int extra = runs[0].Count - 1;
					runPoints.Slice( 1, extra ).CopyTo( runPoints[pointCount..] );
					runs[^1].Count += extra;
					runs = runs[1..];
				}
				foreach ( var run in runs )
				{
					var piece = runPoints.Slice( run.Start, run.Count );
					AddRun( primitives, ref primitiveCount, piece, stroke, closed && piece.Length > 2 && piece[0] == piece[^1] );
				}
			}

			EmitStroke( buffer, primitives[..primitiveCount], stroke, paintBounds, alignmentMask );
		}

		/// <summary>
		/// Emits endpoints and stroke parameters directly, without generating geometry for caps or patterns.
		/// </summary>
		internal static void DrawSimpleLine( Painter.Context buffer, Vector2 from, Vector2 to, in Stroke stroke )
		{
			if ( stroke.IsDisabled || !buffer.State.HasArea || buffer.State.Opacity == 0 ) return;
			if ( !ValidWidth( stroke.Width ) || !from.IsFinite || !to.IsFinite ) return;
			float length = (to - from).Length;
			if ( !float.IsFinite( length ) || length <= 0 ) return;

			float radius = stroke.Width * 0.5f;
			// Use the undashed segment's bounds for paint mapping, as the general path does.
			var primitive = new UICssBoxBatched.PathPrimitive
			{
				Kind = UICssBoxBatched.PathPrimitiveKind.Segment,
				A = Pack( from, to ),
				B = new Vector4( (int)stroke.Cap, (int)stroke.Cap, 0, 0 ),
				Count = stroke.Cap == Stroke.LineCap.Square ? UICssBoxBatched.PathCap.SquareStart | UICssBoxBatched.PathCap.SquareEnd : 0
			};
			var bounds = PrimitiveBounds( primitive, radius );
			if ( !bounds.Position.IsFinite || !bounds.Size.IsFinite ) return;

			int pattern = 0;
			double first = 0, last = 0;
			var paintBounds = bounds;
			if ( GetPattern( stroke, out var dash, out var period, out var phase ) )
			{
				pattern = stroke.Style == BorderStyle.Dotted ? 2 : 1;
				first = pattern == 2 ? (phase == 0 ? 0 : period - phase) : -phase;
				if ( pattern == 1 && first + dash <= 0 ) first += period;
				if ( pattern == 2 ? first > length : first >= length ) return;
				last = pattern == 2 ? Math.Floor( (length - first) / period ) : Math.Ceiling( (length - first) / period ) - 1;

				// Fit the coverage quad to the actual first and last marks, while the fill stays mapped
				// across the undashed line. This also matches general paths under projective transforms.
				var delta = to - from;
				var a = from + delta * (float)(Math.Max( first, 0 ) / length);
				var b = from + delta * (float)(Math.Min( first + last * period + (pattern == 2 ? 0 : dash), length ) / length);
				primitive.A = Pack( a, b );
				bounds = pattern == 2 ? Sandbox.Rect.FromPoints( a, b ).Grow( radius ) : PrimitiveBounds( primitive, radius );
			}

			var shape = new UICssBoxBatched.BorderShape
			{
				Kind = UICssBoxBatched.ShapeKind.SimpleLine,
				Circle = new Vector4( from.x, from.y, stroke.Width, (int)stroke.Cap ),
				Polygon01 = new Vector4( to.x, to.y, length, pattern ),
				Polygon23 = pattern == 0 ? default : new Vector4( (float)dash, (float)Math.Min( period, float.MaxValue ), (float)first, (float)last ),
				Polygon45 = new Vector4( bounds.Left, bounds.Top, 0, 0 )
			};
			var shapeIndex = buffer.Batcher.AddShape( shape );
			AddShape( buffer, Stroke.GetFill( in stroke ), bounds, paintBounds, shapeIndex, clipFill: false );
		}

		internal static void DrawArc( Painter.Context buffer, Vector2 center, float radius, float startAngle, float sweepAngle, Stroke stroke, AlignmentMask? alignmentMask = null )
		{
			if ( stroke.IsDisabled || !buffer.State.HasArea || buffer.State.Opacity == 0 ) return;
			if ( !center.IsFinite || !ValidWidth( radius ) || !ValidWidth( stroke.Width ) || !float.IsFinite( startAngle ) || !float.IsFinite( sweepAngle ) || sweepAngle == 0 ) return;
			bool closed = MathF.Abs( sweepAngle ) >= 360;
			bool aligned = closed && stroke.Alignment != Stroke.StrokeAlignment.Center;
			if ( !aligned ) alignmentMask = null;
			double sweep = Math.Clamp( sweepAngle, -360, 360 ) * (Math.PI / 180);
			double start = (startAngle % 360) * (Math.PI / 180);
			double total = Math.Abs( sweep ) * radius;
			double direction = Math.Sign( sweep );
			bool patterned = GetPattern( stroke, out var dash, out var period, out var phase );
			int capacity = patterned ? GetPatternCapacity( total, period, 1, 1 ) : 1;
			if ( aligned ) stroke = stroke with { Width = stroke.Width * 2 };
			if ( !ValidWidth( stroke.Width ) ) return;

			var paintBounds = ArcBounds( center, radius, start, sweep ).Grow( stroke.Width * (closed ? 0.5f : stroke.Cap == Stroke.LineCap.Arrow ? 1 : stroke.Cap == Stroke.LineCap.Square ? (MathF.Sqrt( 2 ) * 0.5f) : 0.5f) );
			if ( !paintBounds.Position.IsFinite || !paintBounds.Size.IsFinite ) return;
			if ( aligned ) alignmentMask ??= CircleMask( buffer, center, radius );

			UICssBoxBatched.PathPrimitive ArcPrimitive( double from, double to, bool full = false )
			{
				return new UICssBoxBatched.PathPrimitive
				{
					Kind = UICssBoxBatched.PathPrimitiveKind.Arc,
					A = new Vector4( center.x, center.y, radius, (float)(start + direction * from / radius) ),
					B = new Vector4( (float)(direction * (to - from) / radius), 0, 0, 0 ),
					Count = full ? UICssBoxBatched.PathCap.Ring : (int)stroke.Cap,
				};
			}

			if ( !patterned )
			{
				// A solid arc has exactly one primitive and needs no pooled storage.
				Span<UICssBoxBatched.PathPrimitive> primitive = stackalloc UICssBoxBatched.PathPrimitive[1];
				primitive[0] = ArcPrimitive( 0, total, closed );
				EmitStroke( buffer, primitive, stroke, paintBounds, alignmentMask );
				return;
			}

			using var primitiveBuffer = new PooledSpan<UICssBoxBatched.PathPrimitive>( capacity );
			var primitives = primitiveBuffer.Span;
			int primitiveCount = 0;
			if ( stroke.Style == BorderStyle.Dotted )
			{
				foreach ( double distance in DotDistances( stroke, total, closed, period, phase ) )
				{
					AddDisc( primitives, ref primitiveCount, center + Direction( start + direction * distance / radius ) * radius );
				}
			}
			else
			{
				using var intervalBuffer = new PooledSpan<(double Start, double End)>( capacity );
				var intervals = intervalBuffer.Span;
				int intervalCount = 0;
				for ( double distance = -phase; distance < total; )
				{
					if ( distance + dash > 0 ) intervals[intervalCount++] = (Math.Max( 0, distance ), Math.Min( total, distance + dash ));
					double next = distance + period;
					if ( next <= distance ) return;
					distance = next;
				}
				intervals = intervals[..intervalCount];
				if ( closed && intervals.Length > 1 && intervals[0].Start == 0 && intervals[^1].End == total )
				{
					intervals[^1] = (intervals[^1].Start, total + intervals[0].End);
					intervals = intervals[1..];
				}
				foreach ( var interval in intervals )
					primitives[primitiveCount++] = ArcPrimitive( interval.Start, interval.End, closed && interval.End - interval.Start >= total );
			}

			EmitStroke( buffer, primitives[..primitiveCount], stroke, paintBounds, alignmentMask );
		}

		static DotDistanceEnumerator DotDistances( in Stroke stroke, double length, bool closed, double period, double phase )
		{
			int count = -1;
			if ( closed )
			{
				// Fit whole periods around the loop; phase must never create or remove a dot at the seam.
				count = (int)Math.Max( 1, Math.Round( length / period ) );
				period = length / count;
				phase = float.IsFinite( stroke.Offset ) ? ((stroke.Offset % period) + period) % period : 0;
			}

			return new( length, period, closed ? (period - phase) % period : phase == 0 ? 0 : period - phase, count );
		}

		/// <summary>
		/// Enumerates dot positions without allocating an iterator for each stroke.
		/// </summary>
		struct DotDistanceEnumerator( double length, double period, double first, int count )
		{
			int index;
			readonly double start = first;
			double next = first;

			/// <summary>
			/// Distance along the path of the current dot.
			/// </summary>
			public double Current { get; private set; }

			/// <summary>
			/// Returns the value-type enumerator used by foreach.
			/// </summary>
			public readonly DotDistanceEnumerator GetEnumerator() => this;

			/// <summary>
			/// Advances to the next dot, preserving closed-loop spacing and open endpoints.
			/// </summary>
			public bool MoveNext()
			{
				if ( count >= 0 )
				{
					if ( index >= count ) return false;
					Current = Math.Min( length, start + index++ * period );
				}
				else
				{
					if ( next > length ) return false;
					Current = next;
					next += period;
					if ( next <= Current ) next = double.PositiveInfinity;
				}

				return true;
			}
		}

		static bool GetPattern( in Stroke stroke, out double dash, out double period, out double phase )
		{
			dash = stroke.Style == BorderStyle.Dotted ? stroke.Width : stroke.DashLength;
			period = dash + Math.Max( 0, stroke.Gap );
			phase = 0;
			if ( stroke.Style is not (BorderStyle.Dashed or BorderStyle.Dotted) || !float.IsFinite( stroke.Gap ) || !double.IsFinite( dash ) || dash <= 0 || !double.IsFinite( period ) || period <= 0 ) return false;
			if ( stroke.Style != BorderStyle.Dotted && period == dash ) return false;
			phase = float.IsFinite( stroke.Offset ) ? ((stroke.Offset % period) + period) % period : 0;
			return true;
		}

		static void AddRun( Span<UICssBoxBatched.PathPrimitive> primitives, ref int primitiveCount, ReadOnlySpan<Vector2> points, in Stroke stroke, bool closed )
		{
			if ( points.Length < 2 ) return;
			for ( int i = 0; i + 1 < points.Length; i++ )
			{
				if ( points[i] == points[i + 1] ) continue;
				int caps = !closed && stroke.Cap == Stroke.LineCap.Square ? (i == 0 ? UICssBoxBatched.PathCap.SquareStart : 0) | (i + 2 == points.Length ? UICssBoxBatched.PathCap.SquareEnd : 0) : 0;
				bool pointed = !closed && (stroke.Cap == Stroke.LineCap.Triangle || stroke.Cap == Stroke.LineCap.Arrow);
				primitives[primitiveCount++] = new UICssBoxBatched.PathPrimitive
				{
					Kind = UICssBoxBatched.PathPrimitiveKind.Segment,
					A = Pack( points[i], points[i + 1] ),
					B = new Vector4( pointed && i == 0 ? (int)stroke.Cap : (int)Stroke.LineCap.Butt, pointed && i + 2 == points.Length ? (int)stroke.Cap : (int)Stroke.LineCap.Butt, 0, 0 ),
					Count = caps,
				};
				if ( i > 0 ) AddJoin( primitives, ref primitiveCount, points[i - 1], points[i], points[i + 1], stroke );
			}
			if ( closed )
				AddJoin( primitives, ref primitiveCount, points[^2], points[0], points[1], stroke );
			else if ( stroke.Cap == Stroke.LineCap.Round )
			{
				AddDisc( primitives, ref primitiveCount, points[0] );
				AddDisc( primitives, ref primitiveCount, points[^1] );
			}
		}

		static void AddJoin( Span<UICssBoxBatched.PathPrimitive> primitives, ref int primitiveCount, Vector2 a, Vector2 b, Vector2 c, in Stroke stroke )
		{
			var incoming = b - a;
			var outgoing = c - b;
			float incomingLength = incoming.Length;
			float outgoingLength = outgoing.Length;
			incoming = incoming.IsNearZeroLength ? Vector2.Zero : incoming / incomingLength;
			outgoing = outgoing.IsNearZeroLength ? Vector2.Zero : outgoing / outgoingLength;
			if ( stroke.Join == Stroke.LineJoin.Round )
			{
				// The disk's far-end constraints keep short adjacent butt segments from growing extra caps.
				// B.xy stores adjacent lengths; C stores incoming/outgoing unit directions.
				primitives[primitiveCount++] = new UICssBoxBatched.PathPrimitive
				{
					Kind = UICssBoxBatched.PathPrimitiveKind.RoundJoin,
					Count = 4,
					A = Pack( b, default ),
					B = new Vector4( incomingLength, outgoingLength, 0, 0 ),
					C = Pack( incoming, outgoing ),
				};
				return;
			}
			float cross = incoming.x * outgoing.y - incoming.y * outgoing.x;
			if ( MathF.Abs( cross ) < 0.00001f )
			{
				if ( Vector2.Dot( incoming, outgoing ) > 0 )
				{
					float overlap = MathF.Min( MathF.Min( incomingLength, outgoingLength ) * 0.5f, MathF.Max( stroke.Width, 1 ) );
					primitives[primitiveCount++] = new UICssBoxBatched.PathPrimitive { Kind = UICssBoxBatched.PathPrimitiveKind.Segment, A = Pack( b - incoming * overlap, b + outgoing * overlap ) };
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
			primitives[primitiveCount++] = new UICssBoxBatched.PathPrimitive
			{
				Kind = UICssBoxBatched.PathPrimitiveKind.Join,
				Count = useMiter ? 5 : 4,
				A = Pack( b, n0 ),
				B = Pack( useMiter ? miter : n1, useMiter ? n1 : inner0 ),
				C = Pack( useMiter ? inner0 : inner1, useMiter ? inner1 : default ),
			};
		}

		static void AddDisc( Span<UICssBoxBatched.PathPrimitive> primitives, ref int primitiveCount, Vector2 point )
		{
			// Dots and round caps share the same view-independent disk geometry.
			primitives[primitiveCount++] = new UICssBoxBatched.PathPrimitive { Kind = UICssBoxBatched.PathPrimitiveKind.Disc, A = Pack( point, default ) };
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

		static Rect PrimitiveBounds( in UICssBoxBatched.PathPrimitive p, float radius )
		{
			var center = new Vector2( p.A.x, p.A.y );
			if ( p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment )
			{
				var end = new Vector2( p.A.z, p.A.w );
				var bounds = Sandbox.Rect.FromPoints( center, end ).Grow( p.Count == 0 ? radius : radius * MathF.Sqrt( 2 ) );
				// Only triangular caps need a direction. Round, butt and square caps are
				// already enclosed by the conservative expansion above.
				if ( p.B.x != (int)Stroke.LineCap.Triangle && p.B.x != (int)Stroke.LineCap.Arrow
					&& p.B.y != (int)Stroke.LineCap.Triangle && p.B.y != (int)Stroke.LineCap.Arrow ) return bounds;
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

		static bool GetPrimitiveBounds( ReadOnlySpan<UICssBoxBatched.PathPrimitive> primitives, float radius, out Rect bounds )
		{
			bounds = default;
			if ( primitives.IsEmpty ) return false;
			bounds = PrimitiveBounds( primitives[0], radius );
			for ( int i = 0; i < primitives.Length; i++ )
			{
				var rect = i == 0 ? bounds : PrimitiveBounds( primitives[i], radius );
				if ( !rect.Position.IsFinite || !rect.Size.IsFinite || !rect.BottomRight.IsFinite ) return false;
				bounds.Add( rect );
			}
			return bounds.Size.IsFinite && float.IsFinite( bounds.Size.Length ) && bounds.Width > 0 && bounds.Height > 0;
		}

		static Rect SegmentBounds( Vector4 endpoints )
		{
			return Sandbox.Rect.FromPoints( new( endpoints.x, endpoints.y ), new( endpoints.z, endpoints.w ) );
		}

		/// <summary>
		/// A transient mask already stored in this draw's batcher, with optional bounds for inside strokes.
		/// </summary>
		internal readonly record struct AlignmentMask( int ShapeIndex, Rect? Bounds );

		static AlignmentMask PolygonMask( Painter.Context buffer, ReadOnlySpan<Vector2> points )
		{
			using var edgeBuffer = new PooledSpan<UICssBoxBatched.PathPrimitive>( points.Length );
			var edges = edgeBuffer.Span;
			for ( int i = 0; i < points.Length; i++ )
				edges[i] = new() { Kind = UICssBoxBatched.PathPrimitiveKind.Segment, A = Pack( points[i], points[(i + 1) % points.Length] ) };

			GetBounds( points, out var bounds );
			var index = buffer.Batcher.AddPath( new() { Kind = UICssBoxBatched.ShapeKind.PolygonPath }, edges );
			return new( index, bounds );
		}

		internal static AlignmentMask CircleMask( Painter.Context buffer, Vector2 center, float outerRadius, float innerRadius = 0 )
		{
			var shape = new UICssBoxBatched.BorderShape
			{
				Kind = UICssBoxBatched.ShapeKind.Circle,
				Circle = new Vector4( center.x, center.y, outerRadius, innerRadius )
			};
			return new( buffer.Batcher.AddShape( shape ), new Rect( center - new Vector2( outerRadius ), new Vector2( outerRadius * 2 ) ) );
		}

		static void EmitStroke( Painter.Context buffer, ReadOnlySpan<UICssBoxBatched.PathPrimitive> primitives, in Stroke stroke, Rect paintBounds, AlignmentMask? alignmentMask, Rect? strokeBounds = null )
		{
			if ( !buffer.State.HasArea || buffer.State.Opacity == 0 ) return;
			var bounds = strokeBounds.GetValueOrDefault();
			if ( !strokeBounds.HasValue && !GetPrimitiveBounds( primitives, stroke.Width * 0.5f, out bounds ) ) return;
			if ( alignmentMask?.Bounds is { } maskBounds && stroke.Alignment == Stroke.StrokeAlignment.Inside )
			{
				bounds = Sandbox.Rect.Intersect( bounds, maskBounds );
				paintBounds = Sandbox.Rect.Intersect( paintBounds, maskBounds );
				if ( !ValidBounds( bounds ) || !ValidBounds( paintBounds ) ) return;
			}
			var shape = new UICssBoxBatched.BorderShape { Kind = UICssBoxBatched.ShapeKind.StrokePath, Circle = new Vector4( bounds.Left, bounds.Top, stroke.Width, alignmentMask is null ? 0 : stroke.Alignment == Stroke.StrokeAlignment.Inside ? 1 : -1 ) };
			var shapeIndex = buffer.Batcher.AddPath( shape, primitives, alignmentMask is { } mask ? mask.ShapeIndex + 1 : 0 );
			AddShape( buffer, Stroke.GetFill( in stroke ), bounds, paintBounds, shapeIndex, clipFill: false );
		}
	}
}
