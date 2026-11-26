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
public class LayoutPlanTests : RenderGpuTest
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
			Assert.AreEqual( 1, learning, "the first planned frame learns the plan: the first segment's target starts where the last frame left it, the second's needs moving from where the first left it" );
			Assert.AreEqual( 0, planned, "a planned frame needs no fix-up lists" );
			Assert.AreEqual( 0, again, "nor does the next" );

			// The reading segment recording into the clearing one's list, as a main-thread segment after another does
			// (FrameRecorder.PlanSharedLists): one list, which a planned frame submits without fix-ups
			int Shared( float red )
			{
				Device.BeginFrame();
				var before = BackendContext.LayoutFixUps;
				frame.Begin();
				clearing.BeginSegment( frame, null );
				clearing.PlanLayouts( 3 );
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

				reading.BeginSegment( frame, clearing );
				reading.PlanLayouts( 4 );
				reading.BarrierToPixelShaderRead( color );

				frame.Submit();
				clearing.Submit();
				reading.Submit();
				Device.WaitIdle();
				return BackendContext.LayoutFixUps - before;
			}

			Assert.IsTrue( Shared( 0.6f ) <= 1, "a shared list needs at most its own fix-ups while it learns" );
			Assert.AreEqual( 0, Shared( 0.7f ), "a planned shared list needs none" );
			Assert.AreEqual( 0, Shared( 1 ), "nor the next" );

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

	/// <summary>
	/// A segment that draws one of two targets in turn, as a point light's shadow segment draws whichever maps are due this
	/// frame, then a segment that reads it: the plan names last frame's target, not this one, which then starts where it was
	/// last left (readable, two frames ago) rather than needing a fix-up list each frame. Each frame's target holds its clear.
	/// </summary>
	[TestMethod]
	public void TargetsDrawnInTurnNeedNoFixUps()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var first = backend.CreateTexture( TextureDesc.Target( Width, Height, TextureFormat.RGBA16161616F ), "First" );
			using var second = backend.CreateTexture( TextureDesc.Target( Width, Height, TextureFormat.RGBA16161616F ), "Second" );
			using var frame = new BackendContext( backend, null );
			using var drawing = new BackendContext( backend, frame );
			using var reading = new BackendContext( backend, frame );
			backend.LayoutPlans = true;

			int Frame( GpuTexture target, float red )
			{
				Device.BeginFrame();
				var before = BackendContext.LayoutFixUps;
				frame.Begin();

				drawing.BeginSegment( frame, null );
				drawing.PlanLayouts( 1 );
				drawing.Bind( new StageTarget
				{
					Context = drawing,
					Attributes = drawing.Attributes,
					Color = target,
					Size = new Vector2( Width, Height ),
					Viewport = new Rect( 0, 0, Width, Height ),
					MaxZ = 1,
					ColorFormat = TextureFormat.RGBA16161616F,
					Samples = 1,
				} );
				drawing.Clear( new Color( red, 0.5f, 0.25f, 1 ) );
				drawing.BarrierToPixelShaderRead( target );

				reading.BeginSegment( frame, null );
				reading.PlanLayouts( 2 );
				reading.BarrierToPixelShaderRead( target );

				frame.Submit();
				drawing.Submit();
				reading.Submit();
				Device.WaitIdle();
				return BackendContext.LayoutFixUps - before;
			}

			// Each target's first frames learn
			Frame( first, 0.1f );
			Frame( second, 0.2f );
			Frame( first, 0.3f );

			var fixUps = 0;
			for ( int i = 0; i < 6; i++ ) fixUps += Frame( i % 2 == 0 ? second : first, 0.4f + i * 0.1f );
			Assert.AreEqual( 0, fixUps, "a target the plan doesn't name starts where it was last left" );

			foreach ( var (target, red) in new[] { (second, 0.8f), (first, 0.9f) } )
			{
				var (handle, current) = BackendDevice.Unwrap( target );
				Assert.AreEqual( ImageLayout.ShaderRead, current );
				var texels = ReadBack( handle, current );
				Assert.AreEqual( red, (float)BitConverter.ToHalf( texels, 0 ), 0.01f, "its last frame's clear" );
				BackendDevice.SetLayout( target, ImageLayout.CopySource );
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
