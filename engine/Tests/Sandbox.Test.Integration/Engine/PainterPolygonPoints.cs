using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineTests;

/// <summary>
/// Compares dynamic point polygons with equivalent hierarchy geometry through the real renderer.
/// </summary>
[TestClass]
public class PainterPolygonPointsRenderTest
{
	/// <summary>
	/// Covers cutoff boundaries, winding, concavity, repeated vertices, crossings, gradient fills,
	/// transformed clipping and all round-outline alignments without a production test switch.
	/// </summary>
	[TestMethod]
	[DataRow( 9, 0 )]
	[DataRow( 32, 0 )]
	[DataRow( 64, 0 )]
	[DataRow( 128, 0 )]
	[DataRow( 129, 0 )]
	[DataRow( 9, 1 )]
	[DataRow( 9, 2 )]
	[DataRow( 9, 3 )]
	public void PointsMatchHierarchy( int count, int topology )
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );

		var points = new Vector2[count];
		for ( int i = 0; i < count; i++ )
		{
			float angle = MathF.Tau * (topology == 2 ? i * 2 : i) / count;
			float radius = topology == 1 && i % 2 == 0 ? 16 : 35;
			points[i] = new Vector2( 48 ) + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * radius;
		}
		if ( topology == 3 ) points[2] = points[1];

		foreach ( bool reversed in new[] { false, true } )
		{
			if ( reversed ) Array.Reverse( points );
			foreach ( int alignment in new[] { -1, 0, 1, 2 } )
			{
				using var expected = Texture.CreateRenderTarget().WithSize( 128, 128 ).Create();
				using var actual = Texture.CreateRenderTarget().WithSize( 128, 128 ).Create();
				Draw( expected, points, alignment, true );
				Draw( actual, points, alignment, false );
				using var a = actual.GetBitmap();
				using var b = expected.GetBitmap();
				int visible = 0;
				float maximum = 0;
				for ( int y = 0; y < 128; y++ )
				{
					for ( int x = 0; x < 128; x++ )
					{
						var color = b.GetPixel( x, y );
						if ( color.a > 0.1f ) visible++;
						var delta = a.GetPixel( x, y ) - color;
						maximum = MathF.Max( maximum, MathF.Max( MathF.Abs( delta.a ), MathF.Max( MathF.Abs( delta.r ), MathF.Max( MathF.Abs( delta.g ), MathF.Abs( delta.b ) ) ) ) );
					}
				}
				Assert.IsTrue( visible > 40 );
				Assert.IsTrue( maximum <= 1.01f / 255, $"Count={count}, topology={topology}, alignment={alignment}, reversed={reversed}: maximum error {maximum}" );
			}
		}
	}

	static void Draw( Texture target, Vector2[] points, int alignment, bool hierarchy )
	{
		using var painter = Painter.Begin( target );
		painter.Clear( Color.Transparent );
		painter.Translate( 3.25f, 2.75f );
		painter.Rotate( 3 );
		painter.Scale( 1.05f, 0.9f );
		painter.Clip( new Rect( 12, 10, 80, 80 ) );
		painter.Opacity = 0.7f;
		painter.Fill = Fill.LinearGradient( Color.Red.WithAlpha( 0.4f ), Color.Blue.WithAlpha( 0.6f ) );
		painter.Stroke = alignment < 0 ? Stroke.None : Stroke.Solid( Color.White, 0.25f ).WithAlignment( (Stroke.StrokeAlignment)alignment );
		painter.Polygon( points );
		if ( hierarchy ) ReplacePointsWithHierarchy( painter.ActiveContext.Batcher );
		// Exercise cumulative uploads and point offsets within the same frame.
		painter.ActiveContext.Batcher.Flush();
		painter.Translate( 13, 7 );
		painter.Polygon( points );
		if ( hierarchy ) ReplacePointsWithHierarchy( painter.ActiveContext.Batcher );
	}

	static void ReplacePointsWithHierarchy( PainterBatcher batcher )
	{
		var remap = new Dictionary<int, int>();
		int Remap( int index )
		{
			if ( index < 0 ) return index;
			if ( remap.TryGetValue( index, out var mapped ) ) return mapped;
			var shape = batcher.Shapes[index];
			mapped = index;
			if ( shape.Kind == UICssBoxBatched.ShapeKind.PolygonPath && shape.PathNodeCount == -1 )
			{
				var edges = new UICssBoxBatched.PathPrimitive[shape.PathCount];
				for ( int i = 0; i < edges.Length; i++ )
				{
					var a = batcher.PolygonPoints[shape.PathOffset + i];
					var b = batcher.PolygonPoints[shape.PathOffset + (i + 1) % edges.Length];
					edges[i].A = new Vector4( a.x, a.y, b.x, b.y );
				}
				mapped = batcher.AddPath( new UICssBoxBatched.BorderShape { Kind = shape.Kind }, edges );
			}
			else if ( shape.Kind == UICssBoxBatched.ShapeKind.PolygonStroke )
			{
				shape.PolygonCount = Remap( shape.PolygonStrokeShapeIndex );
				mapped = batcher.AddShape( shape );
			}
			remap[index] = mapped;
			return mapped;
		}

		var instances = batcher.Instances.ToArray();
		batcher.Rewind( 0 );
		foreach ( var instance in instances )
		{
			var replacement = instance;
			replacement.ShapeIndex = Remap( instance.ShapeIndex );
			batcher.Add( replacement );
		}
	}
}
