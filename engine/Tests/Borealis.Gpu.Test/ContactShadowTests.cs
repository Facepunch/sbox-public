using Borealis;
using Borealis.Backend;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// The sun's contact shadows on Borealis.Gpu (<c>screen_space_shadows_cs</c> marching the depth chain into the mask the sun
/// reads), for a game camera's frame: one with stages.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ContactShadowTests : GpuTest
{
	const int Width = 256, Height = 256;

	sealed class CameraStages : IRenderStages
	{
		public void Render( RenderStage stage, in StageTarget target ) { }
	}

	/// <summary>
	/// A box on a floor, lit by the sun, the box casting nothing into the shadow maps: with contact shadows the floor beside
	/// the box, away from the sun, is darker than without.
	/// </summary>
	[TestMethod]
	public void DarkenTheFloorBesideABox()
	{
		var with = Render( true );
		var without = Render( false );
		long darker = 0;
		for ( int i = 0; i < with.Length; i += 4 )
		{
			if ( with[i] + with[i + 1] + with[i + 2] + 24 < without[i] + without[i + 1] + without[i + 2] ) darker++;
		}

		Assert.IsTrue( darker > Width * Height / 200, $"{darker} pixels darker with contact shadows" );
	}

	/// <summary>
	/// The same frame recorded in parallel with the depth chain and contact shadows on the async compute queue, beside the
	/// sun's shadow maps on graphics, draws the same pixels as with every layer on graphics, and validation (synchronisation
	/// included) finds nothing wrong with the handoffs between the queues.
	/// </summary>
	[TestMethod]
	public void AsyncComputeDrawsTheSameFrame()
	{
		if ( !Device.Capabilities.AsyncCompute ) Assert.Inconclusive( "No async compute queue" );

		var serial = Render( true, parallel: true, asyncCompute: false, out var overlappedSerial );
		var overlapped = Render( true, parallel: true, asyncCompute: true, out var overlappedAsync );
		Assert.IsFalse( overlappedSerial, "with async compute off, nothing goes on the compute queue" );
		Assert.IsTrue( overlappedAsync, "the frame put its compute layers on the async compute queue" );

		// The contact shadows' march differs by a few pixels from one run to the next on its own (about 70 of these 65,536 on an
		// RX 9070 XT, async compute or not): the same frame is the same within that
		var again = Render( true, parallel: true, asyncCompute: false, out _ );
		var noise = Differing( serial, again );
		var differing = Differing( serial, overlapped );
		Assert.IsTrue( differing <= Math.Max( 3 * noise, 200 ), $"{differing} pixels differ with async compute, {noise} between two runs without" );
	}

	static int Differing( byte[] a, byte[] b )
	{
		var count = 0;
		for ( int i = 0; i < a.Length; i += 4 )
		{
			if ( a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] ) count++;
		}

		return count;
	}

	byte[] Render( bool contactShadows ) => Render( contactShadows, parallel: false, asyncCompute: false, out _ );

	byte[] Render( bool contactShadows, bool parallel, bool asyncCompute, out bool overlapped )
	{
		using var backend = new BackendDevice( Device ) { AsyncCompute = asyncCompute };
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			Assert.IsTrue( RenderHost.Current.ContactShadows, "Borealis.Gpu draws contact shadows" );
			using var system = new RenderSystem { ParallelRecording = parallel };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Contact shadow test" );
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( -1, 0.2f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 4, 4, 4 );
			world.Lighting.AmbientColor = new Color( 0.05f, 0.05f, 0.05f, 1 );
			world.Lighting.SunContactShadows = contactShadows;
			var material = backend.CreateMaterial( "Contact shadow grey", Color.Gray );
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, material ), Transform.Zero ) { CastShadows = false } );

			// A second box, which casts into the sun's shadow maps: graphics work for the compute queue to overlap
			if ( parallel ) world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, material ), new Transform( new Vector3( 0, 120, 0 ), Rotation.Identity, 1 ) ) );
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, material ), new Transform( new Vector3( 0, 0, -52.5f ), Rotation.Identity, new Vector3( 20, 20, 0.05f ) ) ) );

			var view = new RenderView { Name = "Contact shadow test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Rotation = Rotation.From( 35, 90, 0 );
			view.Position = -view.Rotation.Forward * 450;

			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target, new CameraStages() );
			overlapped = system.Frame.AsyncCompute;
			Device.WaitIdle();

			var (texture, layout) = BackendDevice.Unwrap( target.Color );
			var halfs = ReadBack( texture, layout );
			var rgba = new byte[Width * Height * 4];
			for ( int i = 0; i < rgba.Length; i++ ) rgba[i] = (byte)Math.Clamp( (float)BitConverter.ToHalf( halfs, i * 2 ) * 255, 0, 255 );
			Save( rgba, Width, Height, $"contact-shadows-{(contactShadows ? "on" : "off")}{(parallel ? asyncCompute ? "-async" : "-parallel" : "")}" );
			return rgba;
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
