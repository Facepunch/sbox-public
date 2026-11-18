using Borealis.Gpu;
using System.IO;
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

	/// <summary>
	/// A pipeline that may skip draws is made on workers when its parts aren't made: creating it doesn't wait, a list skips
	/// its draws until it's made, then draws it; one whose parts are all made links at once.
	/// </summary>
	[TestMethod]
	public void DrawsOfUnreadyPipelinesAreSkippedNotWaitedOn()
	{
		var vs = Shader( "triangle.vs.spv" );
		var ps = Shader( "triangle.ps.spv" );
		var info = Triangle( vs, ps );
		info.DepthFormat = Format.Unknown;
		info.SkipUntilReady = true;
		Device.SkipDrawsUntilReady = true;
		var pipeline = Device.CreateGraphicsPipeline( info );
		Assert.IsFalse( Device.IsReady( pipeline ), "nothing was made yet: workers make it" );

		var target = Device.CreateTexture( TextureInfo.RenderTarget( 64, 64, Format.RGBA8Unorm, name: "Target" ) );
		int Draw( ImageLayout from, out int skipped )
		{
			var list = Device.BeginCommandList( QueueType.Graphics, "Draw" );
			list.Barrier( TextureBarrier.Transition( target, from, ImageLayout.ColorTarget, PipelineStage.ColorTargetOutput, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
			list.BeginRendering( [ColorTarget.Of( target, System.Numerics.Vector4.Zero )] );
			list.SetViewport( 0, 0, 64, 64 );
			list.SetScissor( 0, 0, 64, 64 );
			list.SetPipeline( pipeline );
			list.PushConstants( new System.Numerics.Vector4( 0, 0, 1, 0 ) );
			list.Draw( 3 );
			list.EndRendering();
			skipped = list.SkippedDraws;
			var draws = list.Counts.Draws;
			Device.Wait( Device.Submit( list ) );
			return draws;
		}

		// Very rarely the workers win the race to the first draw
		var drawn = Draw( ImageLayout.Undefined, out var skippedFirst );
		if ( skippedFirst == 1 ) Assert.AreEqual( 0, drawn, "the draw was skipped, not waited on" );

		Assert.IsTrue( Device.WaitForPendingPipelines( TimeSpan.FromSeconds( 60 ) ) );
		Assert.IsTrue( Device.IsReady( pipeline ) );
		Assert.AreEqual( 1, Draw( ImageLayout.ColorTarget, out var skippedAfter ) );
		Assert.AreEqual( 0, skippedAfter );

		var stats = Device.DeferredPipelines;
		Assert.AreEqual( 1, stats.Made );
		Assert.AreEqual( skippedFirst, (int)stats.SkippedDraws );

		// The same again links its made parts at once
		if ( Device.Capabilities.PipelineLibraries ) Assert.IsTrue( Device.IsReady( Device.CreateGraphicsPipeline( info ) ) );
		Assert.IsTrue( Device.WaitForOptimisedPipelines( TimeSpan.FromSeconds( 60 ) ) );
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

	/// <summary>
	/// A shader part is keyed by its SPIR-V and what its layout's sets hold: a second module of the same SPIR-V with an
	/// identically defined binding layout links the parts the first made; they go when the last such module does.
	/// </summary>
	[TestMethod]
	public void PartsAreSharedBySpirvAndSetContents()
	{
		RequireLibraries();
		BindingSlot[] slots = [new( 0, BindingType.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment )];
		var first = Triangle( Shader( "triangle.vs.spv" ), Shader( "triangle.ps.spv" ) );
		first.Sets = [Device.CreateBindingLayout( slots )];
		var second = Triangle( Shader( "triangle.vs.spv" ), Shader( "triangle.ps.spv" ) );
		second.Sets = [Device.CreateBindingLayout( slots )];

		var a = Device.CreateGraphicsPipeline( first );
		Assert.AreEqual( 4, Device.PipelineLibraryCreation.Parts );
		var b = Device.CreateGraphicsPipeline( second );
		Assert.AreEqual( 4, Device.PipelineLibraryCreation.Parts, "the same SPIR-V and set contents: no new parts" );
		Assert.AreEqual( 4, Device.PartCount );

		Assert.IsTrue( Device.WaitForOptimisedPipelines( TimeSpan.FromSeconds( 60 ) ) );
		Device.Destroy( a );
		Device.Destroy( b );
		for ( int i = 0; i < 4; i++ ) Device.Wait( Device.Submit( Device.BeginCommandList( QueueType.Graphics, "Frame" ) ) );
		Device.Destroy( first.VertexShader );
		Device.Destroy( first.FragmentShader );
		Assert.AreEqual( 4, Device.PartCount, "modules of that SPIR-V are still alive" );
		Device.Destroy( second.VertexShader );
		Device.Destroy( second.FragmentShader );
		Assert.AreEqual( 2, Device.PartCount, "the shader parts went with the last modules" );
	}

	/// <summary>
	/// The part manifest: a run records the parts its pipelines linked beside the pipeline cache; the next run makes the
	/// vertex input and output parts when its device is made and the shader parts as their modules are, so a pipeline asked
	/// for afterwards is ready at once and makes no part. Lines that don't read are dropped; a damaged file is an empty one.
	/// </summary>
	[TestMethod]
	public void PartManifestMakesPartsBeforeFirstUse()
	{
		RequireLibraries();
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-part-manifest-{Environment.ProcessId}-{Guid.NewGuid():n}" );
		var errors = new List<string>();
		Device Open() => Device.Create( new DeviceOptions
		{
			Presentation = false,
			Validation = true,
			PipelineCacheFolder = folder,
			PipelinePartManifest = true,
			Message = m => { if ( m.Severity == MessageSeverity.Error ) lock ( errors ) errors.Add( $"{m.Id}: {m.Text}" ); },
		} );

		GraphicsPipelineInfo Info( Device device )
		{
			var info = Triangle( device.CreateShader( Load( "triangle.vs.spv" ), "vs" ), device.CreateShader( Load( "triangle.ps.spv" ), "ps" ) );
			info.Sets = [device.CreateBindingLayout( [new BindingSlot( 0, BindingType.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment )] )];
			info.SkipUntilReady = true;
			return info;
		}

		try
		{
			using ( var device = Open() )
			{
				device.SkipDrawsUntilReady = false;
				Assert.AreEqual( 0, device.PartManifest.Loaded );
				var first = Info( device );
				device.CreateGraphicsPipeline( first );
				Assert.AreEqual( 4, device.PartManifest.Recorded );
				Assert.IsFalse( device.PartsRecorded( first.VertexShader ), "parts linked this run don't stop guesses" );
			}

			var file = Path.Combine( folder, "pipeline-parts.txt" );
			Assert.IsTrue( File.Exists( file ) );
			Assert.AreEqual( 2, Directory.GetFiles( Path.Combine( folder, "spirv" ), "*.spv" ).Length, "the vertex and pixel shaders' SPIR-V" );
			File.AppendAllLines( file, ["1\tP\tnot a hash", "garbage", "1\tO\t999.1\t1\t1\t0"] );

			using ( var device = Open() )
			{
				Assert.AreEqual( 4, device.PartManifest.Loaded, "the four parts; the lines that don't read are dropped" );

				// From the SPIR-V kept beside the manifest: before any module of the program is made
				var clock = System.Diagnostics.Stopwatch.StartNew();
				while ( device.PipelineLibraryCreation.Parts < 4 && clock.Elapsed < TimeSpan.FromSeconds( 60 ) ) System.Threading.Thread.Sleep( 10 );
				Assert.AreEqual( 4, device.PipelineLibraryCreation.Parts, "made from the kept SPIR-V, before the program's modules" );
				var info = Info( device );
				Assert.IsTrue( device.PartsRecorded( info.VertexShader ) && device.PartsRecorded( info.FragmentShader ) );
				Assert.IsTrue( device.WaitForPendingPipelines( TimeSpan.FromSeconds( 60 ) ) );
				Assert.AreEqual( 4, device.PartManifest.Queued );
				Assert.AreEqual( 4, device.PipelineLibraryCreation.Parts, "made before any pipeline was asked for" );

				device.SkipDrawsUntilReady = true;
				var pipeline = device.CreateGraphicsPipeline( info );
				Assert.IsTrue( device.IsReady( pipeline ), "its parts were all made: linked at once" );
				Assert.AreEqual( 4, device.PipelineLibraryCreation.Parts );
				Assert.IsTrue( device.WaitForOptimisedPipelines( TimeSpan.FromSeconds( 60 ) ) );
			}

			Assert.IsFalse( File.ReadAllText( file ).Contains( "garbage" ), "lines that don't read aren't written back" );
			File.WriteAllText( file, "not a manifest\n\x01\x02" );
			using ( var device = Open() ) Assert.AreEqual( 0, device.PartManifest.Loaded, "a damaged file is an empty one" );
		}
		finally
		{
			Directory.Delete( folder, recursive: true );
		}

		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors.Take( 5 ) ) );
	}

	/// <summary>
	/// The module manifest: a run records the modules its variants made; the next run makes them as their compiled file
	/// loads, before any variant asks, and their recorded parts follow (the part manifest).
	/// </summary>
	[TestMethod]
	public void ModuleManifestMakesVariantModulesAsTheirFileLoads()
	{
		RequireLibraries();
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-module-manifest-{Environment.ProcessId}-{Guid.NewGuid():n}" );
		Device Open() => Device.Create( new DeviceOptions { Presentation = false, PipelineCacheFolder = folder, PipelinePartManifest = true } );
		try
		{
			using ( var device = Open() )
			using ( var library = new Borealis.Source2.Shaders.ShaderLibrary( device, ShaderFileTests.Files ) )
			{
				var shader = library.Load( "shaders/ui_text.shader" );
				Assert.IsNotNull( library.Variant( shader, "Forward", new int[shader.Features.StaticCombos.Length] ).Vertex.Module );
				Assert.IsTrue( library.ModuleManifest.Recorded >= 2, "its vertex and pixel modules" );
				device.SavePipelineCache();
			}

			using ( var device = Open() )
			using ( var library = new Borealis.Source2.Shaders.ShaderLibrary( device, ShaderFileTests.Files ) )
			{
				var recorded = library.ModuleManifest.Loaded;
				Assert.IsTrue( recorded >= 2 );
				library.Load( "shaders/ui_text.shader" );
				var clock = System.Diagnostics.Stopwatch.StartNew();
				while ( library.ModuleManifest.Made < recorded && clock.Elapsed < TimeSpan.FromSeconds( 30 ) ) System.Threading.Thread.Sleep( 10 );
				Assert.AreEqual( recorded, library.ModuleManifest.Made, "made as the file loaded, before any variant" );
			}
		}
		finally
		{
			Directory.Delete( folder, recursive: true );
		}
	}

	/// <summary>
	/// The part manifest keeps its lines in the order the last run first linked them, which is the order the next run
	/// makes them: a first frame's parts come first.
	/// </summary>
	[TestMethod]
	public void PartManifestReplaysInLastRunsOrder()
	{
		var file = Path.Combine( Path.GetTempPath(), $"borealis-part-order-{Environment.ProcessId}-{Guid.NewGuid():n}.txt" );
		static string Output( Format format ) => PipelinePartManifest.Write( PipelineParts.FragmentOutput, new GraphicsPipelineInfo { ColorFormats = [format], Samples = 1 }, 0, null );
		var (a, b, c) = (Output( Format.RGBA8Unorm ), Output( Format.RGBA16Float ), Output( Format.R32Uint ));
		try
		{
			var first = new PipelinePartManifest( file );
			first.Use( a, 0 );
			first.Use( b, 0 );
			first.Use( c, 0 );
			Assert.IsTrue( first.Save() );

			var second = new PipelinePartManifest( file );
			CollectionAssert.AreEqual( new[] { a, b, c }, second.TakeUnshaded().OrderBy( e => e.Rank ).Select( e => e.Line ).ToArray() );
			second.Use( c, 0 );
			second.Use( a, 0 );
			Assert.IsTrue( second.Save() );

			var third = new PipelinePartManifest( file );
			CollectionAssert.AreEqual( new[] { c, a, b }, third.TakeUnshaded().OrderBy( e => e.Rank ).Select( e => e.Line ).ToArray(), "this run's first links first, then the rest" );
		}
		finally
		{
			File.Delete( file );
		}
	}

	/// <summary>
	/// Compute pipelines go in the part manifest too: the next run makes one on a builder as its module is made, and
	/// creating it then takes that pipeline instead of making another.
	/// </summary>
	[TestMethod]
	public void PartManifestMakesComputePipelinesAhead()
	{
		RequireLibraries();
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-compute-manifest-{Environment.ProcessId}-{Guid.NewGuid():n}" );
		var errors = new List<string>();
		Device Open() => Device.Create( new DeviceOptions
		{
			Presentation = false,
			Validation = true,
			PipelineCacheFolder = folder,
			PipelinePartManifest = true,
			Message = m => { if ( m.Severity == MessageSeverity.Error ) lock ( errors ) errors.Add( $"{m.Id}: {m.Text}" ); },
		} );

		try
		{
			using ( var device = Open() )
			{
				device.Destroy( device.CreateComputePipeline( new ComputePipelineInfo { Shader = device.CreateShader( Load( "pattern.cs.spv" ), "pattern" ), Entry = "MainCs" } ) );
				Assert.AreEqual( 1, device.PartManifest.Recorded );
			}

			using ( var device = Open() )
			{
				var shader = device.CreateShader( Load( "pattern.cs.spv" ), "pattern" );
				Assert.IsTrue( device.WaitForPendingPipelines( TimeSpan.FromSeconds( 60 ) ) );
				Assert.AreEqual( 1, device.ComputePipelineCreation.Made, "made ahead as its module was made" );
				var pipeline = device.CreateComputePipeline( new ComputePipelineInfo { Shader = shader, Entry = "MainCs" } );
				Assert.AreEqual( 1, device.ComputePipelineCreation.Made, "creating it took the one made ahead" );
				device.Destroy( pipeline );
			}
		}
		finally
		{
			Directory.Delete( folder, recursive: true );
		}

		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors.Take( 5 ) ) );
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
