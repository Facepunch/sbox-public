using Basalt;
using Basalt.Gpu;
using BasaltTests.Headless;
using System.Collections.Generic;
using System.Reflection;
using static BasaltTests.TestScenes;

namespace BasaltTests;

/// <summary>
/// What whole frames submit, read from the headless device's log: the order passes draw in, and how work crosses between
/// the graphics and async compute queues.
/// </summary>
// Some turn the device's async compute queue on, which every render system reads
[TestClass]
[DoNotParallelize]
public class FrameLogTests
{
	static HeadlessDevice Device => HeadlessRenderHost.Headless;

	static readonly FieldInfo Layers = typeof( RenderSystem ).GetField( "layers", BindingFlags.Instance | BindingFlags.NonPublic );

	/// <summary>
	/// A row of boxes that can draw, all of one opaque material.
	/// </summary>
	static RenderWorld Boxes( GpuMaterial material, int count )
	{
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );
		var world = new RenderWorld();
		for ( int i = 0; i < count; i++ )
			world.Add( new MeshObject( mesh, new Transform( new Vector3( 30, i * 3 - count, 0 ) ) ) );
		return world;
	}

	static FrameCapture Render( RenderSystem system, RenderWorld world, ViewTarget target )
	{
		using var frame = Device.Capture();
		RenderHost.Current.NextFrame();
		system.Render( world, LookingDownX(), target );
		return frame;
	}

	/// <summary>
	/// The prepass lays down depth for every opaque mesh before forward shading draws any, against that same depth. Without
	/// it, forward draws everything itself.
	/// </summary>
	[TestMethod]
	public void DepthPrepassDrawsBeforeForward()
	{
		using var material = new HeadlessMaterial( "box", "complex" );
		var world = Boxes( material, 3 );
		using var target = ViewTarget.Create( new Vector2Int( 640, 480 ) );
		using var system = new RenderSystem();

		var commands = Render( system, world, target ).Commands.ToList();
		var prepass = commands.Where( c => c.IsDraw && c.In( "Depth Prepass" ) ).ToList();
		var forward = commands.Where( c => c.IsDraw && c.In( "Opaque Forward" ) ).ToList();

		Assert.AreEqual( 3, prepass.Sum( c => c.Count ), "every box's depth" );
		Assert.AreNotEqual( 0, forward.Count );
		Assert.IsTrue( commands.IndexOf( prepass[^1] ) < commands.IndexOf( forward[0] ), "depth is laid down before anything shades" );
		Assert.IsTrue( prepass.All( c => c.Texture is null && c.Depth == target.Depth ), "the prepass writes only depth" );
		Assert.IsTrue( forward.All( c => c.Texture == target.Color && c.Depth == target.Depth ), "forward tests against the prepass's depth" );
		Assert.IsTrue( prepass.Concat( forward ).All( c => c.Material == material ) );

		system.DepthPrepass = false;
		commands = Render( system, world, target ).Commands.ToList();
		Assert.IsFalse( commands.Any( c => c.IsDraw && c.In( "Depth Prepass" ) ) );
		Assert.AreEqual( 3, commands.Where( c => c.Op == GpuOp.Draw && c.In( "Opaque Forward" ) ).Sum( c => c.Count ), "forward draws every box itself" );
	}

	/// <summary>
	/// With an async compute queue, the depth chain runs on it: graphics makes depth readable by compute and signals, the
	/// compute queue waits for that, and a later graphics submission waits for the compute queue before the frame ends.
	/// Without one, the same work stays on graphics and nothing waits on anything.
	/// </summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void AsyncComputeIsHandedDepthAndJoinedBack( bool asyncCompute )
	{
		using var material = new HeadlessMaterial( "box", "complex" );
		var world = Boxes( material, 3 );
		using var target = ViewTarget.Create( new Vector2Int( 640, 480 ) );
		using var system = new RenderSystem();
		using var _ = Device.WithAsyncCompute( asyncCompute );

		var submissions = Render( system, world, target ).Submissions;
		var hiZ = submissions.Single( s => s.Commands.Any( c => c.Op == GpuOp.Dispatch && c.In( "Hi-Z Depth Downsample" ) ) );
		var compute = submissions.Where( s => s.AsyncCompute ).ToList();

		if ( !asyncCompute )
		{
			Assert.AreEqual( 0, compute.Count );
			Assert.IsFalse( hiZ.AsyncCompute );
			Assert.IsTrue( submissions.All( s => s.Wait == default && s.Signal == default ), "one queue needs no semaphores" );
			return;
		}

		Assert.IsTrue( hiZ.AsyncCompute, "the depth chain runs on the compute queue" );
		foreach ( var batch in compute )
		{
			var index = IndexOf( submissions, batch );
			Assert.IsFalse( batch.Commands.Any( c => c.IsDraw || c.Op is GpuOp.Bind or GpuOp.Clear ), "compute queues dispatch and barrier, nothing else" );

			// Handed over: the graphics submission it waits for leaves depth readable by compute
			var handOff = submissions.Single( s => s.Signal == batch.Wait );
			Assert.IsTrue( IndexOf( submissions, handOff ) < index );
			Assert.IsFalse( handOff.AsyncCompute );
			var barrier = handOff.Commands.Last( c => c.Op == GpuOp.TextureBarrier && c.Texture == target.Depth );
			Assert.AreEqual( GpuStage.LateFragmentTests, barrier.SrcStage );
			Assert.AreEqual( GpuAccess.DepthStencilAttachmentWrite, barrier.SrcAccess );
			Assert.AreEqual( GpuStage.ComputeShader, barrier.DstStage );
			Assert.AreEqual( GpuAccess.ShaderRead, barrier.DstAccess );
			Assert.AreEqual( TextureLayout.ShaderRead, barrier.Layout );

			// Taken back: graphics waits for it later in the frame
			var joined = submissions.Single( s => s.Wait == batch.Signal );
			Assert.IsTrue( IndexOf( submissions, joined ) > index );
			Assert.IsFalse( joined.AsyncCompute );
		}
	}

	static int IndexOf( IReadOnlyList<GpuSubmission> submissions, GpuSubmission submission )
	{
		for ( int i = 0; i < submissions.Count; i++ )
			if ( ReferenceEquals( submissions[i], submission ) ) return i;
		return -1;
	}

	sealed class Pass( string name, GpuMaterial shader, FrameResources reads = FrameResources.None, FrameResources writes = FrameResources.None, bool compute = false ) : RenderLayer( name )
	{
		public override FrameResources Reads( RenderFrame frame ) => reads;
		public override FrameResources Writes => writes;
		public override bool SupportsAsyncCompute => compute;
		public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats ) => rc.Dispatch( shader, 1, 1, 1 );
	}

	/// <summary>
	/// A pass that only makes something for later passes records nothing when no later pass reads it. When one does, it
	/// records first - on the compute queue, with an async one, the reader's submission waiting for it.
	/// </summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void ProducersRecordOnlyForTheirReaders( bool read, bool asyncCompute )
	{
		using var produce = new HeadlessMaterial( "produce", "produce_cs" );
		using var draw = new HeadlessMaterial( "draw", "draw" );
		using var consume = new HeadlessMaterial( "consume", "consume_cs" );
		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		using var _ = Device.WithAsyncCompute( asyncCompute );

		var layers = new List<RenderLayer>
		{
			new Pass( "producer", produce, writes: FrameResources.QuarterDepth, compute: true ),
			new Pass( "graphics", draw ),
		};
		if ( read ) layers.Add( new Pass( "reader", consume, reads: FrameResources.QuarterDepth ) );
		Layers.SetValue( system, layers.ToArray() );

		var frame = Render( system, new RenderWorld(), target );
		var produced = frame.Submissions.Where( s => s.Commands.Any( c => c.Material == produce ) ).ToList();

		if ( !read )
		{
			Assert.AreEqual( 0, produced.Count, "nothing reads it" );
			Assert.IsTrue( frame.Commands.Any( c => c.Material == draw ), "the rest of the frame still records" );
			Assert.IsFalse( frame.Submissions.Any( s => s.AsyncCompute ) );
			return;
		}

		var producer = produced.Single();
		var reader = frame.Submissions.Single( s => s.Commands.Any( c => c.Material == consume ) );
		Assert.IsTrue( IndexOf( frame.Submissions, producer ) < IndexOf( frame.Submissions, reader ) || producer == reader );
		Assert.AreEqual( asyncCompute, producer.AsyncCompute );
		if ( !asyncCompute )
		{
			var commands = frame.Commands.ToList();
			Assert.IsTrue( commands.FindIndex( c => c.Material == produce ) < commands.FindIndex( c => c.Material == consume ) );
			return;
		}

		Assert.AreNotEqual( default, producer.Signal );
		Assert.AreEqual( producer.Signal, reader.Wait, "the reader waits for what it reads" );
		Assert.IsFalse( reader.AsyncCompute );
	}

	/// <summary>
	/// A camera's attributes reach the frame's parameters each frame, and one the camera stops setting is gone the next frame,
	/// as a native view's attributes are made again every frame.
	/// </summary>
	[TestMethod]
	public void CameraAttributesDontOutliveTheCamerasFrame()
	{
		var tint = new StringToken( "TestCameraTint" );
		var mode = new StringToken( "TestCameraMode" );
		var camera = new HeadlessParameters();
		camera.Set( tint, 0.5f );
		camera.Set( mode, 3 );

		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		var world = new RenderWorld();
		var parameters = (HeadlessParameters)system.FrameContext.Attributes;

		void RenderFrame()
		{
			var view = LookingDownX();
			view.CameraAttributes = camera;
			using var _ = Device.Capture();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
		}

		RenderFrame();
		Assert.AreEqual( 0.5f, parameters.Get( tint ) );
		Assert.AreEqual( 3, parameters.Get( mode ) );

		camera.Clear();
		camera.Set( mode, 4 );
		RenderFrame();
		Assert.IsNull( parameters.Get( tint ), "the camera stopped setting it" );
		Assert.AreEqual( 4, parameters.Get( mode ) );
	}
}
