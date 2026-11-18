using Borealis.Gpu;
using BorealisGpuSample;

namespace BorealisGpuTests;

/// <summary>
/// Graphics pipelines linked from parts (VK_EXT_graphics_pipeline_library): parts shared by what they read, the fast link
/// replaced by an optimised one in the same handle, parts going with their shaders, and the same pictures as pipelines
/// made whole.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PipelineLibraryTests : GpuTest
{
	const int Width = 320, Height = 200;

	void RequireLibraries()
	{
		if ( !Device.Capabilities.PipelineLibraries ) Assert.Inconclusive( "The device has no fast-linking graphics pipeline libraries" );
	}

	ShaderHandle Shader( string name ) => Device.CreateShader( Load( name ), name );

	static byte[] Load( string name )
	{
		using var stream = typeof( SampleScene ).Assembly.GetManifestResourceStream( name );
		var bytes = new byte[stream.Length];
		stream.ReadExactly( bytes );
		return bytes;
	}

	static GraphicsPipelineInfo Triangle( ShaderHandle vs, ShaderHandle ps ) => new()
	{
		VertexShader = vs,
		VertexEntry = "MainVs",
		FragmentShader = ps,
		FragmentEntry = "MainPs",
		Topology = PrimitiveTopology.TriangleList,
		ColorFormats = [Format.RGBA8Unorm],
		DepthFormat = Format.D32Float,
		Samples = 1,
		Name = "Triangle",
	};

	/// <summary>
	/// Pipelines that differ only in dynamic state share all four parts; another blend makes only another output part; a
	/// pipeline without a pixel shader has its own fragment part.
	/// </summary>
	[TestMethod]
	public void PartsAreSharedByWhatTheyRead()
	{
		RequireLibraries();
		var vs = Shader( "triangle.vs.spv" );
		var ps = Shader( "triangle.ps.spv" );

		var first = Device.CreateGraphicsPipeline( Triangle( vs, ps ) );
		Assert.AreEqual( 4, Device.PartCount, "one of each part" );

		var culled = Triangle( vs, ps );
		culled.CullMode = CullMode.Back;
		culled.DepthTest = culled.DepthWrite = true;
		culled.DepthCompare = CompareFunction.GreaterEqual;
		var second = Device.CreateGraphicsPipeline( culled );
		Assert.AreEqual( 4, Device.PartCount, "culling and depth state are dynamic: the same parts" );

		var blended = Triangle( vs, ps );
		blended.Blend = [BlendState.AlphaBlend];
		var third = Device.CreateGraphicsPipeline( blended );
		Assert.AreEqual( 5, Device.PartCount, "another output part" );

		var depthOnly = Triangle( vs, default );
		depthOnly.ColorFormats = [];
		var fourth = Device.CreateGraphicsPipeline( depthOnly );
		Assert.AreEqual( 7, Device.PartCount, "a fragment part with no shader, and an output part with no colour" );

		var stats = Device.PipelineLibraryCreation;
		Assert.AreEqual( 7, stats.Parts );
		Assert.AreEqual( 4, stats.Links );

		Assert.IsTrue( Device.WaitForOptimisedPipelines( TimeSpan.FromSeconds( 60 ) ) );
		foreach ( var pipeline in (ReadOnlySpan<PipelineHandle>)[first, second, third, fourth] )
		{
			Assert.IsTrue( Device.IsOptimised( pipeline ) );
			Device.Destroy( pipeline );
		}

		Device.Destroy( vs );
		Device.Destroy( ps );
		Assert.AreEqual( 5, Device.PartCount, "the shader parts went with their shaders: vertex input, three outputs and the empty fragment part stay" );
	}

	/// <summary>
	/// Parts made ahead are what creating the pipeline then links: it makes none of its own.
	/// </summary>
	[TestMethod]
	public void PreparedPartsMakeCreationALink()
	{
		RequireLibraries();
		var vs = Shader( "triangle.vs.spv" );
		var ps = Shader( "triangle.ps.spv" );

		Device.PreparePipelineParts( Triangle( vs, ps ), PipelineParts.PreRasterization | PipelineParts.FragmentShader );
		Assert.AreEqual( 2, Device.PipelineLibraryCreation.Parts );
		Device.PreparePipelineParts( Triangle( vs, ps ) );
		Assert.AreEqual( 4, Device.PipelineLibraryCreation.Parts, "the shader parts weren't made again" );

		var pipeline = Device.CreateGraphicsPipeline( Triangle( vs, ps ) );
		Assert.AreEqual( 4, Device.PipelineLibraryCreation.Parts );
		Assert.AreEqual( 1, Device.PipelineLibraryCreation.Links );

		Device.Destroy( pipeline );
		Device.Destroy( vs );
		Device.Destroy( ps );
	}

	/// <summary>
	/// The sample scene draws the same with fast links, after their optimised links replace them, and with pipelines made
	/// whole; destroying its shaders before its pipelines leaves the pipelines' parts until the pipelines go.
	/// </summary>
	[TestMethod]
	public void OptimisedLinksReplaceFastOnesAndDrawTheSame()
	{
		RequireLibraries();
		var target = Device.CreateTexture( TextureInfo.RenderTarget( Width, Height, Format.RGBA8Unorm, name: "Target" ) );
		byte[] fast, optimised;
		using ( var scene = new SampleScene( Device, Format.RGBA8Unorm ) )
		{
			fast = Render( Device, scene, target );
			Assert.IsTrue( Device.WaitForOptimisedPipelines( TimeSpan.FromSeconds( 60 ) ), "the optimised links finish" );
			Assert.AreEqual( 2, Device.PipelineLibraryCreation.Optimised );
			optimised = Render( Device, scene, target );
		}

		// Frames go by so the destroyed pipelines and their parts are freed
		for ( int i = 0; i < 4; i++ ) Device.Wait( Device.Submit( Device.BeginCommandList( QueueType.Graphics, "Frame" ) ) );
		Assert.AreEqual( 4, Device.PartCount, "only the two pipelines' vertex input and output parts are kept" );

		Save( fast, Width, Height, "pipeline-library-fast" );
		CollectionAssert.AreEqual( fast, optimised, "the optimised links draw the same" );

		// Pipelines made whole, and both ways without mesh shaders (push constants visible to fewer stages)
		AssertSame( fast, Draw( libraries: false, meshShaders: true ), "pipelines made whole" );
		AssertSame( fast, Draw( libraries: true, meshShaders: false ), "parts without mesh shaders" );
		AssertSame( fast, Draw( libraries: false, meshShaders: false ), "pipelines made whole without mesh shaders" );
	}

	/// <summary>The sample scene drawn on a device of its own, with the validation layer, which must report nothing.</summary>
	byte[] Draw( bool libraries, bool meshShaders )
	{
		var errors = new List<string>();
		var options = new DeviceOptions { Presentation = false, PipelineLibraries = libraries, MeshShaders = meshShaders, Validation = true, Message = m => { if ( m.Severity == MessageSeverity.Error ) lock ( errors ) errors.Add( $"{m.Id}: {m.Text}" ); } };
		byte[] pixels;
		using ( var device = Device.Create( options ) )
		{
			Assert.AreEqual( libraries && Device.Capabilities.PipelineLibraries, device.Capabilities.PipelineLibraries );
			var target = device.CreateTexture( TextureInfo.RenderTarget( Width, Height, Format.RGBA8Unorm, name: "Target" ) );
			using var scene = new SampleScene( device, Format.RGBA8Unorm );
			pixels = Render( device, scene, target );
		}

		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors.Take( 5 ) ) );
		return pixels;
	}

	void AssertSame( byte[] expected, byte[] actual, string what )
	{
		var differing = 0;
		for ( int i = 0; i < expected.Length; i++ )
		{
			if ( expected[i] != actual[i] ) differing++;
		}

		if ( differing > 0 ) Save( actual, Width, Height, "pipeline-library-" + what.Replace( ' ', '-' ) );
		Assert.AreEqual( 0, differing, $"{what} draw as the parts do: {differing} bytes differ" );
	}

	/// <summary>A pipeline destroyed before its optimised link finishes: the link is thrown away, nothing leaks.</summary>
	[TestMethod]
	public void PipelinesDestroyedWhileOptimisingGo()
	{
		RequireLibraries();
		var vs = Shader( "triangle.vs.spv" );
		var ps = Shader( "triangle.ps.spv" );
		for ( int i = 0; i < 8; i++ )
		{
			var info = Triangle( vs, ps );
			info.Blend = [new BlendState { WriteMask = 1 + i }];
			Device.Destroy( Device.CreateGraphicsPipeline( info ) );
		}

		Assert.IsTrue( Device.WaitForOptimisedPipelines( TimeSpan.FromSeconds( 60 ) ) );
		for ( int i = 0; i < 4; i++ ) Device.Wait( Device.Submit( Device.BeginCommandList( QueueType.Graphics, "Frame" ) ) );
		Device.Destroy( vs );
		Device.Destroy( ps );
		StringAssert.Contains( Device.DescribeResources(), " 0 pipelines" );
	}

	static byte[] Render( Device device, SampleScene scene, TextureHandle target )
	{
		var list = device.BeginCommandList( QueueType.Graphics, "Scene" );
		scene.Render( list, target, ImageLayout.Undefined, ImageLayout.ShaderRead, 0.5f );
		device.Wait( device.Submit( list ) );
		return ReadBack( device, target );
	}

	static byte[] ReadBack( Device device, TextureHandle target )
	{
		var buffer = device.CreateBuffer( new BufferInfo { Size = Width * Height * 4, Memory = MemoryLocation.Readback, Name = "Readback" } );
		var list = device.BeginCommandList( QueueType.Graphics, "Read back" );
		list.Barrier( TextureBarrier.Transition( target, ImageLayout.ShaderRead, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		list.CopyTextureToBuffer( target, 0, 0, buffer );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		device.Wait( device.Submit( list ) );
		var pixels = device.Map( buffer ).ToArray();
		device.Destroy( buffer );
		return pixels;
	}
}
