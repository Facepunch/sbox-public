using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Environment map probes captured on Borealis.Gpu (<see cref="CubeCapture"/>): the faces land in their slices as the engine
/// turns its cube camera, and the filter fills every mip.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CubeCaptureTests : RenderGpuTest
{
	/// <summary>The smallest cube with the filter's seven mips.</summary>
	const int Size = 64;

	/// <summary>
	/// A lit box in front of the probe (+X, the second face's direction) shows in the middle of slice 1 and not in slice 0,
	/// which looks the other way; every mip of every face is filled from the faces, none left red or black.
	/// </summary>
	[TestMethod]
	public void FacesLandInTheirSlicesAndEveryMipIsFiltered()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var capture = new CubeCapture( backend, Size );
			if ( !capture.CanFilter ) Assert.Inconclusive( "envmap_filtering_cs isn't built" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 1, 0.2f, -0.3f ).Normal;
			world.Lighting.SunColor = new Color( 1, 1, 1 );
			world.Lighting.AmbientColor = new Color( 0.5f, 0.5f, 0.5f, 1 );
			world.Lighting.SunShadowCascades = 0;
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Cube capture box", Color.White ) ), new Transform( new Vector3( 300, 0, 0 ) ) ) );

			using var cube = CubeCapture.CreateCube( backend, Size, "Cube capture test" );
			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			capture.Render( world, Vector3.Zero, Rotation.Identity, 16, 4096, cube );
			Device.WaitIdle();

			var handle = BackendDevice.Unwrap( cube ).Texture;
			var front = Read( handle, 0, 1 );
			var back = Read( handle, 0, 0 );
			var middle = (Size / 2 * Size + Size / 2) * 4;
			Assert.IsTrue( front[middle] > 0.05f, $"the box shows in slice 1 ({front[middle]})" );
			Assert.IsTrue( back[middle] < front[middle] * 0.1f, $"slice 0 looks away from it ({back[middle]})" );

			for ( int mip = 1; mip < CubeCapture.Mips; mip++ )
			{
				for ( int face = 0; face < 6; face++ )
				{
					var texels = Read( handle, mip, face );
					var red = texels.Where( ( _, i ) => i % 4 == 0 ).Average();
					var green = texels.Where( ( _, i ) => i % 4 == 1 ).Average();
					Assert.IsTrue( red < 0.9f || green > 0.01f, $"mip {mip} of face {face} isn't the red clear" );
					Assert.IsTrue( texels.All( float.IsFinite ), $"mip {mip} of face {face} is finite" );
					Assert.AreEqual( 1f, texels[3], $"mip {mip} of face {face} was written by the filter (alpha 1)" );
				}
			}

			// The box's light reaches the filtered mips on its side
			var roughFront = Read( handle, 3, 1 );
			var roughBack = Read( handle, 3, 0 );
			Assert.IsTrue( roughFront.Average() > roughBack.Average(), "the box brightens slice 1's rough mips" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Faces the caller renders (the engine's cube cameras, through the bridge) are asked for once each in slice order and
	/// land in their slices; without a filter the rough mips stay as the cube was made (black), and with the quality filter
	/// every mip is written.
	/// </summary>
	[TestMethod]
	public void CallerFacesLandInTheirSlicesAndTheFilterIsTheCallers()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var capture = new CubeCapture( backend, Size );
			if ( !capture.CanFilter ) Assert.Inconclusive( "envmap_filtering_cs isn't built" );

			// Each face cleared to its own grey, (i + 1) / 8 (linear once cleared)
			var world = new RenderWorld();
			using var system = new RenderSystem { ParallelRecording = false };
			var view = new RenderView { Name = "Caller face", FieldOfView = 90, Capture = true, Viewport = new Rect( 0, 0, Size, Size ) };
			var asked = new List<int>();
			var expected = new float[6];
			void Face( int i, ViewTarget target )
			{
				asked.Add( i );
				var grey = (i + 1) / 8f;
				view.ClearColor = new Color( grey, grey, grey, 1 );
				expected[i] = grey <= 0.04045f ? grey / 12.92f : MathF.Pow( (grey + 0.055f) / 1.055f, 2.4f );
				system.Render( world, view, target );
			}

			using var unfiltered = CubeCapture.CreateCube( backend, Size, "Caller faces unfiltered" );
			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			capture.Render( unfiltered, Face, null );
			Device.WaitIdle();

			CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5 }, asked, "each face asked for once, in slice order" );
			var handle = BackendDevice.Unwrap( unfiltered ).Texture;
			for ( int face = 0; face < 6; face++ )
			{
				var texels = Read( handle, 0, face );
				Assert.AreEqual( expected[face], texels[0], 0.01f, $"slice {face} has its face" );
			}

			Assert.AreEqual( 0f, Read( handle, 1, 0 )[3], "without a filter mip 1 is as the cube was made" );

			asked.Clear();
			using var filtered = CubeCapture.CreateCube( backend, Size, "Caller faces filtered" );
			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			capture.Render( filtered, Face, 1 );
			Device.WaitIdle();

			handle = BackendDevice.Unwrap( filtered ).Texture;
			for ( int mip = 1; mip < CubeCapture.Mips; mip++ )
			{
				var texels = Read( handle, mip, 2 );
				Assert.AreEqual( 1f, texels[3], $"mip {mip} was written by the quality filter" );
				Assert.IsTrue( texels.All( float.IsFinite ), $"mip {mip} is finite" );
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A capture camera's stages (the DDGI bake's hook) run after each face, in slice order, once that face is in its slice
	/// and before the next is drawn, with the target the face drew into: the probe's integration after the sixth reads the
	/// whole cube.
	/// </summary>
	[TestMethod]
	public void AfterFaceSeesItsFaceInTheCube()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var capture = new CubeCapture( backend, Size );
			var world = new RenderWorld();
			using var system = new RenderSystem { ParallelRecording = false };
			var view = new RenderView { Name = "Caller face", FieldOfView = 90, Capture = true, Viewport = new Rect( 0, 0, Size, Size ) };
			var drawn = new ViewTarget[6];
			void Face( int i, ViewTarget target )
			{
				drawn[i] = target;
				var grey = (i + 1) / 8f;
				view.ClearColor = new Color( grey, grey, grey, 1 );
				system.Render( world, view, target );
			}

			using var cube = CubeCapture.CreateCube( backend, Size, "After face" );
			var handle = BackendDevice.Unwrap( cube ).Texture;
			var after = new List<int>();
			void After( int i, ViewTarget target )
			{
				after.Add( i );
				Assert.AreSame( drawn[i], target, $"face {i}'s stages get the target it drew into" );
				Device.WaitIdle();
				var grey = (i + 1) / 8f;
				var expected = grey <= 0.04045f ? grey / 12.92f : MathF.Pow( (grey + 0.055f) / 1.055f, 2.4f );
				Assert.AreEqual( expected, Read( handle, 0, i )[0], 0.01f, $"face {i} is in its slice when its stages run" );
				if ( i < 5 ) Assert.AreEqual( 1f, Read( handle, 0, i + 1 )[0], 0.01f, $"face {i + 1} isn't drawn yet (red)" );
			}

			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			capture.Render( cube, Face, null, After );
			Device.WaitIdle();

			CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5 }, after, "after each face, in slice order" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>A cube's mip and face as floats, RGBA.</summary>
	float[] Read( TextureHandle texture, int mip, int face )
	{
		var size = Math.Max( Size >> mip, 1 );
		var buffer = Device.CreateBuffer( new BufferInfo { Size = size * size * 8, Memory = MemoryLocation.Readback, Name = "Cube readback" } );
		var list = Device.BeginCommandList( QueueType.Graphics, "Cube readback" );
		list.Barrier( new TextureBarrier { Texture = texture, OldLayout = ImageLayout.ShaderRead, NewLayout = ImageLayout.CopySource, SourceStage = PipelineStage.AllCommands, SourceAccess = MemoryAccess.MemoryWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead, BaseMip = mip, MipCount = 1, BaseLayer = face, LayerCount = 1 } );
		list.CopyTextureToBuffer( texture, mip, face, buffer );
		list.Barrier( new TextureBarrier { Texture = texture, OldLayout = ImageLayout.CopySource, NewLayout = ImageLayout.ShaderRead, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferRead, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.ShaderRead, BaseMip = mip, MipCount = 1, BaseLayer = face, LayerCount = 1 } );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );

		var bytes = Device.Map( buffer ).ToArray();
		Device.Destroy( buffer );
		var floats = new float[size * size * 4];
		for ( int i = 0; i < floats.Length; i++ ) floats[i] = (float)BitConverter.ToHalf( bytes, i * 2 );
		return floats;
	}
}
