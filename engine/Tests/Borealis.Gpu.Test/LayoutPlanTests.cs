using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Layout plans (<see cref="BackendDevice.LayoutPlans"/>): segments recorded into lists of their own start from where the
/// lists before leave their textures, learnt from the last frame of the same shape, and need no fix-up lists.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LayoutPlanTests : GpuTest
{
	const int Width = 64, Height = 32;

	/// <summary>
	/// Two segments in lists of their own: the first clears a target, the second makes it readable. Without a plan each
	/// frame needs a fix-up list per segment; with one, the first frame learns and the frames after need none, and the
	/// target holds the clear either way.
	/// </summary>
	[TestMethod]
	public void PlannedSegmentsNeedNoFixUps()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var color = backend.CreateTexture( TextureDesc.Target( Width, Height, TextureFormat.RGBA16161616F ), "Target" );
			using var frame = new BackendContext( backend, null );
			using var clearing = new BackendContext( backend, frame );
			using var reading = new BackendContext( backend, frame );

			int Frame( bool plans, float red )
			{
				backend.LayoutPlans = plans;
				Device.BeginFrame();
				var before = BackendContext.LayoutFixUps;
				frame.Begin();

				clearing.BeginSegment( frame, null );
				clearing.PlanLayouts( 1 );
				clearing.Bind( new StageTarget
				{
					Context = clearing,
					Attributes = clearing.Attributes,
					Color = color,
					Size = new Vector2( Width, Height ),
					Viewport = new Rect( 0, 0, Width, Height ),
					MaxZ = 1,
					ColorFormat = TextureFormat.RGBA16161616F,
					Samples = 1,
				} );
				clearing.Clear( new Color( red, 0.5f, 0.25f, 1 ) );

				reading.BeginSegment( frame, null );
				reading.PlanLayouts( 2 );
				reading.BarrierToPixelShaderRead( color );

				frame.Submit();
				clearing.Submit();
				reading.Submit();
				Device.WaitIdle();
				return BackendContext.LayoutFixUps - before;
			}

			Frame( plans: false, 0.1f );
			var unplanned = Frame( plans: false, 0.2f );
			var learning = Frame( plans: true, 0.3f );
			var planned = Frame( plans: true, 0.4f );
			var again = Frame( plans: true, 1 );

			Assert.AreEqual( 2, unplanned, "without a plan, each segment's list needs its layouts fixed up" );
			Assert.AreEqual( 2, learning, "the first planned frame learns the plan" );
			Assert.AreEqual( 0, planned, "a planned frame needs no fix-up lists" );
			Assert.AreEqual( 0, again, "nor does the next" );

			var (handle, current) = BackendDevice.Unwrap( color );
			Assert.AreEqual( ImageLayout.ShaderRead, current, "the second segment left the target readable" );
			var texels = ReadBack( handle, current );
			Assert.AreEqual( 1f, (float)BitConverter.ToHalf( texels, 0 ), "the last frame's clear" );
			Assert.AreEqual( 0.5f, (float)BitConverter.ToHalf( texels, 2 ) );
			BackendDevice.SetLayout( color, ImageLayout.CopySource );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
