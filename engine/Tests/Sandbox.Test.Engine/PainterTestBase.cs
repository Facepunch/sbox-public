using Sandbox.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox;

public abstract class PainterTestBase
{
	private protected readonly Painter.Context PaintContext = new( new Sandbox.Rendering.CommandList() );
	private protected Painter Paint => PaintContext.Painter;

	[TestInitialize]
	public void BeginPainterTest() => PaintContext.Begin( new Rect( 0, 0, 1000, 1000 ) );

	[TestCleanup]
	public void EndPainterTest()
	{
		PaintContext.End();
		PaintContext.CommandList.Reset();
		PaintContext.Batcher.Dispose();
	}

	// Existing action-based tests acquire a temporary handle for each property access.
	private protected Fill PaintFill
	{
		get => Paint.Fill;
		set { var painter = Paint; painter.Fill = value; }
	}
	private protected Stroke PaintStroke
	{
		get => Paint.Stroke;
		set { var painter = Paint; painter.Stroke = value; }
	}
	private protected Matrix PaintTransform
	{
		get => Paint.Transform;
		set { var painter = Paint; painter.Transform = value; }
	}
	private protected float PaintOpacity
	{
		get => Paint.Opacity;
		set { var painter = Paint; painter.Opacity = value; }
	}
	private protected TextStyle PaintTextStyle
	{
		get => Paint.TextStyle;
		set { var painter = Paint; painter.TextStyle = value; }
	}
	private protected BlendMode PaintBlendMode
	{
		get => Paint.BlendMode;
		set { var painter = Paint; painter.BlendMode = value; }
	}
}
