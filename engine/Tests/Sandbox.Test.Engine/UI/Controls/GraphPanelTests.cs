using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class GraphPanelTests
{
	UISurface _surface;
	bool _previousTextRendering;

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		_previousTextRendering = UiTesting.DisableTextRendering();
		_surface = new UISurface { Size = new Vector2( 1000, 800 ), DpiScale = 1, MouseInside = true };
	}

	[TestCleanup]
	public void Cleanup()
	{
		_surface.Dispose();
		TextBlock.ui_rendertext = _previousTextRendering;
	}

	void Frame()
	{
		for ( int i = 0; i < 4; i++ )
			UiTesting.Frame( _surface );
	}

	GraphPanel CreateGraph()
	{
		var graph = _surface.Root.AddChild<GraphPanel>();
		graph.Style.Width = 600;
		graph.Style.Height = 400;
		graph.SetView( new( -2, -10 ), new( 8, 30 ) );
		Frame();
		return graph;
	}

	[TestMethod]
	public void AxisFieldsAreOptionalAndReportEditsWithoutChangingTheView()
	{
		var graph = CreateGraph();
		var maximum = graph.Descendants.OfType<NumberEntry>().Single( entry => entry.HasClass( "axis-y-max" ) );
		Assert.IsFalse( maximum.IsVisible );
		graph.EditableAxes = true;
		Frame();
		Assert.IsTrue( maximum.IsVisible );
		Assert.AreEqual( "30.0", maximum.Text );
		int edits = 0;
		graph.AxisBoundEdited += ( axis, upper, value ) =>
		{
			Assert.AreEqual( GraphPanel.Axis.Vertical, axis );
			Assert.IsTrue( upper );
			Assert.AreEqual( 50f, value );
			edits++;
		};
		maximum.OnTextEdited( "50" );
		maximum.OnTextEdited( "NaN" );
		maximum.OnTextEdited( "invalid" );
		Assert.AreEqual( 1, edits );
		Assert.AreEqual( new Vector2( 8, 30 ), graph.ViewMax );
	}

	[TestMethod]
	public void PlotCoordinatesRespectGuttersDpiAndAxisDirection()
	{
		_surface.DpiScale = 1.5f;
		var graph = CreateGraph();
		var minimum = graph.CanvasToScreen( graph.ViewMin );
		var maximum = graph.CanvasToScreen( graph.ViewMax );
		Assert.AreEqual( 60 * graph.ScaleToScreen, minimum.x, 0.01f );
		Assert.AreEqual( 16 * graph.ScaleToScreen, maximum.y, 0.01f );
		Assert.IsTrue( minimum.y > maximum.y );
		graph.YAxisUp = false;
		Frame();
		Assert.IsTrue( graph.HasClass( "y-axis-down" ) );
		Assert.AreEqual( 16 * graph.ScaleToScreen, graph.CanvasToScreen( graph.ViewMin ).y, 0.01f );
		var point = new Vector2( 3, 12 );
		Assert.IsTrue( (point - graph.ScreenToCanvas( graph.CanvasToScreen( point ) )).Length < 0.001f );
	}

	[TestMethod]
	public void UniformScaleAxisFieldsIncludeTheExtraVisibleSpace()
	{
		var graph = CreateGraph();
		graph.PreserveAspectRatio = true;
		graph.EditableAxes = true;
		Frame();
		var minimum = graph.Descendants.OfType<NumberEntry>().Single( entry => entry.HasClass( "axis-x-min" ) );
		Assert.IsTrue( float.Parse( minimum.Text, System.Globalization.CultureInfo.InvariantCulture ) < graph.ViewMin.x );
	}

	[TestMethod]
	public void AxisFormatsStayFixedThroughNavigationAndRangeChanges()
	{
		var graph = CreateGraph();
		graph.EditableAxes = true;
		graph.HorizontalAxisFormat = "0";
		graph.VerticalAxisFormat = "0.00";
		var x = graph.Descendants.OfType<NumberEntry>().Single( entry => entry.HasClass( "axis-x-max" ) );
		var y = graph.Descendants.OfType<NumberEntry>().Single( entry => entry.HasClass( "axis-y-max" ) );
		Frame();
		Assert.AreEqual( "8", x.Text );
		Assert.AreEqual( "30.00", y.Text );
		graph.ZoomAt( graph.CanvasToScreen( Vector2.Zero ), 0.5f );
		Frame();
		Assert.AreEqual( "4", x.Text );
		Assert.AreEqual( "15.00", y.Text );
		graph.SetView( new( 622.79f, -3.33521f ), new( 676.293f, 12.7155f ) );
		Frame();
		Assert.AreEqual( "676", x.Text );
		Assert.AreEqual( "12.72", y.Text );
		Assert.AreEqual( "0.00", GraphAxis.FormatLabel( -0.0001, graph.VerticalAxisFormat ) );
	}

	[TestMethod]
	public void RoundedEndpointsRetainTheirFullValueWhenFocused()
	{
		var graph = CreateGraph();
		graph.EditableAxes = true;
		graph.SetView( new( 622.79f, -3.33521f ), new( 676.293f, 12.7155f ) );
		Frame();
		var maximum = graph.Descendants.OfType<NumberEntry>().Single( entry => entry.HasClass( "axis-y-max" ) );
		Assert.AreEqual( "12.7", maximum.Text );
		maximum.Focus();
		Frame();
		Assert.AreEqual( graph.ViewMax.y, float.Parse( maximum.Text, System.Globalization.CultureInfo.InvariantCulture ) );
		graph.Focus();
		Frame();
		Assert.AreEqual( "12.7", maximum.Text );
		Assert.AreEqual( 12.7155f, graph.ViewMax.y );
	}

	[DataTestMethod]
	[DataRow( 1d, 500d, 0.2d )]
	[DataRow( 100d, 500d, 20d )]
	[DataRow( 0.001d, 500d, 0.0002d )]
	public void TickSpacingUsesAxisUnits( double span, double pixels, double expected )
	{
		Assert.AreEqual( expected, GraphAxis.TickStep( span, pixels, 90 ), expected * 0.000001 );
	}
	[TestMethod]
	public void AxisTitlesAndRotationReserveSpaceWithoutChangingDataCoordinates()
	{
		var graph = CreateGraph();
		var before = graph.CanvasToScreen( graph.ViewMin );
		graph.VerticalAxis.Label = "Revenue";
		graph.VerticalAxis.LabelWidth = 90;
		graph.HorizontalAxis.Label = "Month";
		graph.HorizontalAxis.LabelWidth = 90;
		graph.HorizontalAxis.LabelRotation = -45;
		Frame();
		var after = graph.CanvasToScreen( graph.ViewMin );
		Assert.IsTrue( after.x > before.x );
		Assert.IsTrue( after.y < before.y );
		Assert.IsTrue( (graph.ScreenToCanvas( after ) - graph.ViewMin).Length < 0.001f );
		Assert.AreEqual( 2, graph.Children.OfType<GraphAxis>().Count() );
	}

	[TestMethod]
	public void StandaloneAxisSupportsCategoriesCurrencyAndReversal()
	{
		var axis = _surface.Root.AddChild<GraphAxis>();
		axis.Style.Width = 600;
		axis.Style.Height = 100;
		axis.Minimum = 0;
		axis.Maximum = 200000;
		axis.NumberFormat = "$#,0";
		axis.TickInterval = 50000;
		var ticks = axis.GetTicks( 600 ).Where( x => x.Major ).ToArray();
		Assert.AreEqual( 5, ticks.Length );
		Assert.AreEqual( "$50,000", ticks[1].Label );
		Assert.AreEqual( "$200,000", ticks[^1].Label );
		axis.Minimum = 0;
		axis.Maximum = 3;
		axis.Ticks = [new( 0.5, "January" ), new( 1.5, "February" ), new( 2.5, "March" ), new( 3.5, "April" )];
		axis.Label = "Month";
		axis.LabelRotation = -45;
		Assert.AreEqual( 3, axis.GetTicks( 600 ).Count() );
		Assert.AreEqual( "February", axis.GetTicks( 600 ).ElementAt( 1 ).Label );
		Assert.AreEqual( 0.25f, axis.Fraction( 0.75 ), 0.0001f );
		axis.Reversed = true;
		Assert.AreEqual( 0.75f, axis.Fraction( 0.75 ), 0.0001f );
		Frame();
	}

}
