using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Shaders;
using Sandbox;
using Sandbox.BorealisLab.Surfaces;
using System.IO;
using System.Threading;

namespace BorealisGpuTests;

/// <summary>
/// GPU-driven aggregates (<see cref="BackendDevice.GpuDriven"/>, docs/reference/borealis-gpu-driven.md): the shaders'
/// <c>.gpudriven.shader_c</c> variants pull vertices and read materials from tables, and a pass's fragments of every
/// material draw as one multi-draw per pipeline.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GpuDrivenTests : GpuTest
{
	const int Width = 480, Height = 300;
	const int Rows = 3;

	/// <summary>
	/// Aggregates of boxes in three materials (prepass, forward) draw the same pixels GPU-driven as the classic way, one
	/// bucket for all three materials instead of a bind each; with a bucket per material; culled on the CPU instead of the
	/// GPU; and culled on the GPU with and without compacting the commands.
	/// </summary>
	[TestMethod]
	public void AggregatesDrawTheSamePixelsGpuDriven()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDrivenMeshes = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			Color[] colours = [new( 0.8f, 0.6f, 0.4f ), new( 0.3f, 0.7f, 0.4f ), new( 0.4f, 0.5f, 0.9f )];
			for ( int row = 0; row < Rows; row++ )
			{
				var (mesh, fragments) = Boxes( backend, backend.CreateMaterial( $"Boxes {row}", colours[row] ), row * 120 - 120 );
				world.Add( new AggregateObject( mesh, fragments ) );
			}

			// Static meshes too: one drawn twice (an instanced run), and one of a material the aggregates also have
			var (shared, _) = Boxes( backend, backend.CreateMaterial( "Mesh boxes", new Color( 0.9f, 0.3f, 0.3f ) ), 240 );
			world.Add( new MeshObject( shared, Transform.Zero ) );
			world.Add( new MeshObject( shared, new Transform( new Vector3( 0, 0, 90 ) ) ) );
			var (own, _) = Boxes( backend, backend.CreateMaterial( "Mesh boxes 2", colours[0] ), -240 );
			world.Add( new MeshObject( own, Transform.Zero ) );

			var view = new RenderView { Name = "GPU-driven test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			long gpuDrivenBatches = 0;
			byte[] Frame( bool gpuDriven, out int binds, bool materialBuckets, bool gpuCulling = true, bool compaction = true, bool positions = true, bool visibility = false )
			{
				backend.GpuDriven = gpuDriven;
				backend.GpuDrivenVisibility = visibility;
				backend.GpuDrivenCulling = gpuCulling;
				backend.GpuDrivenCompaction = compaction;
				backend.PositionStreams = positions;
				backend.GpuDrivenMaterialBuckets = materialBuckets;
				backend.TimeDraws = true;
				backend.DrawTimes.Reset();
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				binds = (int)backend.DrawTimes.Binds;
				gpuDrivenBatches = backend.DrawTimes.GpuDrivenBatches;
				backend.TimeDraws = false;
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var classic = Frame( false, out var classicBinds, false );
			var driven = Frame( true, out var drivenBinds, false );
			// The GPU culls them, so preparing the view tests none: each aggregate's batch has all five fragments, the one
			// beyond the far plane too
			// (from the view's resident list, where the device keeps one)
			var aggregateFeature = system.GetFeature<Borealis.Features.AggregateRenderFeature>();
			var preparedFragments = aggregateFeature.BatchesFor( system.MainPass ).Concat( aggregateFeature.ResidentFor( system.MainPass )?.Batches ?? [] ).Select( b => b.Count ).ToArray();
			var drivenBatches = gpuDrivenBatches;
			var drawRecords = backend.GpuDrivenTables.Records.Draws;
			var again = Frame( true, out _, false );
			Assert.AreEqual( drawRecords, backend.GpuDrivenTables.Records.Draws, "the second frame reuses the aggregates' draw records" );
			var perMaterial = Frame( true, out var perMaterialBinds, true );
			var cpuCulled = Frame( true, out _, false, gpuCulling: false );
			var uncompacted = Frame( true, out _, false, compaction: false );
			var fullVertices = Frame( true, out _, false, positions: false );
			var visible = Frame( true, out _, false, visibility: true );
			var visibleAgain = Frame( true, out _, false, visibility: true );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			CollectionAssert.AreEqual( Enumerable.Repeat( 5, Rows ).ToArray(), preparedFragments, "every fragment prepared, for the GPU to cull" );
			Assert.IsTrue( drivenBatches > Rows * 2, $"the static meshes drew GPU-driven with the aggregates ({drivenBatches} batches)" );
			Assert.AreEqual( 0, Different( classic, perMaterial ), "a bucket per material draws the classic pixels" );
			Assert.IsTrue( perMaterialBinds > drivenBinds, $"a bucket per material binds more ({perMaterialBinds}) than one bucket ({drivenBinds})" );
			Assert.IsTrue( backend.GpuDrivenTables.Records.Draws > 1, $"the fragments have draw records ({backend.GpuDrivenTables.Records.Draws})" );
			Assert.IsTrue( drivenBinds < classicBinds, $"fewer binds GPU-driven ({drivenBinds}) than classic ({classicBinds})" );
			Assert.AreEqual( 0, Different( classic, driven ), "GPU-driven draws the classic pixels" );
			CollectionAssert.AreEqual( driven, again, "the same frame twice" );
			Assert.AreEqual( 0, Different( classic, cpuCulled ), "culled on the CPU, the classic pixels too" );
			Assert.AreEqual( 0, Different( classic, uncompacted ), "culled on the GPU without compacting, the classic pixels too" );
			Assert.AreEqual( 0, Different( classic, fullVertices ), "the prepass from full vertices rather than positions alone, the classic pixels too" );
			Assert.IsNotNull( backend.GpuDrivenVisibilityShaders.Ids, "the id prepass shader is built" );
			Assert.AreEqual( 0, Different( classic, visible ), "forward over only the triangles that own a sample, the classic pixels too" );
			Assert.AreEqual( 0, Different( classic, visibleAgain ), "and the next frame" );
			Assert.IsNotNull( backend.GpuDrivenCullShader, "the culling shader is built, so the GPU-driven frames culled on the GPU" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>A camera's stages with nothing in them, with screen-space reflections or not.</summary>
	sealed class ReflectingStages( bool reflections ) : IRenderStages
	{
		public void Render( RenderStage stage, in StageTarget target ) { }
		public bool HasScreenSpaceReflections => reflections;
	}

	/// <summary>
	/// A shader's GPU-driven variant writes its visibility id, a uint, at target 1 for the id prepass. Drawn in an opaque pass
	/// whose target 1 is the reflections' normals (RGB10A2), or in one with no target 1, its pipeline leaves that output out:
	/// the validation layer reports nothing written at location 1 of another type or to nowhere. The id prepass keeps it.
	/// </summary>
	[TestMethod]
	public void TheIdOutputIsLeftOutWhereTargetOneIsNotIds()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDrivenMeshes = true, GpuDriven = true };
		if ( backend.Shaders.GpuDriven( backend.Shaders.Load( "shaders/complex.shader" ) ) is null ) Assert.Inconclusive( "shaders/complex.shader has no GPU-driven variant: ShaderCompiler -gpudriven" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Id output test" );

			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			var (mesh, fragments) = Boxes( backend, backend.CreateMaterial( "Boxes", new Color( 0.8f, 0.6f, 0.4f ) ), 0 );
			world.Add( new AggregateObject( mesh, fragments ) );
			world.Add( new MeshObject( mesh, new Transform( new Vector3( 0, 0, 90 ) ) ) );

			var view = new RenderView { Name = "Id output test", FieldOfView = 70, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			// With reflections (target 1 the normals), without (no target 1), and through the id prepass (target 1 the ids)
			foreach ( var (reflections, visibility) in (ReadOnlySpan<(bool, bool)>)[(true, false), (true, false), (false, false), (false, true)] )
			{
				backend.GpuDrivenVisibility = visibility;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target, new ReflectingStages( reflections ) );
				Device.WaitIdle();
			}

			Assert.IsTrue( backend.DrawsAggregatesGpuDriven, "the boxes drew GPU-driven" );
			var warnings = ValidationWarnings().Where( w => w.Contains( "Location 1", StringComparison.Ordinal ) ).ToArray();
			Assert.AreEqual( 0, warnings.Length, "Location 1 warnings:\n" + string.Join( "\n", warnings.Take( 5 ) ) );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Aggregates of one material drawn GPU-driven by segments recording on several threads at once (the prepass, the
	/// sun's cascades and forward, each reading the same material and frame attributes) finish every frame and draw what
	/// serial recording does.
	/// </summary>
	[TestMethod]
	public void AggregatesRecordFromParallelSegments()
	{
		// The meshes stay classic, for workers to record beside the aggregates' run
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, GpuDrivenMeshes = false };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem();
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven parallel test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 3;

			var material = backend.CreateMaterial( "Shared boxes", new Color( 0.8f, 0.6f, 0.4f ) );
			for ( int row = 0; row < Rows; row++ )
			{
				var (mesh, fragments) = Boxes( backend, material, row * 120 - 120 );
				world.Add( new AggregateObject( mesh, fragments ) );
			}

			// Enough classic draws besides, each its own mesh and material, for the frame to record in parallel
			for ( int i = 0; i < 80; i++ )
			{
				var (small, _) = Boxes( backend, backend.CreateMaterial( $"Mesh boxes {i}", new Color( 0.3f, 0.5f + i / 200.0f, 0.4f ) ), 360 + i / 10 * 20 );
				world.Add( new MeshObject( small, new Transform( new Vector3( 0, 0, i % 10 * 15 ), Rotation.Identity, 0.2f ) ) );
			}

			var view = new RenderView { Name = "GPU-driven parallel test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			(byte[] Pixels, double WorkerMs) Frame( bool parallel )
			{
				system.ParallelRecording = parallel;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();

				// A frame that never finishes recording fails here instead of hanging the run
				var render = System.Threading.Tasks.Task.Run( () => system.Render( world, view, target ) );
				Assert.IsTrue( render.Wait( TimeSpan.FromSeconds( 60 ) ), "the frame finished recording" );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return (pixels, system.Stats.WorkerRecordMs);
			}

			var serial = Frame( false );
			var workerMs = 0.0;
			byte[] parallel = null;
			for ( int i = 0; i < 20; i++ )
			{
				var frame = Frame( true );
				workerMs += frame.WorkerMs;
				parallel ??= frame.Pixels;
				CollectionAssert.AreEqual( parallel, frame.Pixels, $"parallel frame {i} draws what the first did" );
			}

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.IsTrue( workerMs > 0, "segments recorded on workers" );
			Assert.AreEqual( 0, Different( serial.Pixels, parallel ), "parallel recording draws what serial recording does" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Resident aggregates (<see cref="BackendDevice.GpuDrivenResident"/>): static aggregates, drawn in the prepass, the sun's
	/// cascades and forward, draw the pixels views preparing their own draw; from the frame their lists exist, calls are drawn
	/// from kept plans; a material's value, a combo in their object attributes and an aggregate removed each make the plans
	/// again and still draw what the views' own batches do.
	/// </summary>
	[TestMethod]
	public void ResidentAggregatesDrawFromKeptPlans()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven resident test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 2;

			var materials = new BackendMaterial[Rows];
			var aggregates = new AggregateObject[Rows];
			for ( int row = 0; row < Rows; row++ )
			{
				materials[row] = (BackendMaterial)backend.CreateMaterial( $"Resident boxes {row}", new Color( 0.3f + row * 0.2f, 0.6f, 0.4f ) );
				var (mesh, fragments) = Boxes( backend, materials[row], row * 120 - 120 );
				world.Add( aggregates[row] = new AggregateObject( mesh, fragments ) { IsStatic = true } );
			}

			var view = new RenderView { Name = "GPU-driven resident test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( bool resident )
			{
				backend.GpuDrivenResident = resident;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			// Each change: the views' own batches twice (the second with their visibility ready), then resident until a plan
			// draws, which must draw the same
			var feature = system.GetFeature<Borealis.Features.AggregateRenderFeature>();
			void Same( string change )
			{
				Frame( false );
				var own = Frame( false );
				var hits = backend.GpuDrivenPlanHits;
				byte[] resident = null;
				for ( int i = 0; i < 4 && backend.GpuDrivenPlanHits == hits; i++ ) resident = Frame( true );
				Assert.IsTrue( backend.GpuDrivenPlanHits > hits, $"{change}: a call drawn from a plan" );
				Assert.IsTrue( feature.ResidentFor( system.MainPass ) is { Batches.Count: > 0 }, $"{change}: the main view has a resident list" );
				Assert.AreEqual( 0, Different( own, resident ), $"{change}: resident draws what the views' own batches do" );
				CollectionAssert.AreEqual( resident, Frame( true ), $"{change}: and the next frame the same" );
			}

			Same( "first" );
			var builds = backend.GpuDrivenPlanBuilds;
			Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => feature.ResidentFor( p ) is { Batches.Count: > 0 } ), "a cascade drew the static casters from its list" );
			var still = Frame( true );
			Assert.AreEqual( builds, backend.GpuDrivenPlanBuilds, "a frame like the last makes no plan" );

			// Recorded on workers, whose contexts take plans another context made
			system.ParallelRecording = true;
			for ( int i = 0; i < 6; i++ ) CollectionAssert.AreEqual( still, Frame( true ), $"parallel frame {i} draws what the serial one did" );
			system.ParallelRecording = false;

			materials[0].Set( "g_vColorTint", [0.9f, 0.2f, 0.2f, 0] );
			Same( "a material's value" );
			Assert.IsTrue( backend.GpuDrivenPlanBuilds > builds, "the material's change made plans again" );

			var attributes = backend.CreateParameters();
			attributes.SetData( "PerBatchLightProbeVolumeConstantBuffer_t", new Vector4( 1, 2, 3, 4 ) );
			aggregates[2].LightmapAttributes = attributes;
			aggregates[2].Lightmapped = true;
			Same( "object attributes" );
			attributes.SetCombo( "D_OPAQUE_FADE", 1 );
			Same( "a combo in them" );

			world.Remove( aggregates[1] );
			Same( "an aggregate gone" );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Resident aggregates in a Slang surface whose render state turns alpha test on (the material's own
	/// <see cref="SurfaceMaterial.AlphaTest"/> off), and in one whose render state blends, draw from kept plans: a frame
	/// like the last makes no plan, and the pixels are those of the views' own batches.
	/// </summary>
	[TestMethod]
	public void ResidentSurfaceAlphaTestedByItsRenderStateKeepsItsPlans()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is null ) Assert.Inconclusive( "No game content" );
		if ( !File.Exists( Path.Combine( EngineFolders.NativeIn( Path.GetDirectoryName( core ) ), EngineFolders.Library( "slang" ) ) ) ) Assert.Inconclusive( "The engine's Slang isn't built" );

		var folder = Fixtures.Folder( "resident-surface" );
		var previousHost = RenderHost.Current;
		try
		{
			// Stripes of zero opacity, clipped only when the render state's alpha test is on
			var path = Path.Combine( folder, "clip_stripes.slang" );
			File.WriteAllText( path, "import sbox.surface;\n[Material]\npublic struct ClipStripes : ISurface\n{\n"
				+ "\t[RenderState( \"AlphaTest\" )] bool Clip;\n"
				+ "\t[RenderState( \"Blend: Opaque, Alpha\" )] int Mode;\n"
				+ "\t[Default3( 1, 1, 1 )] float3 Tint;\n"
				+ "\tpublic Material Evaluate( SurfaceInput i )\n\t{\n\t\tMaterial m = Material.Init( i );\n\t\tm.Albedo = Tint;\n"
				+ "\t\tm.Opacity = frac( i.Uv.x * 4 ) < 0.5 ? 1.0 : 0.0;\n\t\treturn m;\n\t}\n}\n" );

			using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
			RenderHost.Current = new BackendRenderHost( backend );
			var material = (SurfaceMaterial)backend.Surfaces.CreateMaterial( path, null, new Dictionary<string, float[]> { ["Clip"] = [1], ["Tint"] = [0.8f, 0.5f, 0.3f] } );
			Assert.IsNotNull( material, "the surface compiled" );
			Assert.IsFalse( material.AlphaTest, "the material's own alpha test is off" );
			Assert.IsTrue( material.RenderState.AlphaTest, "its render state turns alpha test on" );
			var blended = (SurfaceMaterial)backend.Surfaces.CreateMaterial( path, null, new Dictionary<string, float[]> { ["Mode"] = [1], ["Tint"] = [0.3f, 0.5f, 0.8f] } );
			Assert.IsTrue( blended.RenderState.Blends, "the other's render state blends" );

			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven resident surface test" );
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 2;
			for ( int row = 0; row < Rows; row++ )
			{
				var (mesh, fragments) = Boxes( backend, row == Rows - 1 ? blended : material, row * 120 - 120 );
				world.Add( new AggregateObject( mesh, fragments ) { IsStatic = true } );
			}

			var view = new RenderView { Name = "GPU-driven resident surface test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( bool resident )
			{
				backend.GpuDrivenResident = resident;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			Frame( false );
			var own = Frame( false );
			var hits = backend.GpuDrivenPlanHits;
			byte[] resident = null;
			for ( int i = 0; i < 4 && backend.GpuDrivenPlanHits == hits; i++ ) resident = Frame( true );
			Assert.IsTrue( backend.GpuDrivenPlanHits > hits, "a call drawn from a plan" );
			Assert.AreEqual( 0, Different( own, resident ), "resident draws what the views' own batches do" );

			var builds = backend.GpuDrivenPlanBuilds;
			hits = backend.GpuDrivenPlanHits;
			for ( int i = 0; i < 3; i++ ) CollectionAssert.AreEqual( resident, Frame( true ), $"frame {i} after draws the same" );
			Assert.AreEqual( builds, backend.GpuDrivenPlanBuilds, "frames like the last make no plan" );
			Assert.IsTrue( backend.GpuDrivenPlanHits >= hits + 3, "every frame drew from plans" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Resident meshes (<see cref="BackendDevice.GpuDrivenResidentMeshes"/>): a seeded field of boxes with three LODs, a
	/// colour each, half with draw tints, some not static, at random places and scales, some at a fixed LOD, draw the pixels
	/// the views' own batches draw (whose LODs
	/// the CPU picks), in the prepass, the sun's cascades and forward, from kept plans; the views prepare none of them; and a
	/// fixed LOD set, a box moved and a box removed each still draw what the views' own batches do.
	/// </summary>
	[TestMethod]
	public void ResidentMeshesPickTheCpusLods()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Resident meshes test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 2;

			// One box, three LODs of a colour each (the last only two faces), switching by screen size
			var vertices = new List<MeshVertex>();
			var indices = new List<uint>();
			Box( vertices, indices, Vector3.Zero, 10 );
			var lodMaterials = new[] { new Color( 0.9f, 0.2f, 0.2f ), new Color( 0.2f, 0.9f, 0.2f ), new Color( 0.2f, 0.2f, 0.9f ) }
				.Select( ( c, i ) => backend.CreateMaterial( $"Resident LOD {i}", c ) ).ToArray();
			var single = BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), lodMaterials[0], "Resident LOD box" );
			var draw = single.Draws[0];
			RenderMesh.Draw[][] lods = [[draw], [draw with { Material = lodMaterials[1] }], [draw with { Material = lodMaterials[2], IndexCount = 12 }]];
			var model = new RenderMesh( single.Geometry, single.Bounds, lods, [0, 30, 90] );

			// And a copy whose draws have tints, so each mesh's entries are a kept run of one per draw
			RenderMesh.Draw[][] tintedLods = [[draw with { Tint = new Vector3( 1, 0.6f, 0.6f ) }], [lods[1][0] with { Tint = new Vector3( 0.6f, 1, 0.6f ) }], [lods[2][0]]];
			var tinted = new RenderMesh( single.Geometry, single.Bounds, tintedLods, [0, 30, 90] );

			var random = new Random( 1234 );
			var boxes = new List<MeshObject>();
			for ( int i = 0; i < 160; i++ )
			{
				var position = new Vector3( random.NextSingle() * 1600 - 200, random.NextSingle() * 1600 - 800, random.NextSingle() * 60 );
				var scale = 0.5f + random.NextSingle() * 2.5f;
				var box = new MeshObject( i % 2 == 0 ? model : tinted, new Transform( position, Rotation.FromYaw( random.NextSingle() * 360 ), scale ) ) { IsStatic = i % 5 != 0 };
				if ( i % 17 == 0 ) box.LodOverride = i % 3;
				world.Add( box );
				boxes.Add( box );
			}

			var view = new RenderView { Name = "Resident meshes test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -400, 0, 200 );
			view.Rotation = Rotation.LookAt( new Vector3( 1, 0, -0.15f ) );

			// The CPU's LODs over the field: every LOD is picked somewhere
			var tanHalfFov = MathF.Tan( view.FieldOfView.DegreeToRadian() * 0.5f );
			var picked = new int[3];
			foreach ( var box in boxes ) picked[Borealis.Features.MeshRenderFeature.ChooseLod( box, world.BoundsCenter[box.Index], view.Position, tanHalfFov, Width )]++;
			Assert.IsTrue( picked.All( n => n > 5 ), $"the field takes every LOD: {string.Join( ", ", picked )}" );

			byte[] Frame( bool resident )
			{
				backend.GpuDrivenResidentMeshes = resident;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var aggregates = system.GetFeature<Borealis.Features.AggregateRenderFeature>();
			var meshes = system.GetFeature<Borealis.Features.MeshRenderFeature>();
			void Same( string change )
			{
				Frame( false );
				var own = Frame( false );
				var hits = backend.GpuDrivenPlanHits;
				byte[] resident = null;
				for ( int i = 0; i < 4 && backend.GpuDrivenPlanHits == hits; i++ ) resident = Frame( true );
				Assert.IsTrue( backend.GpuDrivenPlanHits > hits, $"{change}: a call drawn from a plan" );
				Assert.IsTrue( aggregates.ResidentFor( system.MainPass ) is { MeshGroups.Count: > 0 }, $"{change}: the main view's list holds meshes" );
				Assert.AreEqual( 0, meshes.RunsFor( system.MainPass ).GpuDriven.Count, $"{change}: the main view prepared none of them" );
				Assert.AreEqual( 0, Different( own, resident ), $"{change}: resident draws what the views' own batches do" );
				var builds = backend.GpuDrivenPlanBuilds;
				CollectionAssert.AreEqual( resident, Frame( true ), $"{change}: and the next frame the same" );
				Assert.AreEqual( builds, backend.GpuDrivenPlanBuilds, $"{change}: a frame like the last makes no plan" );
			}

			Same( "first" );
			Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => aggregates.ResidentFor( p ) is { MeshGroups.Count: > 0 } ), "a cascade drew the static casters from its list" );

			boxes[5].LodOverride = 2;
			Same( "a fixed LOD" );

			boxes[8].Transform = boxes[8].Transform.WithPosition( boxes[8].Transform.Position + new Vector3( 300, 0, 0 ) );
			Same( "a box moved" );

			world.Remove( boxes[3] );
			Same( "a box gone" );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// The resident cull (<see cref="BackendDevice.GpuDrivenResidentCull"/>): resident aggregates and a seeded field of
	/// boxes with three LODs, in the prepass, two cascades and forward, culled for every view in setup draw the pixels each
	/// call culling its own draws, serial and on workers; and after a box moves and a material changes, again.
	/// </summary>
	[TestMethod]
	public void ResidentCullDrawsWhatEachCallCulls()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		if ( backend.ResidentCullShader is null ) Assert.Inconclusive( "borealis_resident_cull_cs isn't compiled" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Resident cull test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 2;

			var material = (BackendMaterial)backend.CreateMaterial( "Resident cull boxes", new Color( 0.5f, 0.6f, 0.4f ) );
			var (rowMesh, fragments) = Boxes( backend, material, -120 );
			world.Add( new AggregateObject( rowMesh, fragments ) { IsStatic = true } );

			var vertices = new List<MeshVertex>();
			var indices = new List<uint>();
			Box( vertices, indices, Vector3.Zero, 10 );
			var lodMaterials = new[] { new Color( 0.9f, 0.2f, 0.2f ), new Color( 0.2f, 0.9f, 0.2f ), new Color( 0.2f, 0.2f, 0.9f ) }
				.Select( ( c, i ) => backend.CreateMaterial( $"Resident cull LOD {i}", c ) ).ToArray();
			var single = BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), lodMaterials[0], "Resident cull LOD box" );
			var draw = single.Draws[0];
			RenderMesh.Draw[][] lods = [[draw], [draw with { Material = lodMaterials[1] }], [draw with { Material = lodMaterials[2], IndexCount = 12 }]];
			var model = new RenderMesh( single.Geometry, single.Bounds, lods, [0, 30, 90] );

			var random = new Random( 4321 );
			var boxes = new List<MeshObject>();
			for ( int i = 0; i < 120; i++ )
			{
				var position = new Vector3( random.NextSingle() * 1600 - 200, random.NextSingle() * 1600 - 800, random.NextSingle() * 60 );
				var box = new MeshObject( model, new Transform( position, Rotation.FromYaw( random.NextSingle() * 360 ), 0.5f + random.NextSingle() * 2.5f ) ) { IsStatic = true };
				world.Add( box );
				boxes.Add( box );
			}

			var view = new RenderView { Name = "Resident cull test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -400, 0, 200 );
			view.Rotation = Rotation.LookAt( new Vector3( 1, 0, -0.15f ) );

			byte[] Frame( bool cull )
			{
				backend.GpuDrivenResidentCull = cull;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			// Each call culling its own until its plans are kept, then the resident cull until calls draw from it
			void Same( string change )
			{
				for ( int i = 0; i < 3; i++ ) Frame( false );
				var own = Frame( false );
				var calls = backend.ResidentCullCalls;
				byte[] culled = null;
				for ( int i = 0; i < 4 && backend.ResidentCullCalls == calls; i++ ) culled = Frame( true );
				var views = backend.ResidentCullViews;
				culled = Frame( true );
				Assert.IsTrue( backend.ResidentCullCalls >= calls + 3, $"{change}: the prepass, forward and a cascade drew from the resident cull ({backend.ResidentCullCalls - calls})" );
				Assert.IsTrue( backend.ResidentCullViews - views >= 3, $"{change}: it culled for the main view's passes and the cascades ({backend.ResidentCullViews - views})" );
				Assert.AreEqual( 0, Different( own, culled ), $"{change}: the resident cull draws what each call culling its own does" );
			}

			Same( "first" );

			system.ParallelRecording = true;
			Same( "on workers" );
			system.ParallelRecording = false;

			boxes[4].Transform = boxes[4].Transform.WithPosition( boxes[4].Transform.Position + new Vector3( 200, 0, 0 ) );
			((BackendMaterial)lodMaterials[1]).Set( "g_vColorTint", [0.9f, 0.9f, 0.2f, 0] );
			Same( "a box moved and a material changed" );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A render system disposed takes its views out of the resident cull's state (<see cref="GpuDevice.ReleaseView"/>): the
	/// device keeps which calls each view drew, and with them the view's pass and what it last drew, until frames age them
	/// out, which after the renderer has gone (at shutdown) never happens.
	/// </summary>
	[TestMethod]
	public void DisposedRenderSystemLeavesNoViewsInTheResidentCull()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		if ( backend.ResidentCullShader is null ) Assert.Inconclusive( "borealis_resident_cull_cs isn't compiled" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var system = new RenderSystem { ParallelRecording = false };
			try
			{
				using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Resident cull release test" );
				var world = new RenderWorld();
				world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
				world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
				world.Lighting.SunShadowCascades = 2;

				var material = (BackendMaterial)backend.CreateMaterial( "Resident cull release boxes", new Color( 0.5f, 0.6f, 0.4f ) );
				var (rowMesh, fragments) = Boxes( backend, material, -120 );
				world.Add( new AggregateObject( rowMesh, fragments ) { IsStatic = true } );

				var view = new RenderView { Name = "Resident cull release test", FieldOfView = 70, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
				view.Position = new Vector3( -400, 0, 200 );
				view.Rotation = Rotation.LookAt( new Vector3( 1, 0, -0.15f ) );

				void Frame( bool cull )
				{
					backend.GpuDrivenResidentCull = cull;
					backend.BeginFrame();
					RenderHost.Current.NextFrame();
					system.Render( world, view, target );
					Device.WaitIdle();
				}

				// Each call culling its own until its plans are kept, then the resident cull until calls draw from it
				for ( int i = 0; i < 3; i++ ) Frame( false );
				for ( int i = 0; i < 4 && backend.ResidentCullCalls == 0; i++ ) Frame( true );
				Frame( true );
				Assert.IsTrue( backend.ResidentCullCalls > 0, "calls drew from the resident cull" );
				Assert.IsTrue( backend.ResidentCulls.HeldViews >= 2, $"it kept the main view and a cascade ({backend.ResidentCulls.HeldViews})" );
			}
			finally
			{
				system.Dispose();
			}

			Assert.AreEqual( 0, backend.ResidentCulls.HeldViews, "the disposed system's views are gone from the resident cull" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// The sun's far cascade cache takes resident casters that aren't static by when each last moved,
	/// on the GPU (<see cref="GpuContext.ResidentFilter"/>): its lists hold the boxes and the aggregate that aren't static,
	/// settled or not (what's drawn over it takes its own), and each call culling its own and the resident cull draw what every
	/// view preparing its own does. A
	/// cache drawn so holds no caster that hadn't settled: once those move away, without the cache drawn again, the picture is
	/// that of a cache every view drew its own way.
	/// </summary>
	[TestMethod]
	public void ResidentFarCacheTakesCastersByWhenTheyMoved()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		if ( backend.ResidentCullShader is null ) Assert.Inconclusive( "borealis_resident_cull_cs isn't compiled" );

		var previousHost = RenderHost.Current;
		var (wasCached, wasWhole) = (Borealis.Shadows.ShadowMapper.FarCascadeCache, Borealis.Shadows.ShadowMapper.FarCacheRedrawWhole);
		RenderHost.Current = new BackendRenderHost( backend );
		Borealis.Shadows.ShadowMapper.ResetCache();
		Borealis.Shadows.ShadowMapper.FarCascadeCache = true;
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Resident far cache test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 3;

			// The ground, and boxes on it: static, not static and settled, and not static and not settled
			var material = (BackendMaterial)backend.CreateMaterial( "Resident far cache", new Color( 0.6f, 0.6f, 0.55f ) );
			MeshVertex[] ground = [new( new Vector3( -4000, -4000, 0 ), Vector3.Up, new Vector2( 0, 0 ) ), new( new Vector3( 8000, -4000, 0 ), Vector3.Up, new Vector2( 1, 0 ) ),
				new( new Vector3( 8000, 4000, 0 ), Vector3.Up, new Vector2( 1, 1 ) ), new( new Vector3( -4000, 4000, 0 ), Vector3.Up, new Vector2( 0, 1 ) )];
			var groundMesh = BackendMesh.Create( backend, ground, [0, 2, 1, 0, 3, 2], material, "Resident far cache ground" );
			world.Add( new MeshObject( groundMesh, new Transform( Vector3.Zero ) ) { IsStatic = true } );

			var vertices = new List<MeshVertex>();
			var indices = new List<uint>();
			Box( vertices, indices, Vector3.Zero, 1 );
			var cube = BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), material, "Resident far cache box" );
			var random = new Random( 2468 );
			MeshObject Add( bool isStatic )
			{
				var box = new MeshObject( cube, new Transform( new Vector3( random.NextSingle() * 5000, random.NextSingle() * 5000 - 2500, 60 ), Rotation.FromYaw( random.NextSingle() * 360 ), 20 + random.NextSingle() * 40 ) ) { IsStatic = isStatic };
				world.Add( box );
				return box;
			}

			for ( int i = 0; i < 40; i++ ) Add( true );
			var settled = Enumerable.Range( 0, 40 ).Select( _ => Add( false ) ).ToArray();
			var (rowMesh, fragments) = Boxes( backend, material, 900 );
			var aggregate = new AggregateObject( rowMesh, fragments );
			world.Add( aggregate );

			var view = new RenderView { Name = "Resident far cache test", FieldOfView = 70, ZNear = 4, ZFar = 10000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -300, 0, 500 );
			view.Rotation = Rotation.LookAt( new Vector3( 1, 0, -0.2f ) );

			byte[] Frame( bool resident, bool cull )
			{
				Borealis.Features.AggregateRenderFeature.ResidentEnabled = resident;
				backend.GpuDrivenResidentCull = cull;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			// What's here now has settled; what's added after hasn't
			Frame( false, false );
			Borealis.Shadows.SettledCasters.Reset( settled: true );
			var unsettled = Enumerable.Range( 0, 40 ).Select( _ => Add( false ) ).ToArray();

			// The cache drawn whole every frame: every view's own way, each call culling its own, then the resident cull
			Borealis.Shadows.ShadowMapper.FarCacheRedrawWhole = true;
			byte[] reference = null;
			for ( int i = 0; i < 4; i++ ) reference = Frame( false, false );
			byte[] own = null;
			for ( int i = 0; i < 4; i++ ) own = Frame( true, false );
			Assert.AreEqual( 0, Different( reference, own ), "each call culling its own draws what every view's own way does" );

			var aggregates = system.GetFeature<Borealis.Features.AggregateRenderFeature>();
			var passes = system.ShadowMaps.Passes.ToArray();
			var cache = passes.First( p => p.View.Name == "CSM Far Cache" );
			var over = passes.Single( p => p.StaticFilter == Borealis.StaticFilter.Unsettled );
			bool Holds( ViewPass pass, RenderObject obj ) => aggregates.ResidentFor( pass ) is { } list && obj.Index < list.Holds.Length && list.Holds[obj.Index];
			Assert.IsTrue( Holds( cache, settled[0] ) && Holds( cache, unsettled[0] ) && Holds( cache, aggregate ), "the cache's list holds casters that aren't static, settled or not" );
			Assert.IsFalse( Holds( over, unsettled[0] ) || Holds( over, aggregate ), "what's drawn over it takes its own, which the collect keeps" );

			var calls = backend.ResidentCullCalls;
			byte[] culled = null;
			for ( int i = 0; i < 4 && backend.ResidentCullCalls == calls; i++ ) culled = Frame( true, true );
			culled = Frame( true, true );
			Assert.IsTrue( backend.ResidentCullCalls > calls, "calls drew from the resident cull" );
			Assert.AreEqual( 0, Different( reference, culled ), "the resident cull draws what every view's own way does" );

			// Kept caches, then the boxes that hadn't settled moved under the ground: one the GPU drew, one every view's own way did
			var places = unsettled.Select( b => b.Transform ).ToArray();
			byte[] Kept( bool resident )
			{
				for ( int i = 0; i < unsettled.Length; i++ ) unsettled[i].Transform = places[i];
				Borealis.Shadows.ShadowMapper.FarCacheRedrawWhole = true;
				for ( int i = 0; i < 3; i++ ) Frame( resident, resident );
				Borealis.Shadows.ShadowMapper.FarCacheRedrawWhole = false;
				foreach ( var box in unsettled ) box.Transform = box.Transform.WithPosition( box.Transform.Position.WithZ( -3000 ) );
				Frame( resident, resident );
				return Frame( resident, resident );
			}

			var gpuCache = Kept( true );
			var ownCache = Kept( false );
			Assert.AreEqual( 0, Different( ownCache, gpuCache ), "a cache the GPU drew holds no caster that hadn't settled" );
			Assert.IsTrue( aggregates.SettledListsMade > 0 );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			Borealis.Features.AggregateRenderFeature.ResidentEnabled = true;
			Borealis.Shadows.ShadowMapper.FarCascadeCache = wasCached;
			Borealis.Shadows.ShadowMapper.FarCacheRedrawWhole = wasWhole;
			Borealis.Shadows.ShadowMapper.ResetCache();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// The record memo (<see cref="BackendDevice.GpuDrivenRecordMemo"/>) gives every GPU-culled call the batch records
	/// looking them up gives: on a frame like the last, which it serves, and after each change it has to notice - the
	/// batches drawn, a material's values, object attributes and the lighting record they make, a surface's fields, and the
	/// GPU-driven switches.
	/// </summary>
	[TestMethod]
	public void RecordMemoGivesTheRecordsLookingUpDoes()
	{
		// Views preparing their own batches, which the memo serves (resident lists are drawn from plans instead)
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenResident = false };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven memo test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 1;

			var materials = new BackendMaterial[Rows];
			var aggregates = new AggregateObject[Rows];
			for ( int row = 0; row < Rows; row++ )
			{
				materials[row] = (BackendMaterial)backend.CreateMaterial( $"Memo boxes {row}", new Color( 0.3f + row * 0.2f, 0.6f, 0.4f ) );
				var (mesh, fragments) = Boxes( backend, materials[row], row * 120 - 120 );
				world.Add( aggregates[row] = new AggregateObject( mesh, fragments ) );
			}

			// Two aggregates of one mesh, the second's fragments raised: the same batch but for its fragments' bounds
			var (twinMesh, twinFragments) = Boxes( backend, materials[0], -240 );
			var raised = twinFragments.Select( f => f with { Transform = Matrix3x4.Identity with { Row2 = new Vector4( 0, 0, 1, 90 ) }, BoundsMin = f.BoundsMin + new Vector3( 0, 0, 90 ), BoundsMax = f.BoundsMax + new Vector3( 0, 0, 90 ) } ).ToArray();
			AggregateObject[] twins = [new( twinMesh, twinFragments ), new( twinMesh, raised )];
			world.Add( twins[0] );
			world.Add( twins[1] );

			// A surface's aggregate too, where the engine's Slang and the rock's textures are built
			var core = ContentFiles.FindGame().Roots.FirstOrDefault();
			var slang = core is not null && File.Exists( Path.Combine( EngineFolders.NativeIn( Path.GetDirectoryName( core ) ), EngineFolders.Library( "slang" ) ) );
			if ( slang ) backend.Surfaces.AddFolder( Path.GetFullPath( Path.Combine( core, "..", "..", "engine", "Launcher", "BorealisLab", "Surfaces" ) ) );
			var color = slang ? backend.LoadTexture( "materials/default/rope_color_png_95bee33c.generated.vtex" ) : null;
			var normal = slang ? backend.LoadTexture( "materials/default/rope_normal_png_b8960e44.generated.vtex" ) : null;
			var rma = slang ? backend.LoadTexture( "materials/default/rope01_vmat_g_trma_bbf6a2a.generated.vtex" ) : null;
			using var rock = color is not null && normal is not null && rma is not null ? new Rock { Color = color, Normal = normal, Rma = rma, Tint = new Vector3( 1, 0.8f, 0.6f ) } : null;
			if ( rock is not null )
			{
				var (mesh, fragments) = Boxes( backend, rock, 240 );
				world.Add( new AggregateObject( mesh, fragments ) );
			}

			var view = new RenderView { Name = "GPU-driven memo test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			// Each GPU-culled call's batch records but their transform slot (word 2), which is the frame's
			uint[][] Records( bool memo )
			{
				backend.GpuDrivenRecordMemo = memo;
				backend.GpuDrivenRecordLog = new();
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var log = backend.GpuDrivenRecordLog;
				backend.GpuDrivenRecordLog = null;
				foreach ( var call in log )
				{
					for ( int i = 2; i < call.Length; i += 8 ) call[i] = 0;
				}

				return log.ToArray();
			}

			// The frame after a change, from the memo, then the next looking everything up; a change moves some record
			uint[][] last = null;
			void Same( string change, bool moves = true )
			{
				var memo = Records( true );
				var lookedUp = Records( false );
				Assert.IsTrue( lookedUp.Length > 0, $"{change}: the frame drew GPU-culled" );
				Assert.AreEqual( lookedUp.Length, memo.Length, $"{change}: calls" );
				for ( int c = 0; c < memo.Length; c++ ) CollectionAssert.AreEqual( lookedUp[c], memo[c], $"{change}: call {c}'s records" );

				var moved = last is null || last.Length != lookedUp.Length;
				for ( int c = 0; !moved && c < lookedUp.Length; c++ ) moved = !lookedUp[c].AsSpan().SequenceEqual( last[c] );
				Assert.IsTrue( moved || !moves, $"{change}: no record moved, so the change tests nothing" );
				last = lookedUp;
			}

			Records( true );
			var hits = backend.GpuDrivenRecordMemoHits;
			last = Records( false );
			hits = backend.GpuDrivenRecordMemoHits;
			var memoAgain = Records( true );
			Assert.IsTrue( backend.GpuDrivenRecordMemoHits > hits, "a frame like the last takes its records from the memo" );
			for ( int c = 0; c < memoAgain.Length; c++ ) CollectionAssert.AreEqual( last[c], memoAgain[c], $"nothing changed: call {c}'s records" );

			world.Remove( aggregates[1] );
			Same( "an aggregate gone" );
			world.Add( aggregates[1] );
			Same( "and back, last" );
			foreach ( var twin in twins )
			{
				world.Remove( twin );
				Same( "one of two aggregates of a mesh gone" );
				world.Add( twin );
				Same( "and back" );
			}

			materials[0].Set( "g_vColorTint", [0.9f, 0.2f, 0.2f, 0] );
			Same( "a material's value" );

			// Lit by object attributes: their own bucket, and the lighting record their constants make
			var attributes = backend.CreateParameters();
			attributes.SetData( "PerBatchLightProbeVolumeConstantBuffer_t", new Vector4( 1, 2, 3, 4 ) );
			aggregates[2].LightmapAttributes = attributes;
			aggregates[2].Lightmapped = true;
			Same( "object attributes" );
			attributes.SetData( "PerBatchLightProbeVolumeConstantBuffer_t", new Vector4( 5, 6, 7, 8 ) );
			Same( "their lighting constants" );

			// A combo they set: another variant, with the same material, attributes hash and lighting
			attributes.SetCombo( "D_OPAQUE_FADE", 1 );
			Same( "a combo, so another variant" );

			if ( rock is not null )
			{
				rock.Tint = new Vector3( 0.2f, 0.3f, 1 );
				Same( "a surface's field" );
			}

			// These boxes have no positions-only copy: full vertices draw them either way
			backend.PositionStreams = false;
			Same( "full vertices for depth", moves: false );
			backend.PositionStreams = true;
			backend.GpuDrivenVisibility = false;
			Same( "no visibility" );
			backend.GpuDrivenVisibility = true;
			Same( "visibility again" );
			backend.GpuDrivenMaterialBuckets = true;
			Same( "a bucket per material" );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A draw census (<see cref="BackendDevice.Census"/>) of a GPU-driven frame puts every draw call each pass recorded on a
	/// draw it counted: the aggregates' three materials as one forward bucket, with the commands and triangles the GPU drew
	/// read back, and the static mesh as a classic draw that says why it isn't GPU-driven. Counting doesn't change the frame.
	/// </summary>
	[TestMethod]
	public void CensusCountsEveryDraw()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, GpuDrivenMeshes = false };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven census test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			Color[] colours = [new( 0.8f, 0.6f, 0.4f ), new( 0.3f, 0.7f, 0.4f ), new( 0.4f, 0.5f, 0.9f )];
			for ( int row = 0; row < Rows; row++ )
			{
				var (mesh, fragments) = Boxes( backend, backend.CreateMaterial( $"Census boxes {row}", colours[row] ), row * 120 - 120 );
				world.Add( new AggregateObject( mesh, fragments ) );
			}

			var (own, _) = Boxes( backend, backend.CreateMaterial( "Census mesh boxes", new Color( 0.9f, 0.3f, 0.3f ) ), 240 );
			world.Add( new MeshObject( own, Transform.Zero ) );

			var view = new RenderView { Name = "Census", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( DrawCensus census )
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				backend.Census = census;
				try
				{
					system.Render( world, view, target );
				}
				finally
				{
					backend.Census = null;
				}

				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			// The view's first frame draws without ids; the second is the one counted
			Frame( null );
			var plain = Frame( null );
			var census = new DrawCensus();
			var counted = Frame( census );
			census.Resolve( Device );
			var after = Frame( null );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.AreEqual( 0, Different( plain, counted ), "counting draws the same frame" );
			Assert.AreEqual( 0, Different( plain, after ), "and the frame after it too" );

			var calls = census.Passes.Values.Sum( p => p.Draws );
			Assert.IsTrue( calls > 0, "the passes recorded draws" );
			Assert.AreEqual( calls, census.Draws.Sum( d => d.Calls ), "every draw call a pass recorded is a draw the census counted" );

			var forward = census.Draws.Where( d => d.Path == DrawCensus.Path.Bucket && d.Pass.EndsWith( "Opaque Forward", StringComparison.Ordinal ) ).ToArray();
			Assert.IsTrue( forward.Length > 0, "forward drew buckets" );
			Assert.AreEqual( 1, forward.Select( d => d.Bucket ).Distinct().Count(), "the three materials share one forward bucket" );
			Assert.AreEqual( Rows, forward[0].BucketMaterials, "of three materials" );
			Assert.IsTrue( forward.All( d => d.Commands >= 0 && d.Shader == "complex" ), "its multi-draws' commands were read back" );
			Assert.IsTrue( census.Draws.Where( d => d.Path == DrawCensus.Path.BucketIds ).Sum( d => d.Triangles ) >= Rows * 4 * 12, "the prepass drew every box in view as ids" );

			var meshDraws = census.Draws.Where( d => d.Path == DrawCensus.Path.Classic && d.Material == "Census mesh boxes" ).ToArray();
			Assert.IsTrue( meshDraws.Length > 0, "the static mesh drew classic" );
			Assert.IsTrue( meshDraws.All( d => d.Kind == "static mesh: GPU-driven meshes off" ), $"and says why ({string.Join( ", ", meshDraws.Select( d => d.Kind ).Distinct() )})" );
			Assert.IsTrue( meshDraws.All( d => d.Triangles == 5 * 12 ), "all five of its boxes" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A GPU-driven table that grows while a frame records keeps its old buffer: draws recorded earlier in the frame read it
	/// at its slot once the frame is submitted, which was a GPU page fault and a lost device when it was destroyed at once.
	/// </summary>
	[TestMethod]
	public void GrownTablesKeepTheirOldBuffer()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var first = tables.LightingBuffer;
		for ( int i = 1; tables.LightingBuffer.Equals( first ) && i < 100000; i++ ) tables.Lighting( BitConverter.GetBytes( i ), null );

		Assert.IsFalse( tables.LightingBuffer.Equals( first ), "the lighting table grew" );
		Device.WaitIdle();
		Assert.IsTrue( Device.IsAlive( first ), "its old buffer is kept" );
	}

	/// <summary>
	/// A page all of whose meshes have gone (their models freed) is freed at a frame's start, with the records keyed by its
	/// slot, so a page that takes the slot later doesn't find them; one that still holds a mesh stays. The page being filled
	/// goes too once it's empty (a map unloaded left two 56 MiB index pages behind), and the next mesh starts a new one.
	/// </summary>
	[TestMethod]
	public void PagesWhoseMeshesHaveAllGoneAreFreed()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var layout = backend.Layout( new VertexLayout( 12, [new VertexLayout.Field( VertexSemantic.Position, 0, VertexFormat.Float3, 0 )] ) );

		// Two meshes filling a page, then one that starts the next (the current page)
		var big = new byte[GpuDrivenTables.VertexPageSize / 2];
		var (slotA, offsetA) = tables.AddVertices( big );
		var (slotB, _) = tables.AddVertices( big );
		var (slotC, _) = tables.AddVertices( new byte[1024] );
		Assert.AreEqual( slotA, slotB );
		Assert.AreNotEqual( slotA, slotC );
		var record = tables.Geometry( slotA, offsetA, layout, 16 );
		var bytesBefore = tables.Bytes.Pages;

		// One mesh gone: the page stays
		tables.ReleaseRange( slotA, big.Length );
		tables.FreeEmptyPages();
		Assert.AreEqual( 0, tables.PagesFreed );

		// Both gone, and the page being filled emptied: both freed, their records forgotten
		tables.ReleaseRange( slotB, big.Length );
		tables.ReleaseRange( slotC, 1024 );
		tables.FreeEmptyPages();
		Assert.AreEqual( 2, tables.PagesFreed );
		Assert.AreEqual( bytesBefore - 2 * GpuDrivenTables.VertexPageSize, tables.Bytes.Pages );
		Assert.AreNotEqual( record, tables.Geometry( slotA, offsetA, layout, 16 ), "a record for the freed slot is made afresh" );

		// The next mesh starts a new page
		var (slotD, offsetD) = tables.AddVertices( new byte[1024] );
		Assert.AreEqual( 0, offsetD );
		Assert.AreEqual( bytesBefore - GpuDrivenTables.VertexPageSize, tables.Bytes.Pages );
		tables.ReleaseRange( slotD, 1024 );
		Device.WaitIdle();
	}

	/// <summary>
	/// Frame chunks a busy stretch added (a map's frames) are freed once their frame slot has gone a while without taking
	/// from them; each slot keeps its first.
	/// </summary>
	[TestMethod]
	public void IdleFrameChunksAreFreed()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		for ( int f = 0; f < 3; f++ )
		{
			tables.BeginFrame();
			for ( int i = 0; i < 3; i++ ) tables.Allocate( 3 << 20 );
		}

		Assert.AreEqual( 9, tables.FrameChunks );
		for ( int f = 0; f < 3 * (GpuDrivenTables.IdleChunkTurns + 2); f++ )
		{
			tables.BeginFrame();
			tables.Allocate( 1024 );
		}

		Assert.AreEqual( 3, tables.FrameChunks );
		Device.WaitIdle();
	}

	/// <summary>
	/// Draw and lighting records are found without a lock by recording threads (a lock per draw kept them waiting on each
	/// other): threads asking for the same records at once each get the one record made for each.
	/// </summary>
	[TestMethod]
	public void RecordsFoundAtOnceFromManyThreadsAreMadeOnce()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var before = tables.Records.Draws;
		const int Keys = 2000, Threads = 8;
		var draws = new uint[Threads, Keys];
		var lighting = new uint[Threads, Keys / 10];
		System.Threading.Tasks.Parallel.For( 0, Threads, t =>
		{
			for ( int k = 0; k < Keys; k++ ) draws[t, k] = tables.Draw( 1, 2, 3, 0, (uint)k * 3, 3, 0, 0 );
			for ( int k = 0; k < Keys / 10; k++ ) lighting[t, k] = tables.Lighting( BitConverter.GetBytes( k + 1 ), null );
		} );

		Assert.AreEqual( before + Keys, tables.Records.Draws, "one record per key" );
		for ( int t = 1; t < Threads; t++ )
		{
			for ( int k = 0; k < Keys; k++ ) Assert.AreEqual( draws[0, k], draws[t, k] );
			for ( int k = 0; k < Keys / 10; k++ ) Assert.AreEqual( lighting[0, k], lighting[t, k] );
		}

		Device.WaitIdle();
	}

	/// <summary>
	/// A material's stage churns once it has made <see cref="GpuDrivenTables.MaxRecordsPerStage"/> material records (new
	/// attribute values each time): that stage only, not the material's others or another material's.
	/// </summary>
	[TestMethod]
	public void AStageChurnsAfterItsLimitOfMaterialRecords()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var shader = backend.Shaders.Load( "shaders/complex.shader" );
		var variant = shader is null ? null : backend.Shaders.Variant( shader, "Forward", default );
		if ( variant?.Vertex is null || variant.Pixel is null ) Assert.Inconclusive( "complex.shader isn't compiled (gone: standard's surface draws its materials)" );
		var material = (BackendMaterial)backend.CreateMaterial( "Churning", Color.White );
		var other = (BackendMaterial)backend.CreateMaterial( "Steady", Color.White );

		uint Record( BackendMaterial m, ShaderStage stage, int attributes ) =>
			tables.Material( new GpuDrivenTables.MaterialRecordKey( m, m.Version, stage, false, attributes ), 64, 0, static ( _, bytes ) => bytes.Clear() );

		Record( other, variant.Pixel, 0 );
		for ( int i = 0; i < GpuDrivenTables.MaxRecordsPerStage - 1; i++ ) Record( material, variant.Pixel, i );
		Assert.IsFalse( tables.Churns( material, variant.Pixel ), "one short of the limit" );
		Record( material, variant.Pixel, 0 );
		Assert.IsFalse( tables.Churns( material, variant.Pixel ), "a record it has already is found, not made" );

		Record( material, variant.Pixel, GpuDrivenTables.MaxRecordsPerStage );
		Assert.IsTrue( tables.Churns( material, variant.Pixel ), "at the limit" );
		Assert.IsFalse( tables.Churns( material, variant.Vertex ), "its other stage" );
		Assert.IsFalse( tables.Churns( other, variant.Pixel ), "another material" );
		Device.WaitIdle();
	}

	/// <summary>
	/// A material a game sets a new value on every frame (wind, a pulse) makes a material record each time it's drawn
	/// GPU-driven, and records never go: GAMMA's trees took 75 GB of host memory in three minutes. Past its limit its draws
	/// stay classic, so the table stops growing, and they draw as the classic path does.
	/// </summary>
	[TestMethod]
	public void AMaterialChangedEveryFrameStopsAddingRecords()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDrivenMeshes = true, TimeDraws = true };
		if ( backend.Shaders.GpuDriven( backend.Shaders.Load( "shaders/complex.shader" ) ) is null ) Assert.Inconclusive( "complex.shader has no GPU-driven variant: ShaderCompiler -gpudriven" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Churn test" );
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;
			var material = (BackendMaterial)backend.CreateMaterial( "Pulsing boxes", new Color( 0.8f, 0.6f, 0.4f ) );
			var (mesh, fragments) = Boxes( backend, material, 0 );
			world.Add( new AggregateObject( mesh, fragments ) );

			var view = new RenderView { Name = "Churn test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( bool gpuDriven, float tint )
			{
				backend.GpuDriven = gpuDriven;
				material.Set( "g_vColorTint", [0.8f, 0.6f, tint, 0] );
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var before = backend.GpuDrivenTables.Records.Materials;
			const int Frames = GpuDrivenTables.MaxRecordsPerStage + 60;
			for ( int i = 0; i < Frames; i++ ) Frame( true, i / (float)Frames );
			var made = backend.GpuDrivenTables.Records.Materials - before;
			for ( int i = 0; i < 30; i++ ) Frame( true, 1 + i / 30f );
			var after = backend.GpuDrivenTables.Records.Materials - before;
			backend.GpuDrivenLeftClassic.Clear();
			var driven = Frame( true, 0.25f );
			var left = backend.GpuDrivenLeftClassic.Keys.ToArray();
			var classic = Frame( false, 0.25f );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			// A record per stage (the prepass's and forward's, vertex and pixel) for each value until the limit, then none
			Assert.AreEqual( made, after, $"no material records past the limit: {made} over {Frames} frames, {after} after 30 more" );
			Assert.IsTrue( left.Any( x => x.Contains( "material records churn", StringComparison.Ordinal ) ), $"its draws stay classic: {string.Join( ", ", left )}" );
			Assert.AreEqual( 0, Different( classic, driven ), "and draw the classic pixels" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A released owner's visibility (<see cref="GpuDrivenTables.Visibility"/>) is free again once the frames that could read
	/// it are done, and reused: owners made and let go over and over never run the stamps and vertex bits out. They used to
	/// be kept for good, so resident groups made again on Construct took every bit and later owners drew without culling.
	/// </summary>
	[TestMethod]
	public void ReleasedVisibilityIsReused()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var a = new object();
		var made = tables.Visibility( a, 4, 1000 );
		Assert.AreNotEqual( GpuDrivenTables.NoVisibility, made.VertexBase, "room for the first owner" );
		Assert.AreEqual( made, tables.Visibility( a, 4, 1000 ), "made once" );

		// Not reused while a frame in flight may still read it
		tables.ReleaseFragments( a );
		for ( int i = 0; i < 3; i++ )
		{
			Assert.AreNotEqual( made.VertexBase, tables.Visibility( new object(), 4, 1000 ).VertexBase, $"not reused {i} frames after its release" );
			tables.BeginFrame();
		}

		var top = tables.VisibilityBits;
		tables.BeginFrame();
		Assert.AreEqual( made, tables.Visibility( new object(), 4, 1000 ), "reused once those frames are done" );
		Assert.AreEqual( top, tables.VisibilityBits, "reused below the top" );

		// A fifth of the bits each, made and let go many times over (three released ones wait at a time): always room
		var fifth = (int)(GpuDrivenTables.FrameVisibilityBitBase / 5);
		for ( int round = 0; round < 64; round++ )
		{
			var owner = new object();
			Assert.AreNotEqual( GpuDrivenTables.NoVisibility, tables.Visibility( owner, 1, fifth ).VertexBase, $"room in round {round}" );
			tables.ReleaseFragments( owner );
			tables.BeginFrame();
		}

		Assert.IsTrue( tables.VisibilityUse.Bits <= 3L * (fifth + 32) + top, $"only what's held or waiting is in use ({tables.VisibilityUse.Bits} bits)" );
		Assert.IsTrue( tables.VisibilityRunsReleased >= 65, "every release counted" );
	}

	/// <summary>
	/// Owners collected without a release give their visibility back when room runs out, and an owner that found no room
	/// looks again once it's free, rather than drawing without culling for good.
	/// </summary>
	[TestMethod]
	public void CollectedOwnersVisibilityIsReused()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var half = (int)(GpuDrivenTables.FrameVisibilityBitBase / 2);
		MakeAndDrop( tables, half, 2 );
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		var owner = new object();
		Assert.AreEqual( GpuDrivenTables.NoVisibility, tables.Visibility( owner, 1, half ).VertexBase, "no room while the collected owners' runs wait" );
		for ( int i = 0; i < 4; i++ ) tables.BeginFrame();
		Assert.AreNotEqual( GpuDrivenTables.NoVisibility, tables.Visibility( owner, 1, half ).VertexBase, "room once they're free" );

		[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.NoInlining )]
		static void MakeAndDrop( GpuDrivenTables tables, int vertices, int count )
		{
			for ( int i = 0; i < count; i++ ) Assert.AreNotEqual( GpuDrivenTables.NoVisibility, tables.Visibility( new object(), 1, vertices ).VertexBase, $"room for owner {i}" );
		}
	}

	/// <summary>
	/// Once room runs out, runs no call names any more are let go (<see cref="GpuDrivenTables.StaleVisibilityFrames"/>) and the
	/// owner that found no room gets one; a run a call keeps naming stays. Captures used to take a run for every object they
	/// saw, so a map's main view drew most of its objects without culling for good.
	/// </summary>
	[TestMethod]
	public void StaleVisibilityIsLetGoWhenRoomRunsOut()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var half = (int)(GpuDrivenTables.FrameVisibilityBitBase / 2);
		var stale = new object();
		var drawn = new object();
		var late = new object();
		Assert.AreNotEqual( GpuDrivenTables.NoVisibility, tables.Visibility( stale, 1, half ).VertexBase, "room for the first owner" );
		var kept = tables.Visibility( drawn, 1, half );
		Assert.AreNotEqual( GpuDrivenTables.NoVisibility, kept.VertexBase, "room for the second owner" );

		// Every frame draws the second owner and asks for the third; nothing names the first
		var epoch = tables.VisibilityEpoch;
		var frames = 0;
		while ( tables.Visibility( late, 1, half ).VertexBase == GpuDrivenTables.NoVisibility )
		{
			Assert.IsTrue( frames++ < GpuDrivenTables.StaleVisibilityFrames + 10, "room within the stale frames and those in flight" );
			Assert.AreEqual( kept, tables.Visibility( drawn, 1, half ), "a run that's named stays" );
			tables.BeginFrame();
		}

		Assert.IsTrue( frames >= GpuDrivenTables.StaleVisibilityFrames, $"not let go before it's stale ({frames} frames)" );
		Assert.AreEqual( 1, tables.VisibilityRunsAged, "only the unnamed run let go" );
		Assert.AreEqual( epoch + 2, tables.VisibilityEpoch, "calls name their runs again when a round starts and when its runs are free" );
		Assert.AreEqual( kept, tables.Visibility( drawn, 1, half ), "the named run kept its place" );
		Assert.AreEqual( GpuDrivenTables.NoVisibility, tables.Visibility( stale, 1, half ).VertexBase, "the stale owner looks again and finds no room" );
	}

	/// <summary>
	/// Static overlays draw GPU-driven a call per render order (<see cref="BackendDevice.GpuDrivenOverlays"/>): four overlays of
	/// two materials in render order 0 are one bucket, the one in render order 1 another, nothing draws classic, and where
	/// they don't overlap the pixels are the classic path's.
	/// </summary>
	[TestMethod]
	public void OverlaysDrawGpuDrivenByRenderOrder()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenMeshes = false };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven overlay test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			GpuMaterial[] materials = [backend.CreateMaterial( "Overlay A", new Color( 0.8f, 0.6f, 0.4f ) ), backend.CreateMaterial( "Overlay B", new Color( 0.3f, 0.7f, 0.4f ) )];
			for ( int i = 0; i < 5; i++ )
			{
				var (mesh, _) = Boxes( backend, materials[i % 2], i * 100 - 200 );
				world.Add( new MeshObject( mesh, new Transform( new Vector3( 0, 0, i * 15 ) ) ) { LayerMatch = LayerMatch.StaticOverlay, RenderOrder = i == 4 ? 1 : 0 } );
			}

			var view = new RenderView { Name = "Overlays", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( bool gpuDriven, DrawCensus census = null )
			{
				backend.GpuDrivenOverlays = gpuDriven;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				backend.Census = census;
				try
				{
					system.Render( world, view, target );
				}
				finally
				{
					backend.Census = null;
				}

				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var classic = Frame( false );
			var census = new DrawCensus();
			var driven = Frame( true, census );
			census.Resolve( Device );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.AreEqual( 0, Different( classic, driven ), "the overlays draw the classic pixels" );

			var overlays = census.Draws.Where( d => d.Pass.EndsWith( "Static World Overlays", StringComparison.Ordinal ) ).ToArray();
			Assert.AreEqual( 0, overlays.Count( d => d.Path == DrawCensus.Path.Classic ), "no overlay drew classic" );
			Assert.AreEqual( 2, overlays.Select( d => d.Bucket ).Distinct().Count(), "a bucket per render order" );
			Assert.AreEqual( 2, overlays.Max( d => d.BucketMaterials ), "render order 0's two materials in one bucket" );
			Assert.IsTrue( overlays.Sum( d => d.Commands ) >= 5, "every overlay drew" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Resident static overlays (<see cref="BackendDevice.GpuDrivenResidentOverlays"/>): coplanar overlapping overlays of two
	/// materials in render orders 0 and 1, and one out of view, draw from lists culled on the GPU with their order kept, so the
	/// same overlay wins each pixel as when the view prepares them; render order 2's overlay with object attributes, which
	/// would draw after its order's call anyway, draws after its list; render order 3, which holds an overlay with two LODs,
	/// isn't listed and the view draws all of it; and a frame like the last makes no plan.
	/// </summary>
	[TestMethod]
	public void ResidentOverlaysKeepTheirOrder()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Resident overlay test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			// Boxes whose tops are coplanar and overlap, so which one wins a pixel is the order they draw in
			RenderMesh[] meshes = [ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Overlay red", new Color( 0.9f, 0.2f, 0.2f ) ) ), ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Overlay green", new Color( 0.2f, 0.9f, 0.2f ) ) )];
			var overlays = new List<MeshObject>();
			for ( int i = 0; i < 12; i++ )
			{
				var overlay = new MeshObject( meshes[i % 3 == 0 ? 1 : 0], new Transform( new Vector3( i % 6 * 30 - 75, i / 6 * 40 - 20, 0 ) ) ) { LayerMatch = LayerMatch.StaticOverlay, RenderOrder = i / 6 };
				world.Add( overlay );
				overlays.Add( overlay );
			}

			// Out of view, between the others in their order
			world.Add( new MeshObject( meshes[1], new Transform( new Vector3( 0, 3000, 0 ) ) ) { LayerMatch = LayerMatch.StaticOverlay } );

			// Render order 2 holds one the GPU-driven path doesn't take, which draws after the list; render order 3 one with LODs,
			// which isn't resident: the view draws that order itself
			var attributes = backend.CreateParameters();
			world.Add( new MeshObject( meshes[0], new Transform( new Vector3( 20, 60, 10 ) ) ) { LayerMatch = LayerMatch.StaticOverlay, RenderOrder = 2, Attributes = attributes } );
			world.Add( new MeshObject( meshes[1], new Transform( new Vector3( -20, 60, 10 ) ) ) { LayerMatch = LayerMatch.StaticOverlay, RenderOrder = 2 } );
			var draws = meshes[1].Draws.ToArray();
			var twoLods = new RenderMesh( meshes[1].Geometry, meshes[1].Bounds, [draws, draws], [0, 3000] );
			world.Add( new MeshObject( twoLods, new Transform( new Vector3( 40, -60, 20 ) ) ) { LayerMatch = LayerMatch.StaticOverlay, RenderOrder = 3 } );
			world.Add( new MeshObject( meshes[0], new Transform( new Vector3( -40, -60, 20 ) ) ) { LayerMatch = LayerMatch.StaticOverlay, RenderOrder = 3 } );

			var view = new RenderView { Name = "Resident overlays", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -250, -200, 350 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( bool resident )
			{
				backend.GpuDrivenResidentOverlays = resident;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var aggregates = system.GetFeature<Borealis.Features.AggregateRenderFeature>();
			var meshFeature = system.GetFeature<Borealis.Features.MeshRenderFeature>();
			Frame( false );
			var own = Frame( false );
			byte[] resident = null;
			for ( int i = 0; i < 4 && aggregates.ResidentOverlays( system.MainPass ) is not { Count: > 0 }; i++ ) resident = Frame( true );
			resident = Frame( true );

			var listed = aggregates.ResidentOverlays( system.MainPass );
			CollectionAssert.AreEqual( new[] { 0, 1, 2 }, listed.Select( o => o.RenderOrder ).ToArray(), "render orders 0 to 2 are listed, 3 isn't" );
			var orders = meshFeature.RunsFor( system.MainPass ).OverlayOrders;
			CollectionAssert.AreEqual( new[] { 2, 3, 3 }, orders.ToArray(), "the view prepared render order 2's overlay with attributes and both of render order 3's" );
			Assert.AreEqual( 0, Different( own, resident ), "resident overlays draw what the view's own do" );

			var builds = backend.GpuDrivenPlanBuilds;
			CollectionAssert.AreEqual( resident, Frame( true ), "and the next frame the same" );
			Assert.AreEqual( builds, backend.GpuDrivenPlanBuilds, "a frame like the last makes no plan" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A depth-only bucket (its depth pass strips the pixel shader) that draws its ids with its variant's pixel shader has a
	/// pixel material record for it: an alpha-tested generic material on boxes whose draws aren't flagged alpha-tested read
	/// texture slots from record 0 there, another material's words, which lost the device. (Its ids pass alpha-tests where
	/// the stripped depth pass doesn't, so its cut-out pixels aren't the classic ones; frames with ids draw alike.)
	/// </summary>
	[TestMethod]
	public void DepthOnlyBucketsDrawingIdsHaveAPixelRecord()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDrivenVisibility = true };
		backend.Surfaces.ConvertShaders = false;
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			if ( ((GpuDevice)backend).LoadMaterial( "models/vr/autochap_hand.vmat" ) is not BackendMaterial { Shader: not null } material ) { Assert.Inconclusive( "models/vr/autochap_hand.vmat isn't compiled" ); return; }
			if ( backend.Shaders.GpuDriven( material.Shader ) is null ) Assert.Inconclusive( "generic.shader has no GPU-driven variant: ShaderCompiler -gpudriven" );

			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven depth-only ids test" );
			var world = new RenderWorld();
			var (mesh, fragments) = Boxes( backend, material, 0 );
			world.Add( new AggregateObject( mesh, fragments ) );
			var view = new RenderView { Name = "GPU-driven depth-only ids test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			// The view's second frame is the first to draw ids
			byte[] Frame()
			{
				backend.GpuDriven = true;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			Frame();
			var ids = Frame();
			var again = Frame();
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.AreEqual( 0, Different( ids, again ), "frames with ids draw alike" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A frame whose draws are a few GPU-driven runs of many draws each (here, one run of 100 static overlays) still
	/// records on workers: the parallel threshold counts draws, not runs.
	/// </summary>
	[TestMethod]
	public void HeavyGpuDrivenRunsRecordOnWorkers()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenOverlays = true };
		if ( backend.Shaders.GpuDriven( backend.Shaders.Load( "shaders/complex.shader" ) ) is null ) Assert.Inconclusive( "complex has no GPU-driven variant: ShaderCompiler -gpudriven" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = true };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Heavy run test" );

			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			// A mesh each, so they're 100 draws rather than one draw of 100 instances
			var material = backend.CreateMaterial( "Heavy run overlays", new Color( 0.8f, 0.6f, 0.4f ) );
			for ( int i = 0; i < 100; i++ )
			{
				var (mesh, _) = Boxes( backend, material, 0 );
				world.Add( new MeshObject( mesh, new Transform( new Vector3( i % 10 * 30 - 150, i / 10 * 30 - 150, 0 ) ) ) { LayerMatch = LayerMatch.StaticOverlay } );
			}

			var view = new RenderView { Name = "Heavy run", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			var workerMs = 0.0;
			for ( int i = 0; i < 5; i++ )
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				workerMs += system.Stats.WorkerRecordMs;
			}

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.IsTrue( workerMs > 0, "the overlays' run recorded on a worker" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A static mesh that reads the frame buffer copy draws GPU-driven, its depth and its colour: nothing draws the classic
	/// way in the prepass or the opaque pass, and the frame is the classic path's.
	/// </summary>
	[TestMethod]
	public void FrameBufferReadersDrawGpuDriven()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = false, GpuDrivenMeshes = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven reader test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			var (aggregate, fragments) = Boxes( backend, backend.CreateMaterial( "Reader test boxes", new Color( 0.8f, 0.6f, 0.4f ) ), 0 );
			world.Add( new AggregateObject( aggregate, fragments ) );
			var (reader, _) = Boxes( backend, backend.CreateMaterial( "Reader boxes", new Color( 0.3f, 0.7f, 0.4f ) ), 120 );
			world.Add( new MeshObject( reader, Transform.Zero ) { WantsFrameBufferCopy = true } );
			var (plain, _) = Boxes( backend, backend.CreateMaterial( "Plain mesh boxes", new Color( 0.4f, 0.5f, 0.9f ) ), -120 );
			world.Add( new MeshObject( plain, Transform.Zero ) );

			var view = new RenderView { Name = "Readers", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( bool gpuDriven, DrawCensus census = null )
			{
				backend.GpuDriven = gpuDriven;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				backend.Census = census;
				try
				{
					system.Render( world, view, target );
				}
				finally
				{
					backend.Census = null;
				}

				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var classic = Frame( false );
			Frame( true );
			var census = new DrawCensus();
			var driven = Frame( true, census );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.AreEqual( 0, Different( classic, driven ), "the classic pixels" );

			Assert.IsFalse( census.Draws.Any( d => d.Path == DrawCensus.Path.Classic && d.Pass.EndsWith( "Depth Prepass", StringComparison.Ordinal ) ), "nothing in the prepass draws the classic way" );
			Assert.IsFalse( census.Draws.Any( d => d.Path == DrawCensus.Path.Classic && d.Pass.EndsWith( "Opaque Forward", StringComparison.Ordinal ) ), "nor in the opaque pass" );
			var forwardMaterials = census.Draws.Where( d => d.Path == DrawCensus.Path.Bucket && d.Pass.EndsWith( "Opaque Forward", StringComparison.Ordinal ) ).GroupBy( d => d.Bucket ).Sum( g => g.First().BucketMaterials );
			Assert.AreEqual( 3, forwardMaterials, "the aggregate's, the reader's and the other mesh's materials are in the opaque pass's buckets" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Fading objects (tint alpha below one: the faded list, dithered opaque under <c>D_OPAQUE_FADE</c>) draw GPU-driven, in
	/// the opaque pass and the sun's shadows: nothing of theirs draws the classic way there, and the frame is the classic path's.
	/// </summary>
	[TestMethod]
	public void FadedObjectsDrawGpuDriven()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = false, GpuDrivenMeshes = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven faded test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );

			var (ground, _) = Boxes( backend, backend.CreateMaterial( "Faded test ground", new Color( 0.8f, 0.6f, 0.4f ) ), 0 );
			world.Add( new MeshObject( ground, Transform.Zero ) );
			var (faded, _) = Boxes( backend, backend.CreateMaterial( "Faded boxes", new Color( 0.3f, 0.7f, 0.4f ) ), 120 );
			world.Add( new MeshObject( faded, Transform.Zero ) { Tint = new Color( 1, 1, 1, 0.5f ) } );

			var view = new RenderView { Name = "Faded", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( bool gpuDriven, bool faded, DrawCensus census = null )
			{
				backend.GpuDriven = gpuDriven;
				backend.GpuDrivenFaded = faded;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				backend.Census = census;
				try
				{
					system.Render( world, view, target );
				}
				finally
				{
					backend.Census = null;
				}

				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			// A pipeline draws fast linked until its optimised link, made on threads at the lowest priority, replaces it, and the
			// two needn't draw the same pixels: each frame compared follows one that made its pipelines and a wait for their
			// optimised links, so it doesn't depend on when those threads ran
			byte[] Optimised( bool gpuDriven, bool faded, DrawCensus census = null )
			{
				Frame( gpuDriven, faded, census is null ? null : new DrawCensus() );
				Assert.IsTrue( Device.WaitForOptimisedPipelines( TimeSpan.FromSeconds( 60 ) ), "the optimised links are made" );
				return Frame( gpuDriven, faded, census );
			}

			var classic = Optimised( false, false );
			var before = new DrawCensus();
			Frame( true, false, before );
			var census = new DrawCensus();
			var driven = Optimised( true, true, census );

			Assert.IsTrue( before.Draws.Any( d => d.Path == DrawCensus.Path.Classic && d.Kind.Contains( "faded", StringComparison.Ordinal ) ), $"with the switch off, the faded boxes draw the classic way: {string.Join( ", ", before.Draws.Where( d => d.Path == DrawCensus.Path.Classic ).Select( d => $"{d.Pass} {d.Material} {d.Kind}" ) )}" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			byte[] Rgba8( byte[] halfs )
			{
				var rgba = new byte[Width * Height * 4];
				for ( int i = 0; i < rgba.Length; i++ ) rgba[i] = (byte)(MathF.Pow( Math.Clamp( (float)BitConverter.ToHalf( halfs, i * 2 ), 0, 1 ), 1 / 2.2f ) * 255 + 0.5f);
				return rgba;
			}

			Save( Rgba8( classic ), Width, Height, "faded-classic" );
			Save( Rgba8( driven ), Width, Height, "faded-gpudriven" );
			Assert.AreEqual( 0, Different( classic, driven ), "the classic pixels" );
			Assert.IsFalse( census.Draws.Any( d => d.Path == DrawCensus.Path.Classic && d.Material == "Faded boxes" ), $"nothing of the faded boxes draws the classic way: {string.Join( ", ", census.Draws.Where( d => d.Path == DrawCensus.Path.Classic ).Select( d => $"{d.Pass} {d.Kind}" ) )}" );
			Assert.IsTrue( census.Draws.Any( d => d.Path == DrawCensus.Path.Bucket && d.Pass.EndsWith( "Opaque Forward", StringComparison.Ordinal ) && d.BucketMaterials > 0 ), "the opaque pass draws buckets" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Fading objects dither from their pipelines' first links, not only once their optimised links replace them
	/// (<c>Device.OptimisesLinks</c> off keeps the first): about half of a half-faded box's pixels show what's behind it,
	/// drawn the classic way and GPU-driven.
	/// </summary>
	[TestMethod]
	public void FadedObjectsDitherFromTheirFirstLink()
	{
		Device.OptimisesLinks = false;
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = false, GpuDrivenMeshes = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "First link fade test" );

			// The ground alone, with the boxes solid, and with them half faded; no shadows, so the boxes change only their own pixels
			var (ground, _) = Boxes( backend, backend.CreateMaterial( "Faded test ground", new Color( 0.8f, 0.6f, 0.4f ) ), 0 );
			var (boxes, _) = Boxes( backend, backend.CreateMaterial( "Faded boxes", new Color( 0.3f, 0.7f, 0.4f ) ), 120 );
			RenderWorld World( float? alpha )
			{
				var world = new RenderWorld();
				world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
				world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
				world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
				world.Lighting.SunShadowCascades = 0;
				world.Add( new MeshObject( ground, Transform.Zero ) );
				if ( alpha is { } a ) world.Add( new MeshObject( boxes, Transform.Zero ) { Tint = new Color( 1, 1, 1, a ) } );
				return world;
			}

			var (bare, solid, faded) = (World( null ), World( 1 ), World( 0.5f ));
			var view = new RenderView { Name = "First link fade", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( RenderWorld world, bool gpuDriven )
			{
				backend.GpuDriven = gpuDriven;
				backend.GpuDrivenFaded = gpuDriven;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			static bool Same( byte[] a, byte[] b, int pixel ) => a.AsSpan( pixel * 8, 8 ).SequenceEqual( b.AsSpan( pixel * 8, 8 ) );

			foreach ( var gpuDriven in (bool[])[false, true] )
			{
				var (behind, whole, dithered) = (Frame( bare, gpuDriven ), Frame( solid, gpuDriven ), Frame( faded, gpuDriven ));
				int covered = 0, holes = 0;
				for ( int p = 0; p < Width * Height; p++ )
				{
					if ( Same( whole, behind, p ) ) continue;
					covered++;
					if ( Same( dithered, behind, p ) ) holes++;
				}

				var path = gpuDriven ? "GPU-driven" : "classic";
				Assert.IsTrue( covered > 2000, $"{path}: the boxes cover {covered} pixels" );
				Assert.AreEqual( 0.5, holes / (double)covered, 0.15, $"{path}: {holes} of the {covered} pixels the boxes cover show what's behind them" );
			}

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// The translucent list draws GPU-driven in its sorted order: three overlapping rows of half-faded boxes, alpha-blended,
	/// additive, alpha-blended back to front (two pipelines), blend as the classic path blends them, so the far and near
	/// alpha-blended rows don't share a multi-draw around the additive one; nothing of theirs draws the classic way.
	/// </summary>
	[TestMethod]
	public void TranslucentListDrawsGpuDrivenInOrder()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = false, GpuDrivenMeshes = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven translucent test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			// A built translucent material (complex.shader, F_TRANSLUCENT), as the game's draw: a surface
			const string translucentPath = "materials/dev/primary_white_trans.vmat";
			if ( ContentFiles.FindGame().Find( translucentPath ) is null ) Assert.Inconclusive( $"{translucentPath} isn't built" );
			GpuMaterial Translucent( string name, Color color ) => backend.LoadMaterial( translucentPath, new Dictionary<string, float[]> { ["g_vColorTint"] = [color.r, color.g, color.b, 0] }, name );

			var red = Translucent( "Translucent red", new Color( 0.9f, 0.2f, 0.1f ) );
			// Additive: another pipeline, whose blend doesn't commute with the alpha blend on either side of it
			var blue = backend.LoadMaterial( "materials/dev/primary_green_additive.vmat", new Dictionary<string, float[]> { ["g_vColorTint"] = [0.1f, 0.3f, 0.9f, 0] }, "Translucent blue" );
			Assert.IsTrue( red.IsTranslucent, $"{translucentPath} draws translucent" );
			// As overrides, which say the object draws translucent (a mesh made here takes it from none of its draws)
			foreach ( var (material, y) in new[] { (red, 0f), (blue, -50f), (red, -100f) } )
			{
				var (boxes, _) = Boxes( backend, backend.CreateMaterial( "Translucent test boxes", Color.White ), y );
				world.Add( new MeshObject( boxes, Transform.Zero ) { MaterialOverride = material, Tint = new Color( 1, 1, 1, 0.5f ) } );
			}

			var view = new RenderView { Name = "Translucent", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( bool gpuDriven, bool translucent, DrawCensus census = null )
			{
				backend.GpuDriven = gpuDriven;
				backend.GpuDrivenTranslucent = translucent;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				backend.Census = census;
				try
				{
					system.Render( world, view, target );
				}
				finally
				{
					backend.Census = null;
				}

				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			Frame( false, false );
			var classic = Frame( false, false );
			var before = new DrawCensus();
			var beforePixels = Frame( true, false, before );
			Frame( true, true );
			var census = new DrawCensus();
			var driven = Frame( true, true, census );
			byte[] Rgba8( byte[] halfs )
			{
				var rgba = new byte[Width * Height * 4];
				for ( int i = 0; i < rgba.Length; i++ ) rgba[i] = (byte)(MathF.Pow( Math.Clamp( (float)BitConverter.ToHalf( halfs, i * 2 ), 0, 1 ), 1 / 2.2f ) * 255 + 0.5f);
				return rgba;
			}

			Save( Rgba8( classic ), Width, Height, "translucent-classic" );
			Save( Rgba8( beforePixels ), Width, Height, "translucent-before" );
			Save( Rgba8( driven ), Width, Height, "translucent-gpudriven" );
			TestContext.WriteLine( $"before against classic {Different( beforePixels, classic )}, against GPU-driven {Different( beforePixels, driven )}" );

			bool Ours( DrawCensus.Draw d ) => d.Material is "Translucent red" or "Translucent blue";
			Assert.IsTrue( before.Draws.Any( d => d.Path == DrawCensus.Path.Classic && Ours( d ) ), $"with the switch off, the boxes draw the classic way: {string.Join( ", ", before.Draws.Select( d => $"{d.Path} {d.Pass} {d.Material} {d.Kind}" ) )}" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.AreEqual( 0, Different( classic, driven ), "the classic pixels: blended in the list's order" );
			Assert.IsFalse( census.Draws.Any( d => d.Path == DrawCensus.Path.Classic && Ours( d ) ), $"nothing of the boxes draws the classic way: {string.Join( ", ", census.Draws.Where( d => d.Path == DrawCensus.Path.Classic ).Select( d => $"{d.Pass} {d.Material} {d.Kind}" ) )}" );
			var translucentCalls = census.Draws.Count( d => d.Path == DrawCensus.Path.Bucket && d.Pass.EndsWith( "Translucent Forward", StringComparison.Ordinal ) );
			Assert.IsTrue( translucentCalls >= 3, $"the rows draw in order, alpha, additive, alpha, so in three multi-draws at least: {translucentCalls}" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A shader with no variant beside it (a package's) gets one from the library's compiler: none while it compiles,
	/// then the compiled one, cached, so another library loads it without compiling.
	/// </summary>
	[TestMethod]
	public void PackageShaderVariantCompilesAtLoad()
	{
		var files = ShaderFileTests.Files;
		var compiled = files.Find( "shaders/depth_only.gpudriven.shader" );
		if ( compiled is null ) Assert.Inconclusive( "depth_only has no GPU-driven variant: ShaderCompiler -gpudriven" );

		var folder = Path.Combine( Path.GetTempPath(), $"borealis-gpudriven-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( folder, "package", "shaders" ) );
		File.Copy( files.Find( "shaders/depth_only.shader" ), Path.Combine( folder, "package", "shaders", "depth_only.shader_c" ) );

		try
		{
			using var started = new ManualResetEventSlim();
			using var go = new ManualResetEventSlim();
			var compiles = 0;

			using ( var library = new ShaderLibrary( Device, new ContentFiles( Path.Combine( folder, "package" ) ) ) )
			{
				library.GpuDrivenCache = Path.Combine( folder, "cache" );
				library.GpuDrivenCompiler = ( source, output ) =>
				{
					Interlocked.Increment( ref compiles );
					started.Set();
					go.Wait( 10000 );
					Directory.CreateDirectory( Path.GetDirectoryName( output ) );
					File.Copy( compiled, output );
					return true;
				};

				var shader = library.Load( "shaders/depth_only.shader" );
				Assert.IsNull( library.GpuDriven( shader ), "none while it compiles" );
				Assert.IsTrue( started.Wait( 10000 ) && library.GpuDrivenPending( shader ), "compiling" );
				Assert.IsNull( library.GpuDriven( shader ), "still none" );
				go.Set();

				ShaderFile variant = null;
				for ( int i = 0; i < 1000 && variant is null; i++ )
				{
					variant = library.GpuDriven( shader );
					if ( variant is null ) Thread.Sleep( 10 );
				}

				Assert.IsNotNull( variant, "the compiled variant" );
				Assert.IsFalse( library.GpuDrivenPending( shader ) );
				Assert.AreEqual( 1, compiles, "compiled once" );
			}

			using ( var library = new ShaderLibrary( Device, new ContentFiles( Path.Combine( folder, "package" ) ) ) )
			{
				library.GpuDrivenCache = Path.Combine( folder, "cache" );
				library.GpuDrivenCompiler = ( _, _ ) => throw new AssertFailedException( "the cached variant compiled again" );
				Assert.IsNotNull( library.GpuDriven( library.Load( "shaders/depth_only.shader" ) ), "from the cache" );

				// Another compiler version's variants go
				var stale = Path.Combine( folder, "cache", "depth_only.0000.ffffffff.gpudriven.shader_c" );
				File.WriteAllText( stale, "" );
				library.PruneGpuDrivenCache();
				Assert.IsFalse( File.Exists( stale ), "another version's variant is pruned" );
				Assert.AreEqual( 1, Directory.GetFiles( Path.Combine( folder, "cache" ) ).Length, "this version's variant stays" );
			}
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Shadow views cull meshlets (<see cref="BackendDevice.ShadowMeshlets"/>): static models' largest draws as aggregates (a
	/// large plane, trashcans scaled up on it) and static meshes (resident trashcans), under the sun's two cascades and, apart, round a point light's cube, draw the
	/// pixels they do without, each call culling its own, from plans and from the resident cull, while the shadow views
	/// rasterise fewer triangles.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ShadowViewsDrawOnlyTheMeshletsInside( bool pointLight )
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) Borealis.Backend.Meshlets.MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );

		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var plane = backend.LoadModel( "models/dev/plane_large.vmdl" );
		var can = backend.LoadModel( "models/citizen_props/trashcan01.vmdl" );
		if ( plane is null || can is null ) Assert.Inconclusive( "models/dev/plane_large.vmdl or models/citizen_props/trashcan01.vmdl isn't built" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Shadow meshlets test" );
			if ( backend.MeshletStore is null || backend.MeshletCullShader is null ) Assert.Inconclusive( "no meshlets: meshopt or borealis_meshlet_cull_cs is missing" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = pointLight ? Color.Black : new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = pointLight ? 0 : 2;

			// A model's largest draw in a material of the test's, its depth drawn with depth_only (positions alone), as an
			// aggregate of fragments placed and scaled
			AggregateObject Aggregate( RenderMesh model, Color colour, (Vector3 Position, float Scale)[] placed )
			{
				var draw = model.DrawsForLod( 0 ).ToArray().MaxBy( d => d.IndexCount ) with { Material = backend.CreateMaterial( $"Shadow meshlets {colour}", colour ), ShadowFastPath = true };
				var baseVertex = ((IBackendGeometry)model.Geometry).For( draw.Mesh, draw.DrawCall ).BaseVertex;
				var radius = model.Bounds.Size.Length;
				var fragments = placed.Select( ( p, i ) => new AggregateObject.FragmentDraw
				{
					Transform = Matrix3x4.From( new Transform( p.Position, Rotation.FromYaw( i * 50 ), p.Scale ) ),
					BoundsMin = p.Position - radius * p.Scale,
					BoundsMax = p.Position + radius * p.Scale,
					DrawTint = Vector3.One,
					Args = new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = (uint)draw.IndexCount, InstanceCount = 1, FirstIndex = (uint)draw.StartIndex, BaseVertex = baseVertex, FirstInstance = (uint)i },
				} ).ToArray();
				return new AggregateObject( new RenderMesh( model.Geometry, model.Bounds, [[draw]] ), fragments ) { IsStatic = true };
			}

			var light = new Vector3( 300, 0, 400 );
			world.Add( Aggregate( plane, new Color( 0.5f, 0.5f, 0.5f ), [(new Vector3( 300, 0, 0 ), 6000 / MathF.Max( plane.Bounds.Size.x, 1 ))] ) );
			world.Add( Aggregate( can, new Color( 0.7f, 0.6f, 0.5f ), [(light + new Vector3( 0, 0, -300 ), 8), (new Vector3( 900, -500, 0 ), 6), (new Vector3( 1200, 400, 0 ), 6), (new Vector3( -100, 600, 0 ), 5), (new Vector3( 500, 300, 0 ), 4)] ) );
			if ( pointLight ) world.Add( new LightObject( LightObject.LightKind.Point, new Transform( light ) ) { Radius = 1500, Color = new Color( 40, 40, 40 ) } );

			// Static meshes of the trashcan's draw (resident meshes), whose draws ask for meshlets for the shadow views
			var canDraw = can.DrawsForLod( 0 ).ToArray().MaxBy( d => d.IndexCount ) with { Material = backend.CreateMaterial( "Shadow meshlets mesh", new Color( 0.4f, 0.6f, 0.7f ) ), ShadowFastPath = true };
			var canMesh = new RenderMesh( can.Geometry, can.Bounds, [[canDraw]] );
			foreach ( var at in (Vector3[])[new( 550, 150, 0 ), new( 50, -200, 0 ), new( 700, -150, 0 )] ) world.Add( new MeshObject( canMesh, new Transform( at, Rotation.FromYaw( at.x ), 6 ) ) { IsStatic = true } );

			var view = new RenderView { Name = "Shadow meshlets test", FieldOfView = 70, ZNear = 4, ZFar = 6000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -400, 300 );
			view.Rotation = Rotation.LookAt( new Vector3( 1000, 450, -400 ) );

			byte[] Frame( bool meshlets, bool residentCull, DrawCensus census = null )
			{
				backend.ShadowMeshlets = meshlets;
				backend.GpuDrivenResidentCull = residentCull;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				backend.Census = census;
				try
				{
					system.Render( world, view, target );
				}
				finally
				{
					backend.Census = null;
				}

				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			// The meshlets built and published
			for ( int i = 0; i < 300 && (i < 4 || backend.MeshletStore.Pending > 0); i++ )
			{
				Frame( false, residentCull: true );
				if ( backend.MeshletStore.Pending > 0 ) Thread.Sleep( 10 );
			}

			Assert.AreEqual( 0, backend.MeshletStore.Pending, "the meshlets were built" );
			Assert.IsTrue( backend.MeshletStore.Published > 0, $"and published: {backend.MeshletReport}" );

			// A census frame (no plans: each call buckets and culls its own): the shadow views' triangles and the pixels
			var report = "";
			(long Triangles, long Compacted, byte[] Pixels) Census( bool meshlets )
			{
				Frame( meshlets, residentCull: false );
				var census = new DrawCensus();
				var pixels = Frame( meshlets, residentCull: false, census: census );
				census.Resolve( Device );
				var shadow = census.Draws.Where( d => d.Pass.Contains( pointLight ? "LightObject" : "CSM", StringComparison.Ordinal ) ).ToArray();
				Assert.IsTrue( shadow.Length > 0 && shadow.All( d => d.Triangles >= 0 ), "the shadow views drew, and their multi-draws were read back" );
				report += $" {(meshlets ? "with" : "without")}: " + string.Join( "; ", shadow.Select( d => $"{d.Pass} {d.Kind} {d.Triangles}" ) );
				return (shadow.Sum( d => d.Triangles ), shadow.Where( d => d.Kind?.Contains( "compacted", StringComparison.Ordinal ) == true ).Sum( d => d.Triangles ), pixels);
			}

			var whole = Census( false );
			var culled = Census( true );

			// From plans, each call culling its own, then from the resident cull, without and with
			Frame( false, residentCull: false );
			var ownWhole = Frame( false, residentCull: false );
			Frame( true, residentCull: false );
			var hits = backend.GpuDrivenPlanHits;
			var ownCulled = Frame( true, residentCull: false );
			var planned = backend.GpuDrivenPlanHits > hits;
			for ( int i = 0; i < 3; i++ ) Frame( false, residentCull: true );
			var residentWhole = Frame( false, residentCull: true );
			for ( int i = 0; i < 3; i++ ) Frame( true, residentCull: true );
			var calls = backend.ResidentCullCalls;
			var residentCulled = Frame( true, residentCull: true );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.IsTrue( culled.Compacted > 0 && culled.Triangles < whole.Triangles * 3 / 4, $"the shadow views drew fewer triangles with meshlets, some from kept meshlets ({culled.Triangles:N0} against {whole.Triangles:N0}):{report}" );
			Assert.AreEqual( 0, Different( whole.Pixels, ownWhole ), "the frames without draw the same each way" );
			Assert.AreEqual( 0, Different( whole.Pixels, residentWhole ), "and from the resident cull" );
			Assert.AreEqual( 0, Different( whole.Pixels, culled.Pixels ), "meshlet-culled shadows draw the same pixels" );
			Assert.IsTrue( planned, "calls drew from plans" );
			Assert.AreEqual( 0, Different( ownWhole, ownCulled ), "and from plans" );
			Assert.IsTrue( backend.ResidentCullCalls > calls, "calls drew from the resident cull" );
			Assert.AreEqual( 0, Different( residentWhole, residentCulled ), "and from the resident cull" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Two-phase Hi-Z occlusion (<see cref="BackendDevice.HiZOcclusion"/>): trashcans hidden behind a large one aren't drawn
	/// once forward has seen them hidden (the second phase culls them against the Hi-Z of the first), and a turn of the
	/// camera that shows them draws them that same frame. Each frame draws the pixels it does without, each call culling its
	/// own and from the resident cull, with the meshlet pass culling both phases' fragments.
	/// </summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void HiZCullsHiddenFragmentsAndDrawsShownOnes( bool residentCull, bool meshlets )
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) Borealis.Backend.Meshlets.MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );

		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, GpuDrivenResidentCull = residentCull, Meshlets = meshlets, HiZMinFragments = 0 };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		if ( backend.HiZShader is null ) Assert.Inconclusive( "borealis_hiz_cs isn't built, or pass modules are off" );
		var plane = backend.LoadModel( "models/dev/plane_large.vmdl" );
		var can = backend.LoadModel( "models/citizen_props/trashcan01.vmdl" );
		if ( plane is null || can is null ) Assert.Inconclusive( "models/dev/plane_large.vmdl or models/citizen_props/trashcan01.vmdl isn't built" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Hi-Z test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			AggregateObject Aggregate( RenderMesh model, Color colour, (Vector3 Position, float Scale)[] placed )
			{
				var draw = model.DrawsForLod( 0 ).ToArray().MaxBy( d => d.IndexCount ) with { Material = backend.CreateMaterial( $"Hi-Z {colour}", colour ), ShadowFastPath = true };
				var baseVertex = ((IBackendGeometry)model.Geometry).For( draw.Mesh, draw.DrawCall ).BaseVertex;
				var fragments = placed.Select( ( p, i ) =>
				{
					var transform = new Transform( p.Position, Rotation.FromYaw( i * 50 ), p.Scale );
					var bounds = model.Bounds.Transform( transform );
					return new AggregateObject.FragmentDraw
					{
						Transform = Matrix3x4.From( transform ),
						BoundsMin = bounds.Mins,
						BoundsMax = bounds.Maxs,
						DrawTint = Vector3.One,
						Args = new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = (uint)draw.IndexCount, InstanceCount = 1, FirstIndex = (uint)draw.StartIndex, BaseVertex = baseVertex, FirstInstance = (uint)i },
					};
				} ).ToArray();
				return new AggregateObject( new RenderMesh( model.Geometry, model.Bounds, [[draw]] ), fragments ) { IsStatic = true };
			}

			// A large trashcan in front of five small ones, and one to its side; a ground plane
			var size = can.Bounds.Size;
			var wall = 600 / MathF.Max( MathF.Max( size.x, size.y ), 1 );
			var small = 60 / MathF.Max( MathF.Max( size.x, size.y ), 1 );
			world.Add( Aggregate( plane, new Color( 0.5f, 0.5f, 0.5f ), [(new Vector3( 300, 0, 0 ), 6000 / MathF.Max( plane.Bounds.Size.x, 1 ))] ) );
			world.Add( Aggregate( can, new Color( 0.7f, 0.6f, 0.5f ), [(new Vector3( 0, 0, 0 ), wall), (new Vector3( 0, 900, 0 ), wall / 2)] ) );
			var hidden = Aggregate( can, new Color( 0.3f, 0.7f, 0.4f ), [(new Vector3( 500, -80, 0 ), small), (new Vector3( 500, 0, 0 ), small), (new Vector3( 500, 80, 0 ), small), (new Vector3( 650, -40, 0 ), small), (new Vector3( 650, 40, 0 ), small)] );
			world.Add( hidden );

			var view = new RenderView { Name = "Hi-Z test", FieldOfView = 70, ZNear = 4, ZFar = 6000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			void Behind()
			{
				view.Position = new Vector3( -1200, 0, 120 );
				view.Rotation = Rotation.LookAt( new Vector3( 1, 0, -0.02f ) );
			}

			void Beside()
			{
				view.Position = new Vector3( 560, -900, 200 );
				view.Rotation = Rotation.LookAt( new Vector3( 0, 1, -0.2f ) );
			}

			byte[] Frame( bool hiz )
			{
				backend.HiZOcclusion = hiz;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Resolved );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Resolved, ImageLayout.CopySource );
				return pixels;
			}

			// The meshlets built and published, so both phases' fragments go through the meshlet pass
			Behind();
			for ( int i = 0; i < 300 && (i < 4 || backend.MeshletStore is { Pending: > 0 }); i++ )
			{
				Frame( false );
				if ( backend.MeshletStore is { Pending: > 0 } ) Thread.Sleep( 10 );
			}

			// Without: from behind the large trashcan, then turned to beside the small ones
			Frame( false );
			var behindWithout = Frame( false );
			var behindAgain = Frame( false );
			Beside();
			var besideWithout = Frame( false );

			// With: forward sees the small ones hidden, and the next frames cull them; then the turn shows them at once, and
			// the turn back hides them again
			Behind();
			var tested = backend.HiZTested;
			for ( int i = 0; i < 3; i++ ) Frame( true );
			var behindWith = Frame( true );
			var behindCounts = backend.HiZCounts();
			Beside();
			var besideWith = Frame( true );
			var besideCounts = backend.HiZCounts();
			Behind();
			var backWith = Frame( true );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.IsTrue( backend.HiZTested > tested, "prepasses tested candidates against a Hi-Z" );
			Assert.IsTrue( behindCounts.Occluded >= 5, $"the hidden trashcans were culled (candidates {behindCounts.Candidates}, occluded {behindCounts.Occluded})" );
			Assert.AreEqual( 0, Different( behindWithout, behindAgain ), "frames without are the same each time" );
			Assert.IsTrue( Same( behindWithout, behindWith ), $"and the frame is the same ({Differences( behindWithout, behindWith )})" );
			Assert.IsTrue( besideCounts.Candidates >= 5 && besideCounts.Occluded == 0, $"turned, they were candidates and none was culled: nothing in front of them drew first (candidates {besideCounts.Candidates}, occluded {besideCounts.Occluded})" );
			Assert.IsTrue( Same( besideWithout, besideWith ), $"the frame that shows them draws them ({Differences( besideWithout, besideWith )})" );
			Assert.IsTrue( Same( behindWithout, backWith ), $"and turned back, the same again ({Differences( behindWithout, backWith )})" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A model whose meshlets and levels the resource compiler cooked (<see cref="Borealis.Source2.Models.MeshletCooker"/>: its
	/// draws' indices in meshlet order, the records and levels in the mesh) and one whose meshlets and levels are made at load,
	/// as aggregates side by side: the cooked one's ranges are taken as they are and the other's made, every range publishes,
	/// and the frame is the one drawn without meshlets and the one drawn with nothing taken cooked; with the compute pass and
	/// with task and mesh shaders, without generated LODs and with them.
	/// </summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void CookedAndBuiltMeshletsBothDraw( bool meshTasks, bool lods )
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) Borealis.Backend.Meshlets.MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );
		if ( !Borealis.Backend.Meshlets.MeshOptimizer.Available ) Assert.Inconclusive( "meshopt isn't built" );
		if ( meshTasks && !Device.Capabilities.MeshShaders ) Assert.Inconclusive( "The device has no mesh shaders" );

		byte[] Frame( bool meshlets, bool cookedTaken, out Borealis.Backend.Meshlets.MeshletStore store, out Borealis.Backend.Lods.LodStore lodStore )
		{
			lodStore = null;
			using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, Meshlets = meshlets, MeshletTasks = meshTasks, GeneratedLods = lods };
			foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
			{
				if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
			}

			var plane = backend.LoadModel( "models/dev/plane_large.vmdl" );
			if ( plane is null || backend.Content.TryRead( "models/citizen_props/trashcan01.vmdl" ) is not { } data ) { Assert.Inconclusive( "models/dev/plane_large.vmdl or models/citizen_props/trashcan01.vmdl isn't built" ); store = null; return null; }

			// The same model twice: as compiled, and cooked as the resource compiler cooks an aggregate's meshes
			var file = Borealis.Source2.Models.ModelFile.Read( data, backend.Content.TryRead );
			var cut = Borealis.Backend.Meshlets.MeshletBuilder.Cut( Borealis.Backend.Meshlets.MeshletConfig.Default );
			var lodConfig = Borealis.Backend.Lods.LodConfig.Default;
			var levelCut = Borealis.Backend.Lods.LodStore.Cut( lodConfig );
			var cookedFile = file.WithMeshes( file.Meshes.Select( m => Borealis.Source2.Models.MeshletCooker.Cook( m, 64, 124, cut, Borealis.Backend.Lods.LodStore.Cooked( lodConfig ), levelCut ) ?? m ).ToArray() );
			Assert.IsTrue( cookedFile.Meshes.Any( m => m.IndexBuffers.Any( b => b.Meshlets is not null ) ), "the model cooks" );
			Assert.IsTrue( file.Meshes.All( m => m.IndexBuffers.All( b => b.Meshlets is null ) ), "and as compiled has no cooked meshlets" );
			var built = BackendModel.Create( backend, file );
			var cooked = BackendModel.Create( backend, cookedFile );

			var previousHost = RenderHost.Current;
			RenderHost.Current = new BackendRenderHost( backend );
			Borealis.Backend.Meshlets.MeshletStore.UseCooked = cookedTaken;
			try
			{
				using var system = new RenderSystem { ParallelRecording = false };
				using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Cooked meshlets test" );
				if ( meshlets && (backend.MeshletStore is null || backend.MeshletCullShader is null) ) Assert.Inconclusive( "no meshlets: meshopt or borealis_meshlet_cull_cs is missing" );
				if ( meshlets && meshTasks && backend.MeshletIds is null ) Assert.Inconclusive( "borealis_meshlet_ids isn't built" );

				var world = new RenderWorld();
				world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
				world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
				world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
				world.Lighting.SunShadowCascades = 0;

				AggregateObject Aggregate( RenderMesh model, Color colour, (Vector3 Position, float Scale)[] placed )
				{
					var draw = model.DrawsForLod( 0 ).ToArray().MaxBy( d => d.IndexCount ) with { Material = backend.CreateMaterial( $"Cooked {colour}", colour ), ShadowFastPath = true };
					var baseVertex = ((IBackendGeometry)model.Geometry).For( draw.Mesh, draw.DrawCall ).BaseVertex;
					var fragments = placed.Select( ( p, i ) =>
					{
						var transform = new Transform( p.Position, Rotation.FromYaw( i * 50 ), p.Scale );
						var bounds = model.Bounds.Transform( transform );
						return new AggregateObject.FragmentDraw
						{
							Transform = Matrix3x4.From( transform ),
							BoundsMin = bounds.Mins,
							BoundsMax = bounds.Maxs,
							DrawTint = Vector3.One,
							Args = new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = (uint)draw.IndexCount, InstanceCount = 1, FirstIndex = (uint)draw.StartIndex, BaseVertex = baseVertex, FirstInstance = (uint)i },
						};
					} ).ToArray();
					return new AggregateObject( new RenderMesh( model.Geometry, model.Bounds, [[draw]] ), fragments ) { IsStatic = true };
				}

				// Cooked trashcans on the left, built ones on the right, on a ground plane
				var size = cooked.Bounds.Size;
				var scale = 160 / MathF.Max( MathF.Max( size.x, size.y ), 1 );
				world.Add( Aggregate( plane, new Color( 0.5f, 0.5f, 0.5f ), [(new Vector3( 300, 0, 0 ), 6000 / MathF.Max( plane.Bounds.Size.x, 1 ))] ) );
				world.Add( Aggregate( cooked, new Color( 0.7f, 0.6f, 0.5f ), [(new Vector3( 0, -200, 0 ), scale), (new Vector3( 250, -320, 0 ), scale * 0.7f)] ) );
				world.Add( Aggregate( built, new Color( 0.3f, 0.7f, 0.4f ), [(new Vector3( 0, 200, 0 ), scale), (new Vector3( 250, 320, 0 ), scale * 0.7f)] ) );

				var view = new RenderView { Name = "Cooked meshlets test", FieldOfView = 70, ZNear = 4, ZFar = 6000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
				view.Position = new Vector3( -700, 0, 260 );
				view.Rotation = Rotation.LookAt( new Vector3( 1, 0, -0.3f ) );

				byte[] Render()
				{
					backend.BeginFrame();
					RenderHost.Current.NextFrame();
					system.Render( world, view, target );
					Device.WaitIdle();
					var (texture, layout) = BackendDevice.Unwrap( target.Resolved );
					var pixels = ReadBack( texture, layout );
					BackendDevice.SetLayout( target.Resolved, ImageLayout.CopySource );
					return pixels;
				}

				// Every range's levels and meshlets made or taken, uploaded and published before the frame that's kept
				bool Pending() => backend.MeshletStore is { Pending: > 0 } || backend.LodStore is { Pending: > 0 };
				for ( int i = 0; i < 300 && (i < 4 || Pending()); i++ )
				{
					Render();
					if ( Pending() ) Thread.Sleep( 10 );
				}

				for ( int i = 0; i < 6; i++ ) Render();
				store = backend.MeshletStore;
				lodStore = backend.LodStore;
				return Render();
			}
			finally
			{
				Device.WaitIdle();
				RenderHost.Current = previousHost;
				Borealis.Backend.Meshlets.MeshletStore.UseCooked = true;
			}
		}

		var with = Frame( true, true, out var meshletStore, out var lodStore );
		var made = Frame( true, false, out var madeStore, out _ );
		var without = Frame( false, true, out _, out _ );
		Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		Assert.IsNotNull( meshletStore, "meshlets were on" );
		Assert.AreEqual( 0, meshletStore.Pending, "every range was built or taken" );
		Assert.IsTrue( meshletStore.CookedHits > 0, $"the cooked model's ranges were taken as cooked ({meshletStore.Published} published)" );
		Assert.IsTrue( meshletStore.Published > meshletStore.CookedHits, $"and the other model's built ({meshletStore.Published} published, {meshletStore.CookedHits} cooked)" );
		Assert.AreEqual( meshletStore.Requested, meshletStore.Published, "every range asked for published" );
		Assert.AreEqual( 0, meshletStore.CookedMisses, "and none of the cooked model's built at load" );
		Assert.AreEqual( 0, madeStore.CookedHits, "with cooked meshlets left out, none is taken" );
		if ( lods )
		{
			Assert.IsNotNull( lodStore, "generated LODs were on" );
			Assert.IsTrue( lodStore.CookedHits > 0, $"the cooked model's levels were taken as cooked ({lodStore.Requested} asked for)" );
			Assert.IsTrue( lodStore.Requested > lodStore.CookedHits, $"and the other model's made ({lodStore.Requested} asked for, {lodStore.CookedHits} cooked)" );
			Assert.AreEqual( 0, lodStore.CookedMisses, "none of the cooked model's made at load" );
			Assert.IsTrue( lodStore.Levels > 0, $"levels were published ({lodStore.Published} ranges)" );
		}

		Assert.IsTrue( Same( with, without ), $"both draw what they draw without meshlets ({Differences( with, without )})" );
		Assert.IsTrue( Same( with, made ), $"and what they draw with everything made at load ({Differences( with, made )})" );
	}

	/// <summary>
	/// Four boxes along x at <paramref name="y"/> and a fifth beyond the far plane, which culling drops, in world space in
	/// one mesh, and a fragment per box.
	/// </summary>
	static (RenderMesh Mesh, AggregateObject.FragmentDraw[] Fragments) Boxes( BackendDevice backend, GpuMaterial material, float y )
	{
		const int count = 5;
		var vertices = new List<MeshVertex>();
		var indices = new List<uint>();
		var fragments = new AggregateObject.FragmentDraw[count];
		for ( int b = 0; b < count; b++ )
		{
			var centre = b < 4 ? new Vector3( b * 120 - 180, y, 40 ) : new Vector3( 0, y, -6000 );
			var firstIndex = indices.Count;
			Box( vertices, indices, centre, 40 );
			fragments[b] = new AggregateObject.FragmentDraw
			{
				Transform = Matrix3x4.Identity,
				BoundsMin = centre - 40,
				BoundsMax = centre + 40,
				DrawTint = Vector3.One,
				Args = new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = (uint)(indices.Count - firstIndex), InstanceCount = 1, FirstIndex = (uint)firstIndex, BaseVertex = 0, FirstInstance = (uint)b },
			};
		}

		return (BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), material, "Boxes" ), fragments);
	}

	static void Box( List<MeshVertex> vertices, List<uint> indices, Vector3 centre, float half )
	{
		Vector3[] normals = [Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down];
		foreach ( var n in normals )
		{
			var u = MathF.Abs( n.z ) > 0.5f ? Vector3.Forward : Vector3.Cross( Vector3.Up, n );
			var v = Vector3.Cross( n, u );
			var first = (uint)vertices.Count;
			vertices.Add( new MeshVertex( centre + (n - u - v) * half, n, new Vector2( 0, 1 ) ) );
			vertices.Add( new MeshVertex( centre + (n + u - v) * half, n, new Vector2( 1, 1 ) ) );
			vertices.Add( new MeshVertex( centre + (n + u + v) * half, n, new Vector2( 1, 0 ) ) );
			vertices.Add( new MeshVertex( centre + (n - u + v) * half, n, new Vector2( 0, 0 ) ) );
			indices.AddRange( [first, first + 1, first + 2, first, first + 2, first + 3] );
		}
	}

	/// <summary>
	/// Whether two RGBA16F frames show the same: no channel differs by more than 0.002 and at most 16 pixels differ at all.
	/// A prepass drawn in two phases draws its ids in another order, and where two triangles meet at a sample's depth
	/// the other may win: a few pixels a half-float step apart (4 in <see cref="HiZCullsHiddenFragmentsAndDrawsShownOnes"/>
	/// with nothing culled), where a fragment culled wrongly or drawn a frame late changes whole pixels.
	/// </summary>
	static bool Same( byte[] a, byte[] b )
	{
		var (pixels, largest) = (0, 0f);
		for ( int p = 0; p + 8 <= a.Length; p += 8 )
		{
			var most = 0f;
			for ( int c = 0; c < 4; c++ ) most = MathF.Max( most, MathF.Abs( (float)BitConverter.ToHalf( a, p + c * 2 ) - (float)BitConverter.ToHalf( b, p + c * 2 ) ) );
			if ( most > 0 ) pixels++;
			largest = MathF.Max( largest, most );
		}

		return largest <= 0.002f && pixels <= 16;
	}

	/// <summary>Where two RGBA16F frames differ: pixels and the largest difference of a channel, and the first few.</summary>
	static string Differences( byte[] a, byte[] b )
	{
		var (pixels, largest, first) = (0, 0f, new List<string>());
		for ( int p = 0; p + 8 <= a.Length; p += 8 )
		{
			var most = 0f;
			for ( int c = 0; c < 4; c++ ) most = MathF.Max( most, MathF.Abs( (float)BitConverter.ToHalf( a, p + c * 2 ) - (float)BitConverter.ToHalf( b, p + c * 2 ) ) );
			if ( most == 0 ) continue;
			pixels++;
			largest = MathF.Max( largest, most );
			if ( first.Count < 6 ) first.Add( $"({p / 8 % Width},{p / 8 / Width}) {most:0.####}" );
		}

		return $"{pixels} pixels, largest {largest:0.####}: {string.Join( ", ", first )}";
	}

	static int Different( byte[] a, byte[] b )
	{
		Assert.AreEqual( a.Length, b.Length );
		var count = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			if ( a[i] != b[i] ) count++;
		}

		return count;
	}
}
