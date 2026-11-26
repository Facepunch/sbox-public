using Sandbox.UI;

namespace EngineTests;

[TestClass]
public class GraphicsAvailabilityTest
{
	[TestMethod]
	public void AvailabilityMatchesInitializedRenderer()
	{
		// Borealis.Gpu drawing over Source 2's empty device (-borealisgpu)
		Assert.AreEqual( RenderBackend.Default is BorealisGpuRenderBackend, Graphics.IsAvailable );
	}

	[TestMethod]
	public void EmptyRendererBuildsPanelSnapshotWithoutGpuBuffers()
	{
		if ( Graphics.IsAvailable )
			Assert.Inconclusive( "Requires the empty renderer with nothing drawing." );

		var root = new RootPanel { RenderedManually = true, PanelBounds = new Rect( 0, 0, 64, 64 ) };
		try
		{
			root.AddChild<Panel>().Style.Set( "width: 32px; height: 32px; background-color: red;" );
			root.Layout();
			var snapshot = PanelDrawSnapshot.Build( root );
			Assert.AreEqual( 1, snapshot.Instances.Length );
			Assert.AreEqual( Color.Red, snapshot.Instances[0].Color );

			var batcher = root.PanelCommandList.FindResource<Painter.Context>().Batcher;
			Assert.IsTrue( batcher.Scissors.Count > 0 );
			batcher.BindScissor( root.PanelCommandList.BeginDrawAttributes(), 0 );
			Assert.AreEqual( 0, batcher.GpuBufferCount );
			Assert.AreEqual( 1, batcher.DrawCalls );
		}
		finally
		{
			root.Delete( true );
		}
	}
}
