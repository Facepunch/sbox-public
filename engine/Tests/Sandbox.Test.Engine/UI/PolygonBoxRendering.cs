using System.Runtime.InteropServices;
using Sandbox.UI;

namespace UITests;

[TestClass]
public class PolygonBoxRenderingTest
{
	[TestMethod]
	public void ContinuousTextureUseIsCulledByScissor()
	{
		var scissor = Painter.Scissoring.Single( new Rect( 0, 0, 200, 200 ), BorderRadii.Zero, Matrix.Identity );

		Assert.IsTrue( PainterBatcher.OverlapsScissor( new Rect( 50, 50, 100, 100 ), Matrix.Identity, scissor ) );
		Assert.IsTrue( PainterBatcher.OverlapsScissor( new Rect( 150, 150, 100, 100 ), Matrix.Identity, scissor ) );
		Assert.IsFalse( PainterBatcher.OverlapsScissor( new Rect( 250, 50, 100, 100 ), Matrix.Identity, scissor ) );
		Assert.IsFalse( PainterBatcher.OverlapsScissor( new Rect( 50, 250, 100, 100 ), Matrix.Identity, scissor ) );
	}

	[TestMethod]
	public void PolygonPayloadLayoutAndPacking()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-shape", "polygon(0% 0%, 100% 0%, 100% 100%, 0% 100%)" ) );

		var desc = new Painter.BoxDescriptor( new Rect( 10, 20, 100, 200 ), Color.White );
		Painter.SetBorderShape( ref desc, style.BorderShape );

		Assert.AreEqual( new Vector4( 0, 0, 100, 0 ), desc.BorderShapeData.Polygon01 );
		Assert.AreEqual( new Vector4( 100, 200, 0, 200 ), desc.BorderShapeData.Polygon23 );
		Assert.AreEqual( 4, desc.BorderShapeData.PolygonCount );
		Assert.IsTrue( desc.HasBorderShape );

		// The shape rides in its own table, so a box only carries an index into it - keeping
		// UICssBoxBatched.BoxInstance the same size for the overwhelming majority of boxes, which have no shape.
		Assert.AreEqual( Offset( nameof( UICssBoxBatched.BoxInstance.BackgroundClipRect ) ) + 16, Offset( nameof( UICssBoxBatched.BoxInstance.ShapeIndex ) ) );
		Assert.AreEqual( Offset( nameof( UICssBoxBatched.BoxInstance.ShapeIndex ) ) + 4, Marshal.SizeOf<UICssBoxBatched.BoxInstance>() );

		// UICssBoxBatched.BorderShape mirrors BorderShapeData in ui_cssbox_batched.shader, so it has to stay
		// contiguous and unpadded.
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.Polygon01 ) ) + 16, ShapeOffset( nameof( UICssBoxBatched.BorderShape.Polygon23 ) ) );
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.Polygon23 ) ) + 16, ShapeOffset( nameof( UICssBoxBatched.BorderShape.Polygon45 ) ) );
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.Polygon45 ) ) + 16, ShapeOffset( nameof( UICssBoxBatched.BorderShape.Polygon67 ) ) );
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.Polygon67 ) ) + 16, ShapeOffset( nameof( UICssBoxBatched.BorderShape.PolygonCount ) ) );
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.PolygonCount ) ) + 4, ShapeOffset( nameof( UICssBoxBatched.BorderShape.Circle ) ) );
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.Circle ) ) + 16, ShapeOffset( nameof( UICssBoxBatched.BorderShape.PathOffset ) ) );
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.PathOffset ) ) + 4, ShapeOffset( nameof( UICssBoxBatched.BorderShape.PathCount ) ) );
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.PathCount ) ) + 4, ShapeOffset( nameof( UICssBoxBatched.BorderShape.PathNodeOffset ) ) );
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.PathNodeOffset ) ) + 4, ShapeOffset( nameof( UICssBoxBatched.BorderShape.PathNodeCount ) ) );
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.PathNodeCount ) ) + 4, ShapeOffset( nameof( UICssBoxBatched.BorderShape.Kind ) ) );
		Assert.AreEqual( ShapeOffset( nameof( UICssBoxBatched.BorderShape.Kind ) ) + 4, Marshal.SizeOf<UICssBoxBatched.BorderShape>() );

		Assert.IsTrue( style.Set( "border-shape", "circle(25% at 40% 60%)" ) );
		desc = new Painter.BoxDescriptor( new Rect( 10, 20, 100, 200 ), Color.White );
		Painter.SetBorderShape( ref desc, style.BorderShape );
		Assert.AreEqual( UICssBoxBatched.ShapeKind.Circle, desc.BorderShapeData.Kind );
		Assert.AreEqual( 40, desc.BorderShapeData.Circle.x, 0.001f );
		Assert.AreEqual( 120, desc.BorderShapeData.Circle.y, 0.001f );
		Assert.AreEqual( 39.52847f, desc.BorderShapeData.Circle.z, 0.001f );
	}

	/// <summary>
	/// A box with no border shape must not claim a table slot, or every plain panel in the frame
	/// would push an entry into the shape buffer and the indirection would cost more than it saves.
	/// </summary>
	[TestMethod]
	public void UnshapedBoxCarriesNoShapeIndex()
	{
		var desc = new Painter.BoxDescriptor( new Rect( 0, 0, 50, 50 ), Color.White );
		Painter.SetBorderShape( ref desc, BorderShape.None );

		Assert.IsFalse( desc.HasBorderShape );
		Assert.AreEqual( UICssBoxBatched.ShapeKind.None, desc.BorderShapeData.Kind );

		var batcher = new PainterBatcher( new Sandbox.Rendering.CommandList() );
		Assert.AreEqual( -1, batcher.GetOrAddShape( desc.BorderShapeData ) );
		Assert.AreEqual( 0, batcher.Shapes.Count );
	}

	/// <summary>
	/// The whole point of the table: panels sharing a shape share one entry.
	/// </summary>
	[TestMethod]
	public void IdenticalShapesShareOneTableEntry()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-shape", "polygon(50% 0%, 100% 100%, 0% 100%)" ) );

		var rect = new Rect( 0, 0, 100, 100 );
		var a = new Painter.BoxDescriptor( rect, Color.White );
		var b = new Painter.BoxDescriptor( rect, Color.Red );
		Painter.SetBorderShape( ref a, style.BorderShape );
		Painter.SetBorderShape( ref b, style.BorderShape );

		var batcher = new PainterBatcher( new Sandbox.Rendering.CommandList() );
		var first = batcher.GetOrAddShape( a.BorderShapeData );

		Assert.AreEqual( first, batcher.GetOrAddShape( b.BorderShapeData ) );
		Assert.AreEqual( 1, batcher.Shapes.Count );

		// A different shape has to take its own slot
		var c = new Painter.BoxDescriptor( rect, Color.White );
		Assert.IsTrue( style.Set( "border-shape", "circle(40%)" ) );
		Painter.SetBorderShape( ref c, style.BorderShape );

		Assert.AreNotEqual( first, batcher.GetOrAddShape( c.BorderShapeData ) );
		Assert.AreEqual( 2, batcher.Shapes.Count );
	}

	/// <summary>
	/// Shape coordinates are relative to the box, so the same shape on panels at different
	/// positions has to collapse to one table entry. When they were resolved into layout space
	/// every panel got its own, and a list of identical rows filled the table with duplicates.
	/// </summary>
	[TestMethod]
	public void SameShapeAtDifferentPositionsSharesOneEntry()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-shape", "polygon(50% 0%, 100% 100%, 0% 100%)" ) );

		var a = new Painter.BoxDescriptor( new Rect( 0, 0, 120, 80 ), Color.White );
		var b = new Painter.BoxDescriptor( new Rect( 640, 300, 120, 80 ), Color.White );
		Painter.SetBorderShape( ref a, style.BorderShape );
		Painter.SetBorderShape( ref b, style.BorderShape );

		Assert.AreEqual( a.BorderShapeData.Polygon01, b.BorderShapeData.Polygon01 );

		var batcher = new PainterBatcher( new Sandbox.Rendering.CommandList() );
		Assert.AreEqual( batcher.GetOrAddShape( a.BorderShapeData ), batcher.GetOrAddShape( b.BorderShapeData ) );
		Assert.AreEqual( 1, batcher.Shapes.Count );

		// A different box size is genuinely a different shape and still takes its own slot
		var c = new Painter.BoxDescriptor( new Rect( 0, 0, 200, 80 ), Color.White );
		Painter.SetBorderShape( ref c, style.BorderShape );

		Assert.AreNotEqual( batcher.GetOrAddShape( a.BorderShapeData ), batcher.GetOrAddShape( c.BorderShapeData ) );
		Assert.AreEqual( 2, batcher.Shapes.Count );
	}

	/// <summary>Odd polygon counts clear the unused half-pair and all remaining slots.</summary>
	[TestMethod]
	public void OddPolygonCountClearsUnusedSlots()
	{
		var style = new Styles();
		var desc = new Painter.BoxDescriptor( new Rect( 10, 20, 100, 200 ), Color.White );
		Assert.IsTrue( style.Set( "border-shape", "polygon(0% 0%, 50% 0%, 100% 0%, 100% 50%, 100% 100%, 50% 100%, 0% 100%, 0% 50%)" ) );
		Painter.SetBorderShape( ref desc, style.BorderShape );
		Assert.AreNotEqual( Vector4.Zero, desc.BorderShapeData.Polygon67 );

		Assert.IsTrue( style.Set( "border-shape", "polygon(50% 0%, 100% 100%, 0% 100%)" ) );
		Painter.SetBorderShape( ref desc, style.BorderShape );
		Assert.AreEqual( 3, desc.BorderShapeData.PolygonCount );
		Assert.AreEqual( new Vector4( 50, 0, 100, 200 ), desc.BorderShapeData.Polygon01 );
		Assert.AreEqual( new Vector4( 0, 200, 0, 0 ), desc.BorderShapeData.Polygon23 );
		Assert.AreEqual( Vector4.Zero, desc.BorderShapeData.Polygon45 );
		Assert.AreEqual( Vector4.Zero, desc.BorderShapeData.Polygon67 );
	}

	static int Offset( string field ) => Marshal.OffsetOf<UICssBoxBatched.BoxInstance>( field ).ToInt32();
	static int ShapeOffset( string field ) => Marshal.OffsetOf<UICssBoxBatched.BorderShape>( field ).ToInt32();
}
