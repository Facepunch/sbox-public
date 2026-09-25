using System;
using System.Buffers;
using System.Linq;

namespace UITests;

/// <summary>
/// Checks sampled and retained geometry when temporary arrays contain data from previous users.
/// </summary>
[TestClass]
public class PainterTemporaryStorageTest
{
	/// <summary>
	/// Sampled contours produce the same initialized primitives and hierarchy after reset and pool reuse.
	/// </summary>
	[TestMethod]
	[DataRow( "Ellipse" )]
	[DataRow( "RoundedRect" )]
	[DataRow( "RingSector" )]
	[DataRow( "Pie" )]
	[DataRow( "Star" )]
	[DataRow( "Heart" )]
	[DataRow( "Capsule" )]
	public void SampledShapesIgnorePreviousTemporaryContents( string shape )
	{
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
		try
		{
			DrawSampledShape( context, shape );
			var expectedPrimitives = context.Batcher.Paths.ToArray();
			var expectedNodes = context.Batcher.PathNodes.ToArray();
			var expectedPoints = context.Batcher.PolygonPoints.ToArray();
			Assert.IsTrue( expectedPrimitives.Length > 64 || expectedPoints.Length > 64, "Exercise a sampled contour larger than the former inline point storage." );
			Assert.AreEqual( 1, context.Batcher.Instances.Count );

			PoisonTemporaryArrays();
			DrawSampledShape( context, shape );
			Assert.AreEqual( 1, context.Batcher.Instances.Count );
			CollectionAssert.AreEqual( expectedPrimitives, context.Batcher.Paths.ToArray() );
			CollectionAssert.AreEqual( expectedNodes, context.Batcher.PathNodes.ToArray() );
			CollectionAssert.AreEqual( expectedPoints, context.Batcher.PolygonPoints.ToArray() );
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	/// <summary>
	/// Retained geometry owns its arrays independently of input points, reused scratch arrays and frame tables.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CachedGeometrySurvivesTemporaryReuseAndFrameReset( bool line )
	{
		var points = new Vector2[96];
		for ( int i = 0; i < points.Length; i++ )
		{
			float angle = i * MathF.Tau / points.Length;
			points[i] = new Vector2( 500 + MathF.Cos( angle ) * 400, 500 + MathF.Sin( angle ) * 300 );
		}
		var retained = line ? Painter.Path.BuildSolidLine( points, 4, out _ ) : Painter.Path.BuildPolygon( points, out _ );
		Assert.IsNotNull( retained );
		var expectedPrimitives = retained.Primitives.ToArray();
		var expectedNodes = retained.Nodes.ToArray();
		for ( int i = 0; i < points.Length; i++ ) points[i] = points[i] * 1.5f + new Vector2( 2000, 3000 );
		PoisonTemporaryArrays();
		var other = line ? Painter.Path.BuildSolidLine( points, 8, out _ ) : Painter.Path.BuildPolygon( points, out _ );
		Assert.IsNotNull( other );

		var batcher = new PainterBatcher( new Sandbox.Rendering.CommandList() );
		try
		{
			for ( int frame = 0; frame < 2; frame++ )
			{
				batcher.GetOrAddPath( other );
				var shape = batcher.Shapes[batcher.GetOrAddPath( retained )];
				CollectionAssert.AreEqual( expectedPrimitives, batcher.Paths.Skip( shape.PathOffset ).Take( shape.PathCount ).ToArray() );
				CollectionAssert.AreEqual( expectedNodes, batcher.PathNodes.Skip( shape.PathNodeOffset ).Take( shape.PathNodeCount ).ToArray() );
				CollectionAssert.AreEqual( expectedPrimitives, retained.Primitives.ToArray() );
				CollectionAssert.AreEqual( expectedNodes, retained.Nodes.ToArray() );
				batcher.AdvanceFrame();
			}
		}
		finally
		{
			batcher.CommandList.Reset();
			batcher.Dispose();
		}
	}

	static void DrawSampledShape( Painter.Context context, string shape )
	{
		context.CommandList.Reset();
		context.Begin( new Rect( 0, 0, 5000, 5000 ) );
		var painter = context.Painter;
		painter.Stroke = Stroke.Solid( Color.White, 4 );
		switch ( shape )
		{
			case "Ellipse":
				painter.Circle( new Rect( 100, 100, 2000, 1000 ) );
				break;

			case "RoundedRect":
				painter.Rect( new Rect( 100, 100, 2000, 2000 ), 400 );
				break;

			case "RingSector":
				painter.Ring( new Vector2( 1500 ), 500, 1000, 0, 270 );
				break;

			case "Pie":
				painter.Pie( new Vector2( 1500 ), 1000, 0, 300 );
				break;

			case "Star":
				painter.Star( new Vector2( 1500 ), 500, 500, points: 40 );
				break;

			case "Heart":
				painter.Heart( new Vector2( 1500 ), 2000 );
				break;

			case "Capsule":
				painter.Capsule( new Vector2( 1000 ), new Vector2( 3000, 1000 ), 500, 700 );
				break;
		}
	}

	static void PoisonTemporaryArrays()
	{
		for ( int count = 16; count <= 512; count *= 2 )
		{
			var points = ArrayPool<Vector2>.Shared.Rent( count );
			points.AsSpan().Fill( new Vector2( float.NaN ) );
			ArrayPool<Vector2>.Shared.Return( points );
			var primitives = ArrayPool<UICssBoxBatched.PathPrimitive>.Shared.Rent( count );
			primitives.AsSpan().Fill( new UICssBoxBatched.PathPrimitive { A = new Vector4( float.NaN ), B = new Vector4( float.NaN ), C = new Vector4( float.NaN ), Kind = -1, Count = -1 } );
			ArrayPool<UICssBoxBatched.PathPrimitive>.Shared.Return( primitives );
		}
	}
}
