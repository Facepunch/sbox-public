using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox.UI;

namespace UITests;

/// <summary>Checks shape contracts without submitting draws or uploading buffers.</summary>
[TestClass]
public class ShapeStylesTest : PainterTestBase
{
	[TestMethod]
	public void LayerErrorsIdentifyTheInvalidValue()
	{
		foreach ( float opacity in new[] { -1f, 2f, float.NaN, float.PositiveInfinity } )
		{
			var error = Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
			{
				using var layer = Paint.BeginLayer( new Rect( 0, 0, 32, 32 ), opacity );
			} );
			Assert.AreEqual( "opacity", error.ParamName );
		}
		var tintError = Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
			Paint.FilterBackdrop( new Rect( 0, 0, 32, 32 ), new Painter.Filter { Tint = new Color( float.NaN, 0, 0 ) } ) );
		Assert.AreEqual( "Tint", tintError.ParamName );
		Assert.AreEqual( 0, PaintContext.Batcher.Count );
	}

	[TestMethod]
	public void GradientHashCollisionsDoNotReuseOtherValues()
	{
		// Empty stop arrays deliberately hash to zero, regardless of the other gradient fields.
		var first = new GradientInfo { Angle = 0 };
		var second = first with { Angle = 1 };
		Assert.AreEqual( first.GetHashCode(), second.GetHashCode() );
		var batcher = PaintContext.Batcher;
		int a = batcher.GetOrAddGradient( first );
		int b = batcher.GetOrAddGradient( second );
		Assert.AreNotEqual( a, b );
		Assert.AreEqual( a, batcher.GetOrAddGradient( first ) );
		Assert.AreEqual( b, batcher.GetOrAddGradient( second ) );
		Assert.AreEqual( 0f, batcher.Gradients[a].Angle );
		Assert.AreEqual( 1f, batcher.Gradients[b].Angle );
	}

	[TestMethod]
	public void RepeatedGpuTableLookupsReuseEntries()
	{
		var batcher = PaintContext.Batcher;
		var transform = Matrix.CreateTranslation( new Vector3( 10, 20, 0 ) );
		var scissor = Painter.Scissoring.Single( new Rect( 0, 0, 32, 32 ), BorderRadii.Zero, transform );
		var gradient = new GradientInfo { ColorOffsets = GradientStops.Create( [new() { color = Color.Red, offset = 0 }, new() { color = Color.Blue, offset = 1 }] ) };
		for ( int i = 0; i < 2; i++ )
		{
			batcher.GetOrAddTransform( transform );
			batcher.GetOrAddScissor( scissor );
			batcher.GetOrAddGradient( gradient );
		}
		Assert.AreEqual( 1, batcher.Transforms.Count );
		Assert.AreEqual( 1, batcher.Scissors.Count );
		Assert.AreEqual( 1, batcher.Gradients.Count );
	}

	[TestMethod]
	public void DrawScopesRestoreFillAndStroke()
	{
		using var scope = Paint.Scope();
		PaintFill = Color.Green;
		PaintStroke = Stroke.Solid( Color.Blue, 5 );
		using ( Paint.Scope() )
		{
			PaintFill = Color.Red;
			PaintStroke = Stroke.Solid( Color.White, 2 );
			using ( Paint.Scope() )
			{
				PaintFill = Fill.None;
				PaintStroke = Stroke.None;
			}
			Assert.AreEqual( Fill.Solid( Color.Red ), PaintFill );
			Assert.AreEqual( Stroke.Solid( Color.White, 2 ), PaintStroke );
		}
		Assert.AreEqual( Fill.Solid( Color.Green ), PaintFill );
		Assert.AreEqual( Stroke.Solid( Color.Blue, 5 ), PaintStroke );
		Assert.IsTrue( typeof( Painter.StateScope ).IsByRefLike );
	}

	[TestMethod]
	public void StrokeStateCanBeCopiedUpdatedAndCleared()
	{
		using var scope = Paint.Scope();
		PaintStroke = Stroke.Solid( Color.White, 2 ) with { Cap = Stroke.LineCap.Round };
		var stroke = PaintStroke;
		PaintStroke = stroke with { Width = 3, Offset = 4 };
		Assert.AreEqual( 3f, PaintStroke.Width );
		Assert.AreEqual( 4f, PaintStroke.Offset );
		Assert.AreEqual( 2f, stroke.Width );
		PaintStroke = Stroke.Dotted( Color.Red, 2, 3, 4 );
		Assert.AreEqual( 2f, PaintStroke.Width );
		Assert.AreEqual( 3f, PaintStroke.Gap );
		Assert.AreEqual( 4f, PaintStroke.Offset );
		PaintStroke = Stroke.Dashed( Color.Blue, 2, 6, 3, 4 );
		Assert.AreEqual( 2f, PaintStroke.Width );
		Assert.AreEqual( 6f, PaintStroke.DashLength );
		Assert.AreEqual( 3f, PaintStroke.Gap );
		Assert.AreEqual( 4f, PaintStroke.Offset );
		PaintStroke = Stroke.None;
		Assert.AreEqual( default( Stroke ), PaintStroke );
	}

	[TestMethod]
	public void FillStateCanBeCopiedAndCleared()
	{
		using var scope = Paint.Scope();
		PaintFill = Fill.Solid( Color.White );
		var saved = PaintFill;
		PaintFill = Color.Red.WithAlpha( 0.5f );
		Assert.IsFalse( PaintFill.IsTransparent );
		PaintFill = Fill.LinearGradient( Color.Red, Color.Blue );
		Assert.IsFalse( PaintFill.IsTransparent );
		PaintFill = saved;
		Assert.AreEqual( Fill.Solid( Color.White ), PaintFill );
		Assert.IsFalse( PaintFill.IsTransparent );
		PaintFill = Fill.None;
		Assert.AreEqual( Fill.None, PaintFill );
	}

	[TestMethod]
	public void PreparedPathOwnsItsGeometry()
	{
		var primitives = new[] { new UICssBoxBatched.PathPrimitive { Kind = UICssBoxBatched.PathPrimitiveKind.Segment, A = new Vector4( 1, 2, 3, 4 ) } };
		var path = new Painter.Path.Data( new UICssBoxBatched.BorderShape { Kind = UICssBoxBatched.ShapeKind.PolygonPath }, primitives );
		primitives[0] = default;
		Assert.AreEqual( new Vector4( 1, 2, 3, 4 ), path.Primitives[0].A );
		Assert.AreEqual( new Vector4( 1, 2, 3, 4 ), path.Nodes[0].Bounds );
	}

	/// <summary>The threaded hierarchy agrees with brute-force bounds queries and prunes distant geometry.</summary>
	[TestMethod]
	public void PathHierarchyFindsEveryCandidate()
	{
		var random = new Random( 17 );
		var bounds = Enumerable.Range( 0, 2000 ).Select( i => new Rect( random.Next( 1000 ), random.Next( 1000 ), random.Next( 30 ), random.Next( 30 ) ) ).ToArray();
		var edges = bounds.Select( rect => new UICssBoxBatched.PathPrimitive
		{
			Kind = UICssBoxBatched.PathPrimitiveKind.Segment,
			A = new Vector4( rect.Left, rect.Top, rect.Right, rect.Bottom ),
		} ).ToArray();
		var path = new Painter.Path.Data( new UICssBoxBatched.BorderShape { Kind = UICssBoxBatched.ShapeKind.PolygonPath }, edges );
		Assert.AreEqual( bounds.Length * 2 - 1, path.Nodes.Length );
		for ( int query = 0; query < 100; query++ )
		{
			var point = new Vector2( random.Next( 1000 ), random.Next( 1000 ) );
			var found = new List<int>();
			int visited = 0;
			for ( int i = 0; i < path.Nodes.Length; )
			{
				var node = path.Nodes[i];
				visited++;
				Assert.IsTrue( node.Next > i && node.Next <= path.Nodes.Length );
				if ( point.x < node.Bounds.x || point.y < node.Bounds.y || point.x > node.Bounds.z || point.y > node.Bounds.w ) { i = node.Next; continue; }
				i++;
				if ( node.Primitive >= 0 ) found.Add( node.Primitive );
			}
			var expected = Enumerable.Range( 0, bounds.Length ).Where( i => point.x >= bounds[i].Left && point.x <= bounds[i].Right && point.y >= bounds[i].Top && point.y <= bounds[i].Bottom ).ToArray();
			CollectionAssert.AreEquivalent( expected, found );
			Assert.IsTrue( visited < path.Nodes.Length / 4, "Sparse queries should skip most of the hierarchy." );
		}
	}

	/// <summary>Path fields match the shader's four-byte structured-buffer layout.</summary>
	[TestMethod]
	public void PathLayoutAndShapeIdentity()
	{
		Assert.AreEqual( 104, Marshal.SizeOf<UICssBoxBatched.BorderShape>() );
		Assert.AreEqual( 56, Marshal.SizeOf<UICssBoxBatched.PathPrimitive>() );
		Assert.AreEqual( 24, Marshal.SizeOf<UICssBoxBatched.PathNode>() );
		Assert.AreEqual( 16, Marshal.OffsetOf<UICssBoxBatched.PathNode>( nameof( UICssBoxBatched.PathNode.Next ) ).ToInt32() );
		Assert.AreEqual( 20, Marshal.OffsetOf<UICssBoxBatched.PathNode>( nameof( UICssBoxBatched.PathNode.Primitive ) ).ToInt32() );
		string[] fields = [nameof( UICssBoxBatched.PathPrimitive.A ), nameof( UICssBoxBatched.PathPrimitive.B ), nameof( UICssBoxBatched.PathPrimitive.C ), nameof( UICssBoxBatched.PathPrimitive.Kind ), nameof( UICssBoxBatched.PathPrimitive.Count )];
		int[] offsets = [0, 16, 32, 48, 52];
		for ( int i = 0; i < fields.Length; i++ )
			Assert.AreEqual( offsets[i], Marshal.OffsetOf<UICssBoxBatched.PathPrimitive>( fields[i] ).ToInt32() );
		var shape = new UICssBoxBatched.BorderShape { Kind = UICssBoxBatched.ShapeKind.StrokePath, PathOffset = 7, PathCount = 3 };
		var batcher = new PainterBatcher( new Sandbox.Rendering.CommandList() );
		Assert.AreEqual( 0, batcher.GetOrAddShape( shape ) );
		Assert.AreEqual( 1, batcher.GetOrAddShape( shape with { PathOffset = 8 } ) );
		Assert.AreEqual( 2, batcher.GetOrAddShape( shape with { PathCount = 4 } ) );
		Assert.AreEqual( 0, batcher.GetOrAddShape( shape ) );
	}

	/// <summary>Capacity growth cannot round a valid table beyond the native byte limit.</summary>
	[TestMethod]
	public void PathBufferCapacityUsesTheNativeByteLimit()
	{
		int maximum = PainterBatcher.MaxBufferElements<UICssBoxBatched.PathPrimitive>();
		Assert.AreEqual( maximum, PainterBatcher.GetBufferCapacity<UICssBoxBatched.PathPrimitive>( maximum ) );
		Assert.IsTrue( (long)maximum * Marshal.SizeOf<UICssBoxBatched.PathPrimitive>() <= uint.MaxValue );
		Assert.ThrowsException<InvalidOperationException>( () => PainterBatcher.GetBufferCapacity<UICssBoxBatched.PathPrimitive>( (long)maximum + 1 ) );
		Assert.ThrowsException<InvalidOperationException>( () => PainterBatcher.GetBufferCapacity<UICssBoxBatched.PathPrimitive>( long.MaxValue ) );
	}

	/// <summary>Cached paths deduplicate by identity and receive fresh indices each frame.</summary>
	[TestMethod]
	public void PathTablesResetWithoutMutatingCachedData()
	{
		var shape = new UICssBoxBatched.BorderShape { Kind = UICssBoxBatched.ShapeKind.StrokePath };
		var first = new Painter.Path.Data( shape, new UICssBoxBatched.PathPrimitive[2000] );
		var second = new Painter.Path.Data( shape, new UICssBoxBatched.PathPrimitive[3] );
		var batcher = new PainterBatcher( new Sandbox.Rendering.CommandList() );
		for ( int frame = 0; frame < 4; frame++ )
		{
			Assert.AreEqual( 0, batcher.GetOrAddPath( first ) );
			Assert.AreEqual( 1, batcher.GetOrAddPath( second ) );
			Assert.AreEqual( 0, batcher.GetOrAddPath( first ) );
			Assert.AreEqual( 2003, batcher.Paths.Count );
			Assert.AreEqual( 4004, batcher.PathNodes.Count );
			Assert.AreEqual( 2, batcher.Shapes.Count );
			Assert.AreEqual( shape, second.Shape );
			Assert.AreEqual( 0, batcher.GpuBufferCount );
			batcher.AdvanceFrame();
			Assert.AreEqual( 0, batcher.Paths.Count );
			Assert.AreEqual( 0, batcher.PathNodes.Count );
			Assert.AreEqual( 0, batcher.Shapes.Count );
			(first, second) = (second, first);
		}
	}

	[TestMethod]
	public void PositionedRadialGradientValidatesItsGeometryAndStops()
	{
		var center = new Vector2( 10, 20 );
		var edge = new Vector2( 40, 60 );
		foreach ( var invalid in new[] { new Vector2( float.NaN, 0 ), new Vector2( 0, float.PositiveInfinity ) } )
		{
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.RadialGradient( invalid, edge, Color.Red, Color.Blue ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.RadialGradient( center, invalid, Color.Red, Color.Blue ) );
		}
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.RadialGradient( center, center, Color.Red, Color.Blue ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.RadialGradient( center, new Vector2( float.MaxValue ), Color.Red, Color.Blue ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.RadialGradient( center, edge, [] ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.RadialGradient( center, edge, [new( 0.8f, Color.Red ), new( 0.2f, Color.Blue )] ) );
		Assert.IsFalse( Fill.RadialGradient( center, edge, [new( 0.5f, Color.Red ), new( 0.5f, Color.Blue )] ).IsTransparent );
		Assert.IsFalse( Fill.RadialGradient( center, edge, Color.Red, Color.Blue ).IsTransparent );
	}

	[TestMethod]
	public void GradientCoordinatesValidateLengths()
	{
		Func<Length?, Length?, Length?, Length?, Fill>[] factories = [
			(x, y, endX, endY) => Fill.LinearGradient( x, y, endX, endY, Color.Red, Color.Blue ),
			(x, y, endX, endY) => Fill.RadialGradient( x, y, endX, endY, Color.Red, Color.Blue ),
			(x, y, endX, endY) => Fill.ConicGradient( x, y, endX, endY, Color.Red, Color.Blue )];
		foreach ( var factory in factories )
		{
			foreach ( var invalid in new Length?[] { null, Length.Auto, Length.Cover, Length.Pixels( float.NaN ), Length.Percent( float.PositiveInfinity ), new Length { Unit = (LengthUnit)255 } } )
				Assert.ThrowsException<ArgumentOutOfRangeException>( () => factory( invalid, 0, 10, 10 ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => factory( Length.Percent( 50 ), 20, Length.Percent( 50 ), 20 ) );
			Assert.IsFalse( factory( -10, -20, Length.Percent( 150 ), Length.Percent( 200 ) ).IsTransparent );
		}
	}

	/// <summary>Gradient arrays validate count, ordering, offsets and finite colors.</summary>
	[TestMethod]
	public void GradientValidationAndStrokeDefaults()
	{
		var stroke = Stroke.Solid( Color.Red );
		Assert.AreEqual( 1f, stroke.Width );
		Assert.AreEqual( Stroke.LineCap.Butt, stroke.Cap );
		Assert.AreEqual( Stroke.LineJoin.Round, stroke.Join );
		Assert.AreEqual( 4f, stroke.MiterLimit );
		Assert.AreEqual( BorderStyle.Solid, stroke.Style );
		Assert.IsTrue( default( Fill ).IsTransparent );
		Assert.AreEqual( 0f, default( Stroke ).Width );
		foreach ( var count in new[] { 0, 1, 9 } )
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.LinearGradient( new Fill.GradientStop[count] ) );
		foreach ( var offset in new[] { -1f, 2f, float.NaN, float.PositiveInfinity } )
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.RadialGradient( [new( 0, Color.Red ), new( offset, Color.Blue )] ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.ConicGradient( [new( 0.8f, Color.Red ), new( 0.2f, Color.Blue )] ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.LinearGradient( Color.Red, Color.Blue, float.NaN ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.RadialGradient( Color.Red, new Color( float.NaN, 0, 0 ) ) );
		Assert.ThrowsException<ArgumentNullException>( () => Fill.Image( null ) );
		Assert.IsFalse( Fill.LinearGradient( [new( 0.5f, Color.Red ), new( 0.5f, Color.Blue )] ).IsTransparent );
	}
}
