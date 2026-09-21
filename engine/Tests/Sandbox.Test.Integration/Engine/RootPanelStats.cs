using Sandbox.Rendering;
using Sandbox.UI;
using System;

namespace EngineTests;

[TestClass]
public class RootPanelStatsTest
{
	[TestMethod]
	public void RootSnapshotsAreIndependent()
	{
		RequireVulkan();
		var system = new UISystem();
		var first = new RootPanel( system );
		var second = new RootPanel( system );
		var child = AddBox( first );
		AddBox( first );
		AddBox( second );
		var hidden = AddBox( first );
		hidden.Style.Display = DisplayMode.None;
		AddBox( hidden );
		var overlay = AddBox( first );
		overlay.Style.Position = PositionMode.Fixed;

		try
		{
			Prepare( first );
			Prepare( second );
			Assert.AreEqual( default, first.Stats );
			Assert.AreEqual( default, second.Stats );

			system.CombineCommandLists();
			second.PanelCommandList.Enabled = false;
			Render( () => system.Render() );
			var snapshot = first.Stats;
			Assert.AreEqual( 1L, snapshot.RenderCount );
			Assert.AreEqual( Application.FrameCount, snapshot.FrameNumber );
			Assert.AreEqual( 4, snapshot.PanelsRendered, "Includes batched children and the fixed overlay once." );
			Assert.AreEqual( 1, snapshot.PanelsCulled, "A hidden subtree is counted once, without walking its descendants." );
			Assert.AreEqual( 3, snapshot.Instances );
			Assert.IsTrue( snapshot.DrawCalls > 0 );
			Assert.IsTrue( snapshot.LayoutTime > TimeSpan.Zero );
			Assert.IsTrue( snapshot.CommandBuildTime > TimeSpan.Zero );
			Assert.IsTrue( snapshot.SubmissionTime > TimeSpan.Zero );
			Assert.AreEqual( default, second.Stats, "An inserted, disabled list must not count as a render." );

			second.PanelCommandList.Enabled = true;
			Render( () => second.Render() );
			Assert.AreEqual( 2, second.Stats.PanelsRendered );
			Assert.AreEqual( 1, second.Stats.Instances );
			Assert.AreEqual( snapshot, first.Stats );

			first.BuildCommandList();
			Assert.AreEqual( snapshot, first.Stats, "Preparing another render must not publish an unfinished snapshot." );
			Render( () => first.Render() );
			Assert.AreEqual( 2L, first.Stats.RenderCount );
			Assert.AreEqual( 4, first.Stats.PanelsRendered );

			first.BuildCommandList();
			Render( () => first.Render() );
			Assert.AreEqual( 4, first.Stats.PanelsRendered );
			Assert.AreEqual( 1L, snapshot.RenderCount, "Previously returned snapshots are immutable." );
		}
		finally
		{
			system.Clear();
		}
	}

	[TestMethod]
	public void ReplaysIncrementRenderCount()
	{
		RequireVulkan();
		var system = new UISystem();
		var root = new RootPanel( system ) { RenderedManually = true };
		AddBox( root );
		try
		{
			Prepare( root );
			system.CombineCommandLists();
			Render( () => system.Render() );
			Assert.AreEqual( 0L, root.Stats.RenderCount );

			Render( () => root.RenderManual() );
			var first = root.Stats;
			Assert.AreEqual( 1L, first.RenderCount );
			Render( () => root.Render() );
			Assert.AreEqual( 2L, root.Stats.RenderCount );
			Assert.AreEqual( first.CommandBuildTime, root.Stats.CommandBuildTime );
			Assert.AreEqual( first.PanelsRendered, root.Stats.PanelsRendered );

			root.PanelCommandList.Enabled = false;
			var last = root.Stats;
			Render( () => root.Render() );
			Assert.AreEqual( last, root.Stats );
		}
		finally
		{
			system.Clear();
		}
	}

	[TestMethod]
	public void StatisticsCountCompletedExecutions()
	{
		var system = new UISystem();
		var root = new RootPanel( system );
		var list = new CommandList();
		try
		{
			for ( int i = 0; i < 2; i++ )
			{
				list.Reset();
				root.BeginRenderStatistics( list );
				root.EndRenderStatistics( list, default, TimeSpan.Zero );
				list.Execute();
				Assert.AreEqual( i + 1L, root.Stats.RenderCount );
			}
		}
		finally
		{
			list.Reset();
			system.Clear();
		}
	}

	static Panel AddBox( Panel parent )
	{
		var child = parent.AddChild<Panel>();
		child.Style.Set( "position: absolute; left: 8px; top: 8px; width: 16px; height: 16px; background-color: red;" );
		return child;
	}

	static void Prepare( RootPanel root )
	{
		root.PanelBounds = new Rect( 0, 0, 64, 64 );
		root.Layout();

		root.BuildCommandList();
	}

	static void RequireVulkan()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires the Vulkan renderer." );
	}

	static void Render( Action draw )
	{
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using var scope = Graphics.Scope.Create();
		scope.Attributes.Set( "UIGammaOutput", true );
		scope.Context.BindRenderTargets( target.native );
		scope.Context.SetViewport( new Rect( 0, 0, 64, 64 ) );
		draw();
	}
}
