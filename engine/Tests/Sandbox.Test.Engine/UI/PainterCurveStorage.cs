using System;
using System.Linq;

namespace UITests;

/// <summary>
/// Checks pooled curve growth, flattened geometry and warmed submission allocations.
/// </summary>
[TestClass]
public class PainterCurveStorageTest
{
	/// <summary>
	/// Quadratic, cubic and multi-segment smooth curves reuse storage after their capacities are warm.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Other tests can trigger GC and trim the shared pool during this measurement.
	[DataRow( 0, 4 )]
	[DataRow( 0, 96 )]
	[DataRow( 1, 4 )]
	[DataRow( 1, 96 )]
	[DataRow( 2, 4 )]
	[DataRow( 2, 96 )]
	public void WarmedCurvesDoNotAllocate( int kind, int count )
	{
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
		var points = MakePoints( count );
		try
		{
			for ( int i = 0; i < 32; i++ ) Draw( context, points, kind );
			long start = GC.GetAllocatedBytesForCurrentThread();
			for ( int i = 0; i < 32; i++ ) Draw( context, points, kind );
			long allocated = GC.GetAllocatedBytesForCurrentThread() - start;

			Assert.AreEqual( 0L, allocated );
			Assert.AreEqual( 1, context.Batcher.Instances.Count );
			Assert.IsTrue( context.Batcher.Paths.Count > 16, "The workload must exercise growth beyond the initial point rental." );
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	/// <summary>
	/// A large cubic keeps ordered endpoints, the exact midpoint and the original quarter-pixel tolerance.
	/// </summary>
	[TestMethod]
	public void GrowingBezierPreservesCurveGeometry()
	{
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
		try
		{
			context.Begin( new Rect( 0, 0, 5000, 5000 ) );
			var painter = context.Painter;
			painter.Stroke = Stroke.Solid( Color.White, 2 );
			painter.Bezier( new( 0, 0 ), new( 0, 4096 ), new( 4096, 4096 ), new( 4096, 0 ) );
			var segments = context.Batcher.Paths.Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment ).ToArray();

			Assert.IsTrue( segments.Length > 64 );
			Assert.AreEqual( new Vector2( 0, 0 ), new Vector2( segments[0].A.x, segments[0].A.y ) );
			Assert.AreEqual( new Vector2( 4096, 0 ), new Vector2( segments[^1].A.z, segments[^1].A.w ) );
			Assert.IsTrue( segments.Any( p => p.A.z == 2048 && p.A.w == 3072 ) );
			for ( int i = 1; i < segments.Length; i++ )
			{
				Assert.AreEqual( new Vector2( segments[i - 1].A.z, segments[i - 1].A.w ), new Vector2( segments[i].A.x, segments[i].A.y ) );
			}

			for ( int i = 0; i <= 256; i++ )
			{
				double t = i / 256.0;
				var point = new Vector2( (float)(4096 * t * t * (3 - 2 * t)), (float)(12288 * t * (1 - t)) );
				double nearest = double.PositiveInfinity;
				foreach ( var segment in segments )
				{
					var from = new Vector2( segment.A.x, segment.A.y );
					var to = new Vector2( segment.A.z, segment.A.w );
					var delta = to - from;
					float fraction = Math.Clamp( Vector2.Dot( point - from, delta ) / delta.LengthSquared, 0, 1 );
					nearest = Math.Min( nearest, (point - (from + delta * fraction)).Length );
				}
				Assert.IsTrue( nearest <= 0.251, $"Curve deviation at t={t} was {nearest}." );
			}
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	/// <summary>
	/// Growing a long smooth curve retains every input anchor and emits a single ordered path.
	/// </summary>
	[TestMethod]
	public void GrowingSmoothCurvePreservesAllAnchors()
	{
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
		var points = MakePoints( 96 );
		try
		{
			Draw( context, points, 2 );
			var segments = context.Batcher.Paths.Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment ).ToArray();
			Assert.IsTrue( segments.Length > points.Length );
			foreach ( var point in points )
			{
				Assert.IsTrue( segments.Any( p => new Vector2( p.A.x, p.A.y ) == point || new Vector2( p.A.z, p.A.w ) == point ) );
			}
			for ( int i = 1; i < segments.Length; i++ )
			{
				Assert.AreEqual( new Vector2( segments[i - 1].A.z, segments[i - 1].A.w ), new Vector2( segments[i].A.x, segments[i].A.y ) );
			}
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	static Vector2[] MakePoints( int count )
	{
		var points = new Vector2[count];
		for ( int i = 0; i < count; i++ ) points[i] = new Vector2( 10 + i * 40, i % 2 == 0 ? 100 : 500 );
		return points;
	}

	static void Draw( Painter.Context context, Vector2[] points, int kind )
	{
		context.CommandList.Reset();
		context.Begin( new Rect( 0, 0, 5000, 5000 ) );
		var painter = context.Painter;
		painter.Stroke = Stroke.Solid( Color.White, 2 );
		float size = points.Length * 40;
		if ( kind == 0 )
		{
			painter.Bezier( Vector2.Zero, new Vector2( 0, size ), new Vector2( size ), new Vector2( size, 0 ) );
		}
		else if ( kind == 1 )
		{
			painter.Bezier( Vector2.Zero, new Vector2( size * 0.5f, size ), new Vector2( size, 0 ) );
		}
		else
		{
			painter.LineSmooth( points );
		}
	}
}
