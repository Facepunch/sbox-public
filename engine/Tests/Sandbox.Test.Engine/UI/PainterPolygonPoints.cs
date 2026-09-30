using System;

namespace UITests;

/// <summary>
/// Verifies dynamic polygon storage, fallback boundaries and frame lifetime.
/// </summary>
[TestClass]
public class PainterPolygonPointsTest
{
	/// <summary>
	/// Raw polygon storage honors the cutoff and appends repeated dynamic contours without segments or nodes.
	/// </summary>
	[TestMethod]
	[DataRow( 8 )]
	[DataRow( 9 )]
	[DataRow( 32 )]
	[DataRow( 33 )]
	[DataRow( 64 )]
	[DataRow( 129 )]
	[DataRow( 128 )]
	public void PointPolygonsRespectCutoffWithoutReuse( int count )
	{
		var commands = new Sandbox.Rendering.CommandList();
		var context = new Painter.Context( commands );
		try
		{
			var points = new Vector2[count];
			var origin = new Vector2( float.MaxValue );
			for ( int i = 0; i < count; i++ )
			{
				points[i] = new Vector2( 30, 40 ) + new Vector2( MathF.Cos( i * MathF.Tau / count ), MathF.Sin( i * MathF.Tau / count ) ) * 10;
				origin = Vector2.Min( origin, points[i] );
			}
			using ( var painter = context.Begin( new Rect( 0, 0, 200, 200 ) ) )
			{
				painter.Fill = Color.White;
				painter.Stroke = Stroke.None;
				painter.Polygon( points );
				painter.Polygon( points );
				Assert.AreEqual( 2, context.Batcher.Shapes.Count );
				bool raw = count > 8 && count <= PainterBatcher.MaxPolygonPoints;
				Assert.AreEqual( raw ? count * 2 : 0, context.Batcher.PolygonPoints.Count );
				Assert.AreEqual( count > PainterBatcher.MaxPolygonPoints ? count * 2 : 0, context.Batcher.Paths.Count );
				Assert.AreEqual( count > PainterBatcher.MaxPolygonPoints ? (count * 2 - 1) * 2 : 0, context.Batcher.PathNodes.Count );
				if ( raw )
				{
					Assert.AreEqual( -1, context.Batcher.Shapes[0].PathNodeCount );
					Assert.AreEqual( 0, context.Batcher.Shapes[0].PathOffset );
					Assert.AreEqual( count, context.Batcher.Shapes[1].PathOffset );
					for ( int i = 0; i < count; i++ ) Assert.AreEqual( points[i] - origin, context.Batcher.PolygonPoints[i] );
				}
			}
			commands.Reset();
			Assert.AreEqual( 0, context.Batcher.PolygonPoints.Count );
		}
		finally
		{
			commands.Reset();
			context.Batcher.Dispose();
		}
	}
}
