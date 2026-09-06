using System.Runtime.InteropServices;
using Sandbox.UI;

namespace UITests;

[TestClass]
public class PolygonBoxRenderingTest
{
	[TestMethod]
	public void ContinuousTextureUseIsCulledByScissor()
	{
		var scissor = PanelRenderer.GPUScissor.Single( new Rect( 0, 0, 200, 200 ), BorderRadii.Zero, Matrix.Identity );

		Assert.IsTrue( PanelRenderer.OverlapsScissor( new Rect( 50, 50, 100, 100 ), Matrix.Identity, scissor ) );
		Assert.IsTrue( PanelRenderer.OverlapsScissor( new Rect( 150, 150, 100, 100 ), Matrix.Identity, scissor ) );
		Assert.IsFalse( PanelRenderer.OverlapsScissor( new Rect( 250, 50, 100, 100 ), Matrix.Identity, scissor ) );
		Assert.IsFalse( PanelRenderer.OverlapsScissor( new Rect( 50, 250, 100, 100 ), Matrix.Identity, scissor ) );
	}

	[TestMethod]
	public void PolygonPayloadLayoutAndPacking()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-shape", "polygon(0% 0%, 100% 0%, 100% 100%, 0% 100%)" ) );

		var desc = new BoxDrawDescriptor( new Rect( 10, 20, 100, 200 ), Color.White );
		desc.SetBorderShape( style.BorderShape );

		Assert.AreEqual( new Vector4( 0, 0, 100, 0 ), desc.BorderShapeData.Polygon01 );
		Assert.AreEqual( new Vector4( 100, 200, 0, 200 ), desc.BorderShapeData.Polygon23 );
		Assert.AreEqual( 4, desc.BorderShapeData.PolygonCount );
		Assert.IsTrue( desc.HasBorderShape );

		// The shape rides in its own table, so a box only carries an index into it - keeping
		// GPUBoxInstance the same size for the overwhelming majority of boxes, which have no shape.
		Assert.AreEqual( Offset( nameof( GPUBoxInstance.BackgroundClipRect ) ) + 16, Offset( nameof( GPUBoxInstance.ShapeIndex ) ) );
		Assert.AreEqual( Offset( nameof( GPUBoxInstance.ShapeIndex ) ) + 4, Marshal.SizeOf<GPUBoxInstance>() );

		// GPUBorderShape mirrors BorderShapeData in ui_cssbox_batched.shader, so it has to stay
		// contiguous and unpadded.
		Assert.AreEqual( ShapeOffset( nameof( GPUBorderShape.Polygon01 ) ) + 16, ShapeOffset( nameof( GPUBorderShape.Polygon23 ) ) );
		Assert.AreEqual( ShapeOffset( nameof( GPUBorderShape.Polygon23 ) ) + 16, ShapeOffset( nameof( GPUBorderShape.Polygon45 ) ) );
		Assert.AreEqual( ShapeOffset( nameof( GPUBorderShape.Polygon45 ) ) + 16, ShapeOffset( nameof( GPUBorderShape.Polygon67 ) ) );
		Assert.AreEqual( ShapeOffset( nameof( GPUBorderShape.Polygon67 ) ) + 16, ShapeOffset( nameof( GPUBorderShape.PolygonCount ) ) );
		Assert.AreEqual( ShapeOffset( nameof( GPUBorderShape.PolygonCount ) ) + 4, ShapeOffset( nameof( GPUBorderShape.Circle ) ) );
		Assert.AreEqual( ShapeOffset( nameof( GPUBorderShape.Circle ) ) + 16, ShapeOffset( nameof( GPUBorderShape.Kind ) ) );
		Assert.AreEqual( ShapeOffset( nameof( GPUBorderShape.Kind ) ) + 4, Marshal.SizeOf<GPUBorderShape>() );

		Assert.IsTrue( style.Set( "border-shape", "circle(25% at 40% 60%)" ) );
		desc = new BoxDrawDescriptor( new Rect( 10, 20, 100, 200 ), Color.White );
		desc.SetBorderShape( style.BorderShape );
		Assert.AreEqual( (int)BorderShapeKind.Circle, desc.BorderShapeData.Kind );
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
		var desc = new BoxDrawDescriptor( new Rect( 0, 0, 50, 50 ), Color.White );
		desc.SetBorderShape( BorderShape.None );

		Assert.IsFalse( desc.HasBorderShape );
		Assert.AreEqual( 0, desc.BorderShapeData.Kind );

		var batcher = new UIBatcher();
		Assert.AreEqual( -1, batcher.GetOrAddShape( desc.BorderShapeData ) );
		Assert.AreEqual( 0, batcher.BorderShapeCount );
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
		var a = new BoxDrawDescriptor( rect, Color.White );
		var b = new BoxDrawDescriptor( rect, Color.Red );
		a.SetBorderShape( style.BorderShape );
		b.SetBorderShape( style.BorderShape );

		var batcher = new UIBatcher();
		var first = batcher.GetOrAddShape( a.BorderShapeData );

		Assert.AreEqual( first, batcher.GetOrAddShape( b.BorderShapeData ) );
		Assert.AreEqual( 1, batcher.BorderShapeCount );

		// A different shape has to take its own slot
		var c = new BoxDrawDescriptor( rect, Color.White );
		Assert.IsTrue( style.Set( "border-shape", "circle(40%)" ) );
		c.SetBorderShape( style.BorderShape );

		Assert.AreNotEqual( first, batcher.GetOrAddShape( c.BorderShapeData ) );
		Assert.AreEqual( 2, batcher.BorderShapeCount );
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

		var a = new BoxDrawDescriptor( new Rect( 0, 0, 120, 80 ), Color.White );
		var b = new BoxDrawDescriptor( new Rect( 640, 300, 120, 80 ), Color.White );
		a.SetBorderShape( style.BorderShape );
		b.SetBorderShape( style.BorderShape );

		Assert.AreEqual( a.BorderShapeData.Polygon01, b.BorderShapeData.Polygon01 );

		var batcher = new UIBatcher();
		Assert.AreEqual( batcher.GetOrAddShape( a.BorderShapeData ), batcher.GetOrAddShape( b.BorderShapeData ) );
		Assert.AreEqual( 1, batcher.BorderShapeCount );

		// A different box size is genuinely a different shape and still takes its own slot
		var c = new BoxDrawDescriptor( new Rect( 0, 0, 200, 80 ), Color.White );
		c.SetBorderShape( style.BorderShape );

		Assert.AreNotEqual( batcher.GetOrAddShape( a.BorderShapeData ), batcher.GetOrAddShape( c.BorderShapeData ) );
		Assert.AreEqual( 2, batcher.BorderShapeCount );
	}

	static int Offset( string field ) => Marshal.OffsetOf<GPUBoxInstance>( field ).ToInt32();
	static int ShapeOffset( string field ) => Marshal.OffsetOf<GPUBorderShape>( field ).ToInt32();
}
