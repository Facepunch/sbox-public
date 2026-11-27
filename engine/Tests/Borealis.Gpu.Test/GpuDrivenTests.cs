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
public class GpuDrivenTests : RenderGpuTest
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
		RequireGpuDrivenDepth( backend );

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
			var perMaterial = Frame( true, out _, true );
			var cpuCulled = Frame( true, out _, false, gpuCulling: false );
			var uncompacted = Frame( true, out _, false, compaction: false );
			var fullVertices = Frame( true, out _, false, positions: false );
			var visible = Frame( true, out _, false, visibility: true );
			var visibleAgain = Frame( true, out _, false, visibility: true );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			CollectionAssert.AreEqual( Enumerable.Repeat( 5, Rows ).ToArray(), preparedFragments, "every fragment prepared, for the GPU to cull" );
			Assert.IsTrue( drivenBatches > Rows * 2, $"the static meshes drew GPU-driven with the aggregates ({drivenBatches} batches)" );
			Assert.AreEqual( 0, Different( classic, perMaterial ), "a bucket per material draws the classic pixels" );
			// GpuDrivenMaterialBuckets splits only buckets of compiled variants; these boxes are standard surfaces, so it binds the same
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

	/// <summary>
	/// Forward over only the triangles that own a sample, for fragments a wall mostly hides: one of more triangles than a
	/// compaction group scans at once (counted and copied in parts) and one of fewer (read once for its count and copy),
	/// each drawn from its owned triangles alone, the classic pixels.
	/// </summary>
	[TestMethod]
	public void MostlyHiddenFragmentsCompactTheirOwnedTriangles()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDrivenMeshes = true };
		RequireGpuDrivenDepth( backend );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Hidden compaction test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			// Two walls of quads facing the camera (both windings, so either faces it), 19,600 and 1,120 triangles, and a box
			// in front hiding the left three quarters of both (a quad's triangles share their first vertex, so a quad seen
			// owns all four)
			var vertices = new List<MeshVertex>();
			var indices = new List<uint>();
			var fragments = new List<AggregateObject.FragmentDraw>();
			void Fragment( Action build, Vector3 min, Vector3 max )
			{
				var firstIndex = indices.Count;
				build();
				fragments.Add( new AggregateObject.FragmentDraw
				{
					Transform = Matrix3x4.Identity,
					BoundsMin = min,
					BoundsMax = max,
					DrawTint = Vector3.One,
					Args = new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = (uint)(indices.Count - firstIndex), InstanceCount = 1, FirstIndex = (uint)firstIndex, BaseVertex = 0, FirstInstance = (uint)fragments.Count },
				} );
			}

			void Grid( float z0, float z1, int columns, int rows )
			{
				for ( int r = 0; r < rows; r++ )
				{
					for ( int c = 0; c < columns; c++ )
					{
						float y0 = -200 + 400f * c / columns, y1 = -200 + 400f * (c + 1) / columns;
						float za = z0 + (z1 - z0) * r / rows, zb = z0 + (z1 - z0) * (r + 1) / rows;
						var first = (uint)vertices.Count;
						vertices.Add( new MeshVertex( new Vector3( 300, y0, za ), Vector3.Backward, new Vector2( 0, 1 ) ) );
						vertices.Add( new MeshVertex( new Vector3( 300, y1, za ), Vector3.Backward, new Vector2( 1, 1 ) ) );
						vertices.Add( new MeshVertex( new Vector3( 300, y1, zb ), Vector3.Backward, new Vector2( 1, 0 ) ) );
						vertices.Add( new MeshVertex( new Vector3( 300, y0, zb ), Vector3.Backward, new Vector2( 0, 0 ) ) );
						indices.AddRange( [first, first + 1, first + 2, first, first + 2, first + 3, first, first + 2, first + 1, first, first + 3, first + 2] );
					}
				}
			}

			Fragment( () => Grid( 0, 150, 70, 70 ), new Vector3( 299, -200, 0 ), new Vector3( 301, 200, 150 ) );
			Fragment( () => Grid( 160, 260, 20, 14 ), new Vector3( 299, -200, 160 ), new Vector3( 301, 200, 260 ) );
			Fragment( () => Box( vertices, indices, new Vector3( 100, -108, 130 ), 150 ), new Vector3( -50, -258, -20 ), new Vector3( 250, 42, 280 ) );
			var triangles = indices.Count / 3;
			var mesh = BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), backend.CreateMaterial( "Hidden", new Color( 0.6f, 0.6f, 0.5f ) ), "Hidden" );
			world.Add( new AggregateObject( mesh, fragments.ToArray() ) );

			var view = new RenderView { Name = "Hidden compaction test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -300, 0, 130 );
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			byte[] Frame( bool gpuDriven, bool visibility, DrawCensus census = null )
			{
				backend.GpuDriven = gpuDriven;
				backend.GpuDrivenVisibility = visibility;
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

			var classic = Frame( false, false );
			Frame( true, true );
			var census = new DrawCensus();
			var visible = Frame( true, true, census );
			census.Resolve( Device );
			var visibleAgain = Frame( true, true );
			var compacted = census.Draws.Where( d => d.Kind?.Contains( "compacted", StringComparison.Ordinal ) == true ).Sum( d => d.Triangles );
			var kinds = string.Join( "; ", census.Draws.Select( d => $"{d.Pass} {d.Kind} {d.Triangles}" ) );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.IsTrue( compacted > 0 && compacted < triangles / 2, $"forward drew compacted triangles, fewer than half of the {triangles:N0}: {kinds}" );
			Assert.AreEqual( 0, Different( classic, visible ), "forward over only the triangles that own a sample draws the classic pixels" );
			Assert.AreEqual( 0, Different( classic, visibleAgain ), "and the next frame" );
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
		RequireGpuDrivenDepth( backend );

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
		RequireGpuDrivenDepth( backend );

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
		RequireGpuDrivenDepth( backend );

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

			var materials = new GpuMaterial[Rows];
			var aggregates = new AggregateObject[Rows];
			for ( int row = 0; row < Rows; row++ )
			{
				materials[row] = backend.CreateMaterial( $"Resident boxes {row}", new Color( 0.3f + row * 0.2f, 0.6f, 0.4f ) );
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

			backend.SetOnMaterial( materials[0], new Dictionary<string, float[]> { ["g_vColorTint"] = [0.9f, 0.2f, 0.2f, 0] }, null );
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
		RequireGpuDrivenDepth( backend );

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
	/// An instanced object's meshes (<see cref="InstancedMeshObject"/>) in an alpha-tested surface, two LODs of a colour each:
	/// past the switch distance both LODs draw dithered for the fade's length (<see cref="BackendDevice.LodFadeTime"/>), then
	/// the new one alone, as a device that switches at once draws it; and a mesh that changed LOD out of view comes back in
	/// its LOD at once.
	/// </summary>
	[TestMethod]
	public void InstancedMeshesCrossFadeTheirLods()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is null ) Assert.Inconclusive( "No game content" );
		if ( !File.Exists( Path.Combine( EngineFolders.NativeIn( Path.GetDirectoryName( core ) ), EngineFolders.Library( "slang" ) ) ) ) Assert.Inconclusive( "The engine's Slang isn't built" );

		var folder = Fixtures.Folder( "lod-fade" );
		var previousHost = RenderHost.Current;
		try
		{
			var path = Path.Combine( folder, "clip_tint.slang" );
			File.WriteAllText( path, "import sbox.surface;\n[Material]\npublic struct ClipTint : ISurface\n{\n"
				+ "\t[RenderState( \"AlphaTest\" )] bool Clip;\n"
				+ "\t[Default3( 1, 1, 1 )] float3 Tint;\n"
				+ "\tpublic Material Evaluate( SurfaceInput i )\n\t{\n\t\tMaterial m = Material.Init( i );\n\t\tm.Albedo = Tint;\n"
				+ "\t\tm.Opacity = 1.0;\n\t\treturn m;\n\t}\n}\n" );

			using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
			RequireGpuDrivenDepth( backend );
			RenderHost.Current = new BackendRenderHost( backend );
			var materials = new[] { new[] { 0.9f, 0.2f, 0.2f }, new[] { 0.2f, 0.9f, 0.2f } }
				.Select( tint => (SurfaceMaterial)backend.Surfaces.CreateMaterial( path, null, new Dictionary<string, float[]> { ["Clip"] = [1], ["Tint"] = tint } ) ).ToArray();
			Assert.IsTrue( materials.All( m => m is { DithersLodFade: true } ), "both LODs' materials dither" );

			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "LOD fade test" );
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 2;

			var vertices = new List<MeshVertex>();
			var indices = new List<uint>();
			Box( vertices, indices, Vector3.Zero, 10 );
			var single = BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), materials[0], "LOD fade box" );
			var draw = single.Draws[0];
			var model = new RenderMesh( single.Geometry, single.Bounds, [[draw], [draw with { Material = materials[1] }]], [0, 30] );
			var instances = new InstancedMeshObject { Mesh = model };
			instances.SetInstances( new[] { -80, -40, 0, 40, 80 }.Select( y => new Transform( new Vector3( 0, y, 0 ) ) ).ToArray(), default );
			world.Add( instances );

			// LOD 1 starts about 206 units from a box at this width and field of view
			var view = new RenderView { Name = "LOD fade test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };

			byte[] Frame()
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			byte[] Settled( int frames = 2 )
			{
				byte[] pixels = null;
				for ( int i = 0; i < frames; i++ ) pixels = Frame();
				return pixels;
			}

			void Look( float x, bool away = false )
			{
				view.Position = new Vector3( x, 0, 0 );
				view.Rotation = Rotation.LookAt( new Vector3( away ? -1 : 1, 0, 0 ) );
			}

			backend.LodFadeTime = 0;
			Look( -100 );
			var near = Settled( 6 );
			Look( -400 );
			var far = Settled( 6 );
			Assert.IsTrue( system.GetFeature<Borealis.Features.AggregateRenderFeature>().ResidentFor( system.MainPass ) is { MeshGroups.Count: > 0 }, "the instances draw resident" );
			Assert.IsTrue( Different( near, far ) > 0, "the two distances draw different LODs" );

			backend.LodFadeTime = 1;
			backend.LodFadeClock = 0;
			Look( -100 );
			Assert.AreEqual( 0, Different( near, Settled( 6 ) ), "nothing fades while no LOD changes" );

			Look( -400 );
			var start = Settled();
			Assert.IsTrue( Different( start, far ) > 0, "the old LOD still draws as the fade starts" );

			backend.LodFadeClock = 0.5;
			var mid = Settled();
			Assert.IsTrue( Different( mid, far ) > 0, "halfway, the old LOD still draws" );
			Assert.IsTrue( Different( mid, start ) > 0, "halfway, the new LOD draws more" );

			backend.LodFadeClock = 1;
			Assert.AreEqual( 0, Different( far, Settled() ), "the fade over, the new LOD alone" );

			Look( -100, away: true );
			Settled( 3 );
			Look( -100 );
			Assert.AreEqual( 0, Different( near, Settled() ), "a mesh that changed LOD out of view doesn't fade" );
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
	/// The resident cull (<see cref="BackendDevice.GpuDrivenResidentCull"/>): resident aggregates and a seeded field of
	/// boxes with three LODs, in the prepass, two cascades and forward, culled for every view in setup draw the pixels each
	/// call culling its own draws, serial and on workers; and after a box moves and a material changes, again.
	/// </summary>
	[TestMethod]
	public void ResidentCullDrawsWhatEachCallCulls()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
		RequireGpuDrivenDepth( backend );

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

			var material = backend.CreateMaterial( "Resident cull boxes", new Color( 0.5f, 0.6f, 0.4f ) );
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
			backend.SetOnMaterial( lodMaterials[1], new Dictionary<string, float[]> { ["g_vColorTint"] = [0.9f, 0.9f, 0.2f, 0] }, null );
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
	/// <see cref="BackendDevice.MergedMarks"/>: the resident prepass leaves its ids for the main view's own batches
	/// (a box moved every frame) to mark, in the main view and in its 3D skybox, which keeps its visibility passes. Merged and separate marks
	/// produce the same pixels, single-sampled and multisampled, serial and on workers, and with normals from the ids.
	/// Both worlds contain resident aggregates, a seeded field of boxes with three LODs, and a moving box.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MergedMarksDrawWhatAMarkACallDraws( bool idsNormals )
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, IdsNormals = idsNormals };
		RequireGpuDrivenDepth( backend );
		if ( backend.ResidentCullShader is null ) Assert.Inconclusive( "borealis_resident_cull_cs isn't compiled" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var single = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Merged marks test" );
			using var multi = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Merged marks test (4x)" );

			var vertices = new List<MeshVertex>();
			var indices = new List<uint>();
			Box( vertices, indices, Vector3.Zero, 10 );
			var lodMaterials = new[] { new Color( 0.9f, 0.2f, 0.2f ), new Color( 0.2f, 0.9f, 0.2f ), new Color( 0.2f, 0.2f, 0.9f ) }
				.Select( ( c, i ) => backend.CreateMaterial( $"Merged marks LOD {i}", c ) ).ToArray();
			var box = BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), lodMaterials[0], "Merged marks LOD box" );
			var draw = box.Draws[0];
			RenderMesh.Draw[][] lods = [[draw], [draw with { Material = lodMaterials[1] }], [draw with { Material = lodMaterials[2], IndexCount = 12 }]];
			var model = new RenderMesh( box.Geometry, box.Bounds, lods, [0, 30, 90] );
			var moving = BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), backend.CreateMaterial( "Merged marks moving", new Color( 0.9f, 0.8f, 0.3f ) ), "Merged marks moving box" );

			// A world of a resident row, a field of boxes and a box that moves, seen from (-400, 0, 200)
			(RenderWorld World, MeshObject Moving) Scene( int seed )
			{
				var world = new RenderWorld();
				world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
				world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
				world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
				world.Lighting.SunShadowCascades = 0;
				var (rowMesh, fragments) = Boxes( backend, backend.CreateMaterial( $"Merged marks row {seed}", new Color( 0.5f, 0.6f, 0.4f ) ), -120 );
				world.Add( new AggregateObject( rowMesh, fragments ) { IsStatic = true } );
				var random = new Random( seed );
				for ( int i = 0; i < 120; i++ )
				{
					var position = new Vector3( random.NextSingle() * 1600 - 200, random.NextSingle() * 1600 - 800, random.NextSingle() * 60 );
					world.Add( new MeshObject( model, new Transform( position, Rotation.FromYaw( random.NextSingle() * 360 ), 0.5f + random.NextSingle() * 2.5f ) ) { IsStatic = true } );
				}

				var mover = new MeshObject( moving, new Transform( new Vector3( 0, 0, 40 ), Rotation.Identity, 4 ) );
				world.Add( mover );
				return (world, mover);
			}

			// The skybox's scene seen from where the main world's is, its camera at (-400, 0, 200) there too: drawn where the main
			// world shows nothing
			var (skyWorld, skyMover) = Scene( 1234 );
			var (world, mover) = Scene( 4321 );
			world.Skybox3D = new Skybox3D( skyWorld ) { Scale = 16, Origin = new Vector3( -400 + 400 / 16f, 0, 200 - 200 / 16f ) };
			var view = new RenderView { Name = "Merged marks test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -400, 0, 200 );
			view.Rotation = Rotation.LookAt( new Vector3( 1, 0, -0.1f ) );

			// The same frames each way: the boxes move along the same path
			byte[] Frames( ViewTarget target, bool merged )
			{
				backend.MergedMarks = merged;
				byte[] pixels = null;
				for ( int i = 0; i < 8; i++ )
				{
					mover.Transform = mover.Transform.WithPosition( new Vector3( 100 + i * 30, 40 - i * 10, 60 ) );
					skyMover.Transform = skyMover.Transform.WithPosition( new Vector3( 300 + i * 30, 60 + i * 10, 40 ) );
					backend.BeginFrame();
					RenderHost.Current.NextFrame();
					system.Render( world, view, target );
					Device.WaitIdle();
					var color = target.Samples > 1 ? target.Resolved : target.Color;
					var (texture, layout) = BackendDevice.Unwrap( color );
					pixels = ReadBack( texture, layout );
					BackendDevice.SetLayout( color, ImageLayout.CopySource );
				}

				return pixels;
			}

			foreach ( var parallel in new[] { false, true } )
			{
				system.ParallelRecording = parallel;
				foreach ( var target in new[] { single, multi } )
				{
					var name = $"{target.Samples}x{(parallel ? ", on workers" : "")}";
					Frames( target, true );
					var separateCalls = backend.MarksMerged;
					var apart = Frames( target, false );
					var none = backend.MarksMerged;
					Assert.AreEqual( separateCalls, none, $"{name}: no marks merged when disabled" );
					var mergedCalls = backend.ResidentCullCalls;
					var merged = Frames( target, true );
					TestContext.WriteLine( $"{name}: {Differences( apart, merged )}, {backend.MarksMerged - none} marks merged" );
					Assert.AreEqual( 0, Device.ValidationErrors, $"{name}: no validation errors" );
					Assert.IsTrue( backend.ResidentCullCalls > mergedCalls, $"{name}: calls drew from the resident cull" );
					Assert.AreEqual( 8L * 2, backend.MarksMerged - none, $"{name}: the main view and the skybox each merge one mark per frame" );
					Assert.AreEqual( 0, Different( apart, merged ), $"{name}: the same pixels" );
				}
			}

			Assert.IsTrue( system.SkyboxSystem is { Stats.Draws: > 0 }, "the skybox drew" );
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
		RequireGpuDrivenDepth( backend );

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

				var material = backend.CreateMaterial( "Resident cull release boxes", new Color( 0.5f, 0.6f, 0.4f ) );
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
		RequireGpuDrivenDepth( backend );

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
			var material = backend.CreateMaterial( "Resident far cache", new Color( 0.6f, 0.6f, 0.55f ) );
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
		RequireGpuDrivenDepth( backend );

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

			var materials = new GpuMaterial[Rows];
			var aggregates = new AggregateObject[Rows];
			for ( int row = 0; row < Rows; row++ )
			{
				materials[row] = backend.CreateMaterial( $"Memo boxes {row}", new Color( 0.3f + row * 0.2f, 0.6f, 0.4f ) );
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
			var color = slang ? backend.LoadTexture( Fixtures.Rope.Color ) : null;
			var normal = slang ? backend.LoadTexture( Fixtures.Rope.Normal ) : null;
			var rma = slang ? backend.LoadTexture( Fixtures.Rope.Rma ) : null;
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

			backend.SetOnMaterial( materials[0], new Dictionary<string, float[]> { ["g_vColorTint"] = [0.9f, 0.2f, 0.2f, 0] }, null );
			Same( "a material's value" );

			// Lit by object attributes: their own bucket, and the lighting record their constants make
			var attributes = backend.CreateParameters();
			attributes.SetData( "PerBatchLightProbeVolumeConstantBuffer_t", new Vector4( 1, 2, 3, 4 ) );
			aggregates[2].LightmapAttributes = attributes;
			aggregates[2].Lightmapped = true;
			Same( "object attributes" );
			attributes.SetData( "PerBatchLightProbeVolumeConstantBuffer_t", new Vector4( 5, 6, 7, 8 ) );
			Same( "their lighting constants" );

			// A combo they set: another variant of a compiled shader; a surface's records don't take the object's combos, so none
			// moves, and the memo still has to give what looking up does
			attributes.SetCombo( "D_OPAQUE_FADE", 1 );
			Same( "a combo", moves: false );

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
			// Splits only compiled variants' buckets: these are surfaces, so nothing moves, and the memo still matches
			backend.GpuDrivenMaterialBuckets = true;
			Same( "a bucket per material", moves: false );

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
		RequireGpuDrivenDepth( backend );

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
			Assert.IsTrue( forward.All( d => d.Commands >= 0 && d.Shader == "standard.slang (surface)" ), $"its multi-draws' commands were read back: {string.Join( ", ", forward.Select( d => $"{d.Shader} {d.Commands}" ) )}" );
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
	/// As a load starts, the next meshes start new vertex and index pages, once per load: a map's meshes share no page with
	/// the menu's, so its pages free with it (sharing the menu's index page kept its ranges' meshlets, levels and draw
	/// records).
	/// </summary>
	[TestMethod]
	public void ALoadStartsNewPages()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var indices = new byte[1024];
		var (vertexA, _) = tables.AddVertices( new byte[1024] );
		var (_, indexA, _) = tables.AddIndices( indices, IndexFormat.UInt16 );
		Assert.AreEqual( vertexA, tables.AddVertices( new byte[1024] ).Slot, "meshes fill the page being filled" );

		backend.Loading = true;
		var (vertexB, _) = tables.AddVertices( new byte[1024] );
		var (_, indexB, _) = tables.AddIndices( indices, IndexFormat.UInt16 );
		Assert.AreNotEqual( vertexA, vertexB, "a load's meshes start a new vertex page" );
		Assert.AreNotEqual( indexA, indexB, "and a new index page" );

		backend.Loading = true;
		Assert.AreEqual( vertexB, tables.AddVertices( new byte[1024] ).Slot, "once per load, though the host says so each frame" );
		backend.Loading = false;
		Assert.AreEqual( vertexB, tables.AddVertices( new byte[1024] ).Slot, "and the load's pages go on filling after it" );
		Assert.AreEqual( 1, tables.NewPagesStarted );
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
	/// The draw records over a freed index page are taken by the next draws, lowest first, once the frames that may have
	/// drawn them are done, rather than the next draws being appended: each map loaded made its draws on top of the last
	/// one's, and the tables indexed by draw record (the LOD table, meshlet runs) doubled every visit.
	/// </summary>
	[TestMethod]
	public void DrawRecordsOfFreedPagesAreReused()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var indices = new byte[3 * 2 * 64];
		var (_, pageA, _) = tables.AddIndices( indices, IndexFormat.UInt16 );
		var made = new uint[8];
		for ( uint i = 0; i < made.Length; i++ ) made[i] = tables.Draw( 5, 0, 0, 0, i * 3, 3, 0, pageA );
		var records = tables.DrawRecords;

		// Its mesh gone and the page freed: its records wait for the frames that may have drawn them, and a draw made
		// meanwhile is appended
		tables.ReleaseRange( pageA, indices.Length );
		tables.FreeEmptyPages();
		Assert.AreEqual( 1, tables.PagesFreed );
		Assert.AreEqual( (0, made.Length), tables.FreeDrawRecords );
		var (_, pageB, _) = tables.AddIndices( indices, IndexFormat.UInt16 );
		Assert.AreEqual( (uint)records, tables.Draw( 7, 0, 0, 0, 0, 3, 0, pageB ), "a draw made at once is appended" );

		for ( int f = 0; f < Device.FramesInFlight + 2; f++ )
		{
			backend.BeginFrame();
			tables.FreeEmptyPages();
		}

		Assert.AreEqual( (made.Length, 0), tables.FreeDrawRecords, "free once those frames are done" );
		var reused = new uint[made.Length];
		for ( uint i = 0; i < reused.Length; i++ ) reused[i] = tables.Draw( 7, 0, 0, 0, (i + 1) * 3, 3, 0, pageB );
		CollectionAssert.AreEqual( made, reused, "the next draws take their slots, lowest first" );
		Assert.AreEqual( records + 1, tables.DrawRecords, "the table didn't grow" );
		Assert.AreEqual( 7u, tables.DrawFields( reused[0] ).Geometry, "the slot holds the new record" );
		Assert.AreEqual( made.Length, tables.DrawRecordsReused );
		Assert.AreEqual( reused[3], tables.Draw( 7, 0, 0, 0, 4 * 3, 3, 0, pageB ), "and is found by its key" );
		tables.ReleaseRange( pageB, indices.Length );
		Device.WaitIdle();
	}

	/// <summary>
	/// The LOD table, indexed by draw record, shrinks after ranges are forgotten (a map unloaded) once it's four times what
	/// the draw records with levels need, to the smallest power of two over the highest (at least its first size); not
	/// otherwise, and a record past it grows it again.
	/// </summary>
	[TestMethod]
	public void LodTableShrinksOnceRangesAreForgotten()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		using var lods = new Borealis.Backend.Lods.LodStore( Device, backend.GpuDrivenTables, new Borealis.Backend.Lods.LodConfig(), 1 );
		lods.BeginFrame();
		Assert.AreEqual( 1u << 12, lods.Table.Records );

		// Grown as a map's records grow it
		var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
		void Grow()
		{
			var list = Device.BeginCommandList( QueueType.Graphics, "LOD table growth" );
			typeof( Borealis.Backend.Lods.LodStore ).GetMethod( "GrowTable", flags ).Invoke( lods, [list, 1L << 16] );
			Device.Submit( list );
		}

		Grow();
		lods.BeginFrame();
		Assert.AreEqual( 1u << 16, lods.Table.Records, "not before ranges are forgotten" );

		// Ranges forgotten, none with levels left: back to its first size, once
		typeof( Borealis.Backend.Lods.LodStore ).GetField( "mayShrink", flags ).SetValue( lods, true );
		lods.BeginFrame();
		Assert.AreEqual( 1u << 12, lods.Table.Records );
		Assert.AreEqual( 1, lods.TableShrinks );
		lods.BeginFrame();
		Assert.AreEqual( 1, lods.TableShrinks, "once" );
		Assert.AreEqual( 0, Device.ValidationErrors );
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
	/// A material's stage rings once it has made <see cref="GpuDrivenTables.MaxRecordsPerStage"/> material records (new
	/// attribute values each time): that stage only, not the material's others or another material's. Ringing, it takes
	/// up to <see cref="GpuDrivenTables.MaxCopiesPerFrame"/> values a frame; one more and it draws classic.
	/// </summary>
	[TestMethod]
	public void AStageRingsAfterItsLimitOfMaterialRecords()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var material = backend.CreateMaterial( "Churning", Color.White );
		var other = backend.CreateMaterial( "Steady", Color.White );
		var shader = backend.Shaders.Load( "shaders/line.shader" );
		var variant = shader is null ? null : backend.Shaders.Variant( shader, "Forward", default );
		if ( variant?.Vertex is null || variant.Pixel is null ) Assert.Inconclusive( "line.shader isn't compiled" );

		uint Record( GpuMaterial m, ShaderStage stage, int attributes ) =>
			tables.Material( new GpuDrivenTables.MaterialRecordKey( m, 0, stage, false, attributes ), 64, 0, static ( _, bytes ) => bytes.Clear() );

		Record( other, variant.Pixel, 0 );
		for ( int i = 0; i < GpuDrivenTables.MaxRecordsPerStage - 1; i++ ) Record( material, variant.Pixel, i );
		Assert.IsFalse( tables.Rings( material, variant.Pixel ), "one short of the limit" );
		Record( material, variant.Pixel, 0 );
		Assert.IsFalse( tables.Rings( material, variant.Pixel ), "a record it has already is found, not made" );

		Record( material, variant.Pixel, GpuDrivenTables.MaxRecordsPerStage );
		Assert.IsTrue( tables.Rings( material, variant.Pixel ), "at the limit" );
		Assert.IsFalse( tables.Churns( material, variant.Pixel ), "ringing, it stays GPU-driven" );
		Assert.IsFalse( tables.Rings( material, variant.Vertex ), "its other stage" );
		Assert.IsFalse( tables.Rings( other, variant.Pixel ), "another material" );

		for ( int i = 0; i < GpuDrivenTables.MaxCopiesPerFrame; i++ ) Record( material, variant.Pixel, 1000 + i );
		Assert.IsFalse( tables.Churns( material, variant.Pixel ), "as many values in a frame as it has copies" );
		Record( material, variant.Pixel, 2000 );
		Assert.IsTrue( tables.Churns( material, variant.Pixel ), "one more in the frame" );
		Device.WaitIdle();
	}

	/// <summary>
	/// A stage that makes a material record every frame (an attribute a game sets each frame) rings after
	/// <see cref="GpuDrivenTables.ChurnRecords"/> of them, not <see cref="GpuDrivenTables.MaxRecordsPerStage"/>: the draw
	/// records naming the records it made go at the next frame's start, and from then on each frame's value goes in its
	/// frame slot's copy, so it has a draw record per range per frame slot however many values it reads. Each made record
	/// used to make draw records for every range drawn with it: 5.1 million of them on a forest map. A stage making as many
	/// in one frame (objects' own values as they come into view) doesn't ring, and keeps its draw records.
	/// </summary>
	[TestMethod]
	public void AStageMakingARecordEveryFrameRingsAndKeepsAFewDrawRecords()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var windy = backend.CreateMaterial( "Windy", Color.White );
		var objects = backend.CreateMaterial( "Objects", Color.White );
		var shader = backend.Shaders.Load( "shaders/line.shader" );
		var variant = shader is null ? null : backend.Shaders.Variant( shader, "Forward", default );
		if ( variant?.Vertex is null || variant.Pixel is null ) Assert.Inconclusive( "line.shader isn't compiled" );

		const int Ranges = 4;
		uint Record( GpuMaterial m, int attributes ) =>
			tables.Material( new GpuDrivenTables.MaterialRecordKey( m, 0, variant.Pixel, false, attributes ), 64, 0, static ( _, bytes ) => bytes.Clear() );
		void DrawRanges( uint pixel )
		{
			for ( int r = 0; r < Ranges; r++ ) tables.Draw( 1, 0, pixel, 0, (uint)r * 3, 3, 0, 0 );
		}

		tables.BeginFrame();
		for ( int i = 0; i < GpuDrivenTables.ChurnRecords * 2; i++ ) DrawRanges( Record( objects, i ) );
		var objectDraws = tables.Records.Draws;

		for ( int frame = 0; frame < GpuDrivenTables.ChurnRecords - 1; frame++ )
		{
			tables.BeginFrame();
			DrawRanges( Record( windy, frame ) );
		}

		Assert.IsFalse( tables.Rings( windy, variant.Pixel ), "one record short" );
		tables.BeginFrame();
		DrawRanges( Record( windy, GpuDrivenTables.ChurnRecords ) );
		Assert.IsTrue( tables.Rings( windy, variant.Pixel ), "a record a frame for as many frames" );
		Assert.IsFalse( tables.Rings( objects, variant.Pixel ), "as many records in one frame" );
		Assert.AreEqual( objectDraws + GpuDrivenTables.ChurnRecords * Ranges, tables.Records.Draws, "a draw record per range per record until the next frame" );

		var copies = new List<uint>();
		long materialBytes = 0;
		for ( int frame = 0; frame < GpuDrivenTables.FrameSlots * 4; frame++ )
		{
			tables.BeginFrame();
			var copy = Record( windy, 1000 + frame );
			Assert.AreEqual( copy, Record( windy, 1000 + frame ), "the frame's copy of a value is found again" );
			DrawRanges( copy );
			copies.Add( copy );
			if ( frame == GpuDrivenTables.FrameSlots - 1 ) materialBytes = tables.Bytes.Materials;
		}

		Assert.AreEqual( GpuDrivenTables.ChurnRecords * Ranges, tables.DrawRecordsForgotten, "the records it made go, and their draw records" );
		Assert.AreEqual( objectDraws + GpuDrivenTables.FrameSlots * Ranges, tables.Records.Draws, "a draw record per range per frame slot" );
		Assert.AreEqual( GpuDrivenTables.FrameSlots, copies.Distinct().Count(), "a copy per frame slot" );
		Assert.AreEqual( copies[0], copies[GpuDrivenTables.FrameSlots], "rewritten a frame slot on" );
		Assert.AreEqual( materialBytes, tables.Bytes.Materials, "the material table stops growing" );
		Device.WaitIdle();
	}

	/// <summary>
	/// A stage that rang and then reads the same value every frame for <see cref="GpuDrivenTables.CalmFrames"/> stops
	/// ringing, and the draw records naming its copies go: rocks rang while their textures streamed in, then made their
	/// call's plans again every frame for good (60 MB of garbage a second on odyssey). One still churning keeps ringing.
	/// </summary>
	[TestMethod]
	public void ARingingStageReadingTheSameValueCalms()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var settled = backend.CreateMaterial( "Settled", Color.White );
		var windy = backend.CreateMaterial( "Windy", Color.White );
		var shader = backend.Shaders.Load( "shaders/line.shader" );
		var variant = shader is null ? null : backend.Shaders.Variant( shader, "Forward", default );
		if ( variant?.Pixel is null ) Assert.Inconclusive( "line.shader isn't compiled" );

		uint Record( GpuMaterial m, int attributes ) =>
			tables.Material( new GpuDrivenTables.MaterialRecordKey( m, 0, variant.Pixel, false, attributes ), 64, 0, static ( _, bytes ) => bytes.Clear() );

		for ( int frame = 0; frame < GpuDrivenTables.ChurnRecords; frame++ )
		{
			tables.BeginFrame();
			tables.Draw( 1, 0, Record( settled, frame ), 0, 0, 3, 0, 0 );
			tables.Draw( 1, 0, Record( windy, frame ), 0, 3, 3, 0, 0 );
		}

		Assert.IsTrue( tables.Rings( settled, variant.Pixel ) && tables.Rings( windy, variant.Pixel ), "both ring" );

		for ( int frame = 0; frame < GpuDrivenTables.CalmFrames + 2; frame++ )
		{
			tables.BeginFrame();
			tables.Draw( 1, 0, Record( settled, -1 ), 0, 0, 3, 0, 0 );
			tables.Draw( 1, 0, Record( windy, 1000 + frame ), 0, 3, 3, 0, 0 );
		}

		Assert.IsFalse( tables.Rings( settled ), "the same value calms it" );
		Assert.IsTrue( tables.Rings( windy ), "a new value each frame keeps it ringing" );
		Assert.AreEqual( 1, tables.RingingCalmed );

		Assert.AreEqual( 1 + GpuDrivenTables.FrameSlots, tables.Records.Draws, "its record's draw record and the windy one's copies': its own copies' went" );
		var record = Record( settled, -1 );
		tables.BeginFrame();
		Assert.AreEqual( record, Record( settled, -1 ), "a record again, found the next frame, not a frame slot's copy" );
		Device.WaitIdle();
	}

	/// <summary>
	/// A ringing stage out of view (not asked for) doesn't calm, however long: it would make its records and their draw
	/// records afresh each time it came back into view, and ring again. Drawn with the same value, it calms as before.
	/// </summary>
	[TestMethod]
	public void ARingingStageOutOfViewDoesntCalm()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var windy = backend.CreateMaterial( "Windy", Color.White );
		var shader = backend.Shaders.Load( "shaders/line.shader" );
		var variant = shader is null ? null : backend.Shaders.Variant( shader, "Forward", default );
		if ( variant?.Pixel is null ) Assert.Inconclusive( "line.shader isn't compiled" );

		uint Record( int attributes ) =>
			tables.Material( new GpuDrivenTables.MaterialRecordKey( windy, 0, variant.Pixel, false, attributes ), 64, 0, static ( _, bytes ) => bytes.Clear() );

		for ( int frame = 0; frame < GpuDrivenTables.ChurnRecords; frame++ )
		{
			tables.BeginFrame();
			tables.Draw( 1, 0, Record( frame ), 0, 0, 3, 0, 0 );
		}

		Assert.IsTrue( tables.Rings( windy, variant.Pixel ), "it rings" );

		for ( int frame = 0; frame < GpuDrivenTables.CalmFrames * 2; frame++ ) tables.BeginFrame();
		tables.BeginFrame();
		tables.Draw( 1, 0, Record( 5000 ), 0, 0, 3, 0, 0 );
		tables.BeginFrame();
		Assert.IsTrue( tables.Rings( windy, variant.Pixel ), "out of view, it kept ringing" );
		Assert.AreEqual( 0, tables.RingingCalmed );

		for ( int frame = 0; frame < GpuDrivenTables.CalmFrames + 2; frame++ )
		{
			tables.BeginFrame();
			tables.Draw( 1, 0, Record( 5000 ), 0, 0, 3, 0, 0 );
		}

		Assert.IsFalse( tables.Rings( windy, variant.Pixel ), "drawn with the same value, it calms" );
		Device.WaitIdle();
	}

	/// <summary>
	/// Once a record is made at a material's newer version (texture streaming moves it on with each mip), the records of
	/// its older versions go at the next frame's start, with the draw records naming them; another material's stay.
	/// </summary>
	[TestMethod]
	public void AMaterialsOlderVersionsRecordsAreForgotten()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		var tables = backend.GpuDrivenTables;
		var streamed = backend.CreateMaterial( "Streamed", Color.White );
		var other = backend.CreateMaterial( "Other", Color.White );
		var shader = backend.Shaders.Load( "shaders/line.shader" );
		var variant = shader is null ? null : backend.Shaders.Variant( shader, "Forward", default );
		if ( variant?.Vertex is null || variant.Pixel is null ) Assert.Inconclusive( "line.shader isn't compiled" );

		uint Record( GpuMaterial m, int version, ShaderStage stage ) =>
			tables.Material( new GpuDrivenTables.MaterialRecordKey( m, version, stage, false, 0 ), 64, 0, static ( _, bytes ) => bytes.Clear() );
		uint Draw( GpuMaterial m, int version ) => tables.Draw( 1, Record( m, version, variant.Vertex ), Record( m, version, variant.Pixel ), 0, 0, 3, 0, 0 );

		tables.BeginFrame();
		var before = tables.Records;
		var kept = Draw( other, 0 );
		Draw( streamed, 0 );
		Draw( streamed, 1 );
		var during = tables.Records;
		Assert.AreEqual( (before.Materials + 6, before.Draws + 3), (during.Materials, during.Draws), "both versions' records until the frame ends" );

		tables.BeginFrame();
		var after = tables.Records;
		Assert.AreEqual( (before.Materials + 4, before.Draws + 2), (after.Materials, after.Draws), "the older version's two stage records and its draw record went" );
		Assert.AreEqual( kept, Draw( other, 0 ), "another material's are kept" );
		Draw( streamed, 1 );
		Assert.AreEqual( after.Draws, tables.Records.Draws, "and the newer version's found, not made again" );
		Device.WaitIdle();
	}

	/// <summary>
	/// A material a game sets a new value on every frame (wind, a pulse) made a material record each time it was drawn
	/// GPU-driven, and records never go: GAMMA's trees took 75 GB of host memory in three minutes. Past its limit it rings:
	/// its records are copies rewritten each frame, so the tables stop growing, it stays GPU-driven, and it draws what the
	/// classic path draws with the frame's values.
	/// </summary>
	[TestMethod]
	public void AMaterialChangedEveryFrameStopsAddingRecords()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDrivenMeshes = true, TimeDraws = true };
		RequireGpuDrivenDepth( backend );

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
			var material = backend.CreateMaterial( "Pulsing boxes", new Color( 0.8f, 0.6f, 0.4f ) );
			var (mesh, fragments) = Boxes( backend, material, 0 );
			world.Add( new AggregateObject( mesh, fragments ) );

			var view = new RenderView { Name = "Churn test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( bool gpuDriven, float tint )
			{
				backend.GpuDriven = gpuDriven;
				backend.SetOnMaterial( material, new Dictionary<string, float[]> { ["g_vColorTint"] = [0.8f, 0.6f, tint, 0] }, null );
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var tables = backend.GpuDrivenTables;
			const int Frames = GpuDrivenTables.MaxRecordsPerStage + 60;
			for ( int i = 0; i < Frames; i++ ) Frame( true, i / (float)Frames );
			var (madeBytes, madeDraws) = (tables.Bytes.Materials, tables.Records.Draws);
			for ( int i = 0; i < 30; i++ ) Frame( true, 1 + i / 30f );
			var (afterBytes, afterDraws) = (tables.Bytes.Materials, tables.Records.Draws);
			backend.GpuDrivenLeftClassic.Clear();
			var driven = Frame( true, 0.25f );
			var left = backend.GpuDrivenLeftClassic.Keys.ToArray();
			var classic = Frame( false, 0.25f );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.IsTrue( tables.Rings( material ), "it rings" );
			Assert.AreEqual( madeBytes, afterBytes, $"no material records past the limit: {madeBytes} bytes over {Frames} frames, {afterBytes} after 30 more" );
			Assert.AreEqual( madeDraws, afterDraws, "nor draw records" );
			Assert.IsFalse( left.Any( x => x.Contains( "material records churn", StringComparison.Ordinal ) ), $"its draws stay GPU-driven: {string.Join( ", ", left )}" );
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
		RequireGpuDrivenDepth( backend );

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
		RequireGpuDrivenDepth( backend );

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
	/// pixel material record for it: an alpha-tested generic material (a standard surface) on boxes whose draws aren't
	/// flagged alpha-tested read texture slots from record 0 there, another material's words, which lost the device. (Its ids
	/// pass alpha-tests where the stripped depth pass doesn't, so its cut-out pixels aren't the classic ones; frames with ids
	/// draw alike.)
	/// </summary>
	[TestMethod]
	public void DepthOnlyBucketsDrawingIdsHaveAPixelRecord()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDrivenVisibility = true };
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			if ( backend.Content.Find( "models/vr/autochap_hand.vmat" ) is null ) { Assert.Inconclusive( "models/vr/autochap_hand.vmat isn't compiled" ); return; }
			var material = ((GpuDevice)backend).LoadMaterial( "models/vr/autochap_hand.vmat" );
			Assert.IsInstanceOfType<SurfaceMaterial>( material, "autochap_hand draws as the standard surface" );
			Assert.IsTrue( material.IsAlphaTest, "autochap_hand is alpha-tested" );

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
		RequireGpuDrivenDepth( backend );

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
		RequireGpuDrivenDepth( backend );

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
		RequireGpuDrivenDepth( backend );

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
		RequireGpuDrivenDepth( backend );

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
		RequireGpuDrivenDepth( backend );

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
			// Additive: another pipeline, whose blend doesn't commute with the alpha blend on either side of it. The material is
			// standard's, translucent alone: additive blending is set in code, as Material.SetFeature does
			var blue = backend.LoadMaterial( "materials/dev/primary_green_additive.vmat", new Dictionary<string, float[]> { ["g_vColorTint"] = [0.1f, 0.3f, 0.9f, 0] }, "Translucent blue" );
			backend.SetFeaturesOnMaterial( blue, new Dictionary<string, int> { ["F_TRANSLUCENT"] = 1, ["F_ADDITIVE_BLEND"] = 1 } );
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
			Assert.IsTrue( translucentCalls >= 3, $"the rows draw in order, alpha, additive, alpha, so in three multi-draws at least: {translucentCalls} ({string.Join( "; ", census.Draws.Where( d => Ours( d ) || d.Pass.Contains( "Translucent", StringComparison.Ordinal ) ).Select( d => $"{d.Path} {d.Pass} {d.Material} {d.Shader} {d.Kind} calls {d.Calls} bucket {d.Bucket}" ) )})" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// <see cref="GlassDrawsGpuDrivenInOrder"/>'s scene: opaque boxes behind; rows of translucent red boxes, clear glass
	/// (refraction 1.05), tinted blurred glass (1.02, blur 0.6) and red again in front of them, back to front, offset to show
	/// what's behind; and its view.
	/// </summary>
	internal static (RenderWorld World, RenderView View) GlassScene( BackendDevice backend )
	{
		const string glassPath = "materials/sbox/glass/glass_a.vmat";
		const string translucentPath = "materials/dev/primary_white_trans.vmat";
		if ( backend.Content.Find( glassPath ) is null || backend.Content.Find( translucentPath ) is null ) Assert.Inconclusive( $"{glassPath} or {translucentPath} isn't built" );
		var clear = backend.LoadMaterial( glassPath, new Dictionary<string, float[]> { ["g_flRefractionStrength"] = [1.05f] }, "Glass clear" );
		var tinted = backend.LoadMaterial( glassPath, new Dictionary<string, float[]> { ["g_flRefractionStrength"] = [1.02f], ["g_flBlurAmount"] = [0.6f], ["g_flTintColor"] = [0.6f, 0.9f, 0.7f] }, "Glass tinted" );
		Assert.IsTrue( clear is SurfaceMaterial { IsTranslucent: true } glass && glass.Attribute( "bWantsFBCopyTexture", false ), $"{glassPath} draws translucent from the frame buffer copy" );
		var red = backend.LoadMaterial( translucentPath, new Dictionary<string, float[]> { ["g_vColorTint"] = [0.9f, 0.2f, 0.1f, 0] }, "Glass test red" );

		var world = new RenderWorld();
		world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
		world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
		world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
		world.Lighting.SunShadowCascades = 0;

		var (far, _) = Boxes( backend, backend.CreateMaterial( "Glass test far", new Color( 0.8f, 0.6f, 0.4f ) ), 150 );
		world.Add( new MeshObject( far, Transform.Zero ) );
		foreach ( var (material, y) in new[] { (red, 90f), (clear, 30f), (tinted, -30f), (red, -90f) } )
		{
			var (boxes, _) = Boxes( backend, backend.CreateMaterial( "Glass test boxes", Color.White ), y );
			var reads = material != red;
			world.Add( new MeshObject( boxes, new Transform( new Vector3( 20, 0, 0 ) ) ) { MaterialOverride = material, Tint = new Color( 1, 1, 1, reads ? 1 : 0.5f ), WantsFrameBufferCopy = reads } );
		}

		var view = new RenderView { Name = "Glass", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
		view.Position = new Vector3( -500, -300, 300 );
		view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );
		return (world, view);
	}

	/// <summary>
	/// Four frames of <paramref name="world"/> in a renderer of its own (so from a new, cleared frame copy), the last's
	/// pixels returned and counted in <paramref name="census"/>.
	/// </summary>
	byte[] GlassFrames( BackendDevice backend, RenderWorld world, RenderView view, ViewTarget target, bool gpuDriven, DrawCensus census, bool parallel = false )
	{
		using var system = new RenderSystem { ParallelRecording = parallel };
		backend.GpuDriven = gpuDriven;
		byte[] pixels = null;
		for ( int i = 0; i < 4; i++ )
		{
			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			backend.Census = i == 3 ? census : null;
			try
			{
				system.Render( world, view, target );
			}
			finally
			{
				backend.Census = null;
			}

			Device.WaitIdle();
			var color = target.Samples > 1 ? target.Resolved : target.Color;
			var (texture, layout) = BackendDevice.Unwrap( color );
			pixels = ReadBack( texture, layout );
			BackendDevice.SetLayout( color, ImageLayout.CopySource );
		}

		return pixels;
	}

	/// <summary>
	/// The frame buffer copy made with its depth-aware mask in one pass (<see cref="BackendDevice.FrameBufferCopyMasked"/>)
	/// holds what a transfer copy (or a resolve) and then the mask over it hold. The glass frames match exactly for a
	/// single sample; a fixed-function MSAA resolve may round differently from a shader average.
	/// </summary>
	[TestMethod]
	public void GlassFrameCopyMadeWithItsMaskInOnePass()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = false, GpuDrivenMeshes = true };
		var (world, view) = GlassScene( backend );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			// Multisampled, the pass resolves the frame where it keeps it
			using var single = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Glass frame copy test" );
			using var multi = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Glass frame copy test (4x)" );
			foreach ( var (target, gpuDriven) in new[] { (single, false), (single, true), (multi, false) } )
			{
				var samples = target.Samples;
				backend.FrameBufferCopyMasked = false;
				var apart = GlassFrames( backend, world, view, target, gpuDriven, new DrawCensus() );
				backend.FrameBufferCopyMasked = true;
				var census = new DrawCensus();
				var together = GlassFrames( backend, world, view, target, gpuDriven, census );

				TestContext.WriteLine( $"{samples}x, GPU-driven {gpuDriven}: {Differences( apart, together )}" );
				Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
				Assert.IsTrue( census.Draws.Any( d => d.Pass.EndsWith( "Translucent Forward", StringComparison.Ordinal ) ), "the glass draws" );
				var name = $"{samples}x, GPU-driven {gpuDriven}";
				if ( samples == 1 )
					Assert.AreEqual( 0, Different( apart, together ), $"{name}: the same pixels" );
				else
					Assert.IsTrue( Same( apart, together, maxError: 1f / 2048, maxDifferentPixels: int.MaxValue ), $"{name}: the same pixels to resolve precision ({Differences( apart, together )})" );
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A viewmodel's glass (an overlay reading the frame buffer copy, as the tool gun's screen does) in front of the world's
	/// glass: the frame is copied twice, by the world's translucent layer and again by GameOverlay Translucent, into the same
	/// copy. Without multisampling and with it, its segments recorded one after another and beside each other, the frames
	/// are the same pixels whether the copy is made with its mask in one pass or not, with no validation errors.
	/// </summary>
	[TestMethod]
	public void ViewmodelGlassCopiesTheFrameAgain()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = false, GpuDrivenMeshes = true };
		var (world, view) = GlassScene( backend );
		var glass = backend.LoadMaterial( "materials/sbox/glass/glass_a.vmat", new Dictionary<string, float[]> { ["g_flRefractionStrength"] = [1.04f], ["g_flBlurAmount"] = [0.3f] }, "Viewmodel glass" );

		// One of a row of boxes just in front of the camera, drawn as a viewmodel
		var (boxes, _) = Boxes( backend, backend.CreateMaterial( "Viewmodel glass boxes", Color.White ), 0 );
		var near = view.Position + view.Rotation.Forward * 220 - new Vector3( -60, 0, 40 );
		world.Add( new MeshObject( boxes, new Transform( near ) ) { MaterialOverride = glass, WantsFrameBufferCopy = true, Overlay = true } );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var single = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Viewmodel glass test" );
			using var multi = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Viewmodel glass test (4x)" );
			foreach ( var (target, parallel) in new[] { (single, false), (single, true), (multi, true) } )
			{
				backend.FrameBufferCopyMasked = false;
				var apart = GlassFrames( backend, world, view, target, false, new DrawCensus(), parallel );
				backend.FrameBufferCopyMasked = true;
				var census = new DrawCensus();
				var together = GlassFrames( backend, world, view, target, false, census, parallel );

				TestContext.WriteLine( $"{target.Samples}x, parallel {parallel}: {Differences( apart, together )}" );
				Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
				Assert.IsTrue( census.Draws.Any( d => d.Pass.EndsWith( "GameOverlay Translucent", StringComparison.Ordinal ) && d.Material == "Viewmodel glass" ), "the viewmodel's glass draws in GameOverlay Translucent" );
				Assert.IsTrue( census.Draws.Any( d => d.Pass.EndsWith( "Translucent Forward", StringComparison.Ordinal ) && d.Material is "Glass clear" or "Glass tinted" ), "the world's glass draws" );
				Assert.AreEqual( 0, Different( apart, together ), $"{target.Samples}x, parallel {parallel}: the same pixels" );
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// The frame buffer copy's mips made a 16x16 tile a group (<see cref="BackendDevice.FrameBufferCopyTiledMips"/>) are
	/// downsample_cs's, a texel a thread: the glass scene's frames, whose tinted glass reads them blurred, are the same pixels
	/// either way.
	/// </summary>
	[TestMethod]
	public void GlassFrameCopyMipsMadeInTiles()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = false, GpuDrivenMeshes = true };
		var (world, view) = GlassScene( backend );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Glass frame copy mips test" );
			backend.FrameBufferCopyTiledMips = false;
			var texels = GlassFrames( backend, world, view, target, false, new DrawCensus() );
			backend.FrameBufferCopyTiledMips = true;
			var tiles = GlassFrames( backend, world, view, target, false, new DrawCensus() );

			TestContext.WriteLine( Differences( texels, tiles ) );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.AreEqual( 0, Different( texels, tiles ), "the same pixels" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A 3D skybox's frame buffer copy is made, with its mask, only around the glass that reads it, which reads where it is
	/// there (<see cref="BackendDevice.FrameBufferCopyRegions"/>): blurred and clear glass small in a skybox, in front of its
	/// opaque boxes, and at the screen's edge, are the same pixels as with the copy made whole, multisampled too.
	/// </summary>
	[TestMethod]
	public void SkyboxGlassFrameCopyMadeAroundItsReaders()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = false, GpuDrivenMeshes = true };
		var (glassWorld, _) = GlassScene( backend );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			// The glass scene, far off in the skybox: what reads the copy is a small part of the screen
			var world = new RenderWorld { Skybox3D = new Skybox3D( glassWorld ) { Scale = 16 } };
			world.Lighting.SunShadowCascades = 0;
			var view = new RenderView { Name = "Skybox glass", FieldOfView = 70, ZNear = 4, ZFar = 40000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -16 * 2500, -16 * 300, 16 * 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 16 * 40 ) );

			using var single = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Skybox glass test" );
			using var multi = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Skybox glass test (4x)" );
			foreach ( var target in new[] { single, multi } )
			{
				backend.FrameBufferCopyRegions = false;
				var whole = GlassFrames( backend, world, view, target, false, new DrawCensus() );
				backend.FrameBufferCopyRegions = true;
				var census = new DrawCensus();
				var around = GlassFrames( backend, world, view, target, false, census );

				TestContext.WriteLine( $"{target.Samples}x: {Differences( whole, around )}" );
				Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
				Assert.IsTrue( census.Draws.Any( d => d.Pass.Contains( "Translucent Forward", StringComparison.Ordinal ) && d.Material is "Glass clear" or "Glass tinted" ), "the skybox's glass draws" );
				Assert.AreEqual( 0, Different( whole, around ), $"{target.Samples}x: the same pixels" );
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Refractive glass (glass.shader's surface, reading the frame buffer copy) draws GPU-driven in the translucent list's
	/// order, with the frame copied once, at the first glass: between rows of translucent boxes that don't read it and in
	/// front of opaque ones, two rows of glass, refracting, blurred and tinted, shade exactly as the classic path shades them,
	/// and none of them draws the classic way, nor their depth in the refraction stencil, which the copy's depth-aware mask
	/// reads. The mask takes the last frame's blurred copy where the glass is, so each path draws its frames in a renderer of
	/// its own from a new (cleared) copy, the same frames each time.
	/// </summary>
	[TestMethod]
	public void GlassDrawsGpuDrivenInOrder()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = false, GpuDrivenMeshes = true };
		RequireGpuDrivenDepth( backend );
		var (world, view) = GlassScene( backend );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven glass test" );
			var before = new DrawCensus();
			var classic = GlassFrames( backend, world, view, target, false, before );
			var census = new DrawCensus();
			var driven = GlassFrames( backend, world, view, target, true, census );

			byte[] Rgba8( byte[] halfs )
			{
				var rgba = new byte[Width * Height * 4];
				for ( int i = 0; i < rgba.Length; i++ ) rgba[i] = (byte)(MathF.Pow( Math.Clamp( (float)BitConverter.ToHalf( halfs, i * 2 ), 0, 1 ), 1 / 2.2f ) * 255 + 0.5f);
				return rgba;
			}

			Save( Rgba8( classic ), Width, Height, "glass-classic" );
			Save( Rgba8( driven ), Width, Height, "glass-gpudriven" );
			TestContext.WriteLine( Differences( classic, driven ) );
			string Translucent( DrawCensus c ) => string.Join( "; ", c.Draws.Where( d => d.Pass.Contains( "Translucent", StringComparison.Ordinal ) || d.Pass.Contains( "Refraction", StringComparison.Ordinal ) ).Select( d => $"{d.Path} {d.Pass} {d.Material} {d.Kind} calls {d.Calls} bucket {d.Bucket}" ) );
			TestContext.WriteLine( $"classic: {Translucent( before )}" );
			TestContext.WriteLine( $"GPU-driven: {Translucent( census )}" );

			bool Glass( DrawCensus.Draw d ) => d.Material is "Glass clear" or "Glass tinted";
			bool Forward( DrawCensus.Draw d ) => d.Pass.EndsWith( "Translucent Forward", StringComparison.Ordinal );
			bool Stencil( DrawCensus.Draw d ) => d.Pass.EndsWith( "Refraction Stencil Layer", StringComparison.Ordinal );
			Assert.IsTrue( before.Draws.Any( d => d.Path == DrawCensus.Path.Classic && Glass( d ) && Forward( d ) ), "the classic path draws the glass the classic way" );
			Assert.IsTrue( before.Draws.Any( d => d.Path == DrawCensus.Path.Classic && Stencil( d ) ), "and its refraction stencil" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.AreEqual( 0, Different( classic, driven ), "the classic pixels" );
			Assert.IsFalse( census.Draws.Any( d => d.Path == DrawCensus.Path.Classic && Glass( d ) && Forward( d ) ), $"no glass draws the classic way: {Translucent( census )}" );
			Assert.IsFalse( census.Draws.Any( d => d.Path == DrawCensus.Path.Classic && Stencil( d ) ), $"nor in the refraction stencil: {Translucent( census )}" );
			Assert.IsTrue( census.Draws.Any( d => d.Path == DrawCensus.Path.Bucket && Stencil( d ) ), $"which draws it GPU-driven: {Translucent( census )}" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A shader with no variant beside it (a package's) gets one from the library's compiler: none while it compiles,
	/// then the compiled one, cached, so another library loads it without compiling. line.shader_c stands in for both the
	/// package's shader and the variant the compiler writes.
	/// </summary>
	[TestMethod]
	public void PackageShaderVariantCompilesAtLoad()
	{
		var files = ShaderFileTests.Files;
		var compiled = files.Find( "shaders/line.shader" );
		if ( compiled is null ) Assert.Inconclusive( "line.shader isn't compiled" );

		var folder = Path.Combine( Path.GetTempPath(), $"borealis-gpudriven-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( folder, "package", "shaders" ) );
		File.Copy( compiled, Path.Combine( folder, "package", "shaders", "line.shader_c" ) );

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

				var shader = library.Load( "shaders/line.shader" );
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
				Assert.IsNotNull( library.GpuDriven( library.Load( "shaders/line.shader" ) ), "from the cache" );

				// Another compiler version's variants go
				var stale = Path.Combine( folder, "cache", "line.0000.ffffffff.gpudriven.shader_c" );
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
		RequireGpuDrivenDepth( backend );

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
		RequireGpuDrivenDepth( backend );

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
			Assert.IsTrue( Same( behindWithout, behindWith, roundingError: 1f / 2048 ), $"and the frame is the same ({Differences( behindWithout, behindWith )})" );
			Assert.IsTrue( besideCounts.Candidates >= 5 && besideCounts.Occluded == 0, $"turned, they were candidates and none was culled: nothing in front of them drew first (candidates {besideCounts.Candidates}, occluded {besideCounts.Occluded})" );
			Assert.IsTrue( Same( besideWithout, besideWith, roundingError: 1f / 2048 ), $"the frame that shows them draws them ({Differences( besideWithout, besideWith )})" );
			Assert.IsTrue( Same( behindWithout, backWith, roundingError: 1f / 2048 ), $"and turned back, the same again ({Differences( behindWithout, backWith )})" );
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
	/// with task and mesh shaders, without generated LODs and with them. With meshlets the frames resolve normals from the ids,
	/// and the ranges taken cooked get the first-corner tables the built ones do.
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
			using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, Meshlets = meshlets, MeshletTasks = meshTasks, GeneratedLods = lods, IdsNormals = meshlets };
			RequireGpuDrivenDepth( backend );

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
		Assert.IsTrue( meshletStore.CornerTableBytes > 0, "first-corner tables were uploaded" );
		Assert.AreEqual( madeStore.CornerTableBytes, meshletStore.CornerTableBytes, "as many for ranges taken cooked as for the same ranges built" );
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
	/// A model's meshlet pages go once its geometry has (a map unloaded left its three behind), and the next model's
	/// meshlets make a new one. Its draw records go too: the next model's take their slots and draw as the first did, with
	/// none of the first's meshlet runs or levels.
	/// </summary>
	[TestMethod]
	public void MeshletPagesGoWithTheirMeshes()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) Borealis.Backend.Meshlets.MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );
		if ( !Borealis.Backend.Meshlets.MeshOptimizer.Available ) Assert.Inconclusive( "meshopt isn't built" );

		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, Meshlets = true };
		RequireGpuDrivenDepth( backend );
		if ( backend.Content.TryRead( "models/citizen_props/trashcan01.vmdl" ) is not { } data ) { Assert.Inconclusive( "models/citizen_props/trashcan01.vmdl isn't built" ); return; }
		var file = Borealis.Source2.Models.ModelFile.Read( data, backend.Content.TryRead );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Meshlet pages test" );
			if ( backend.MeshletStore is null || backend.MeshletCullShader is null ) Assert.Inconclusive( "no meshlets: meshopt or borealis_meshlet_cull_cs is missing" );
			var view = new RenderView { Name = "Meshlet pages test", FieldOfView = 70, ZNear = 4, ZFar = 6000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -400, 0, 100 );
			view.Rotation = Rotation.LookAt( new Vector3( 1, 0, -0.2f ) );

			byte[] Render( RenderWorld world )
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

			(RenderWorld World, AggregateObject Aggregate, BackendModel Model, byte[] Pixels) Draw()
			{
				var mesh = BackendModel.Create( backend, file );
				var model = (BackendModel)mesh.Geometry;
				var draw = mesh.DrawsForLod( 0 ).ToArray().MaxBy( d => d.IndexCount ) with { Material = backend.CreateMaterial( "Meshlet pages", new Color( 0.7f, 0.6f, 0.5f ) ), ShadowFastPath = true };
				var baseVertex = ((IBackendGeometry)mesh.Geometry).For( draw.Mesh, draw.DrawCall ).BaseVertex;
				var fragment = new AggregateObject.FragmentDraw
				{
					Transform = Matrix3x4.From( new Transform( Vector3.Zero ) ),
					BoundsMin = mesh.Bounds.Mins,
					BoundsMax = mesh.Bounds.Maxs,
					DrawTint = Vector3.One,
					Args = new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = (uint)draw.IndexCount, InstanceCount = 1, FirstIndex = (uint)draw.StartIndex, BaseVertex = baseVertex },
				};

				var world = new RenderWorld();
				var aggregate = new AggregateObject( new RenderMesh( mesh.Geometry, mesh.Bounds, [[draw]] ), [fragment] ) { IsStatic = true };
				world.Add( aggregate );
				for ( int i = 0; i < 300 && (i < 4 || backend.MeshletStore.Pending > 0); i++ )
				{
					Render( world );
					if ( backend.MeshletStore.Pending > 0 ) Thread.Sleep( 10 );
				}

				return (world, aggregate, model, Render( world ));
			}

			var store = backend.MeshletStore;
			var tables = backend.GpuDrivenTables;
			var (world, aggregate, model, first) = Draw();
			var records = tables.DrawRecords;
			Assert.IsTrue( store.Published > 0, "the model's ranges published" );
			Assert.AreEqual( 1, store.PageCount, "its meshlets are in a page" );

			// Gone: its geometry pages are freed and their ranges forgotten, and the meshlet page goes with them
			world.Remove( aggregate );
			Render( world );
			model.Free();
			for ( int i = 0; i < 4; i++ ) Render( world );
			Assert.AreEqual( 0, store.PageCount, "the page went with the model" );
			Assert.AreEqual( 1, store.PagesReleased );

			var (_, _, again, second) = Draw();
			Assert.AreEqual( 1, store.PageCount, "the next model's meshlets make a new one" );
			Assert.IsTrue( tables.DrawRecordsReused > 0, "the next model's draw records take the first's slots" );
			Assert.AreEqual( records, tables.DrawRecords, "so the table didn't grow" );
			Assert.IsTrue( Same( first, second ), $"and it draws as the first did ({Differences( first, second )})" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			again.Free();
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Inconclusive where depth_only (the depth draws of materials drawn the classic way) has no GPU-driven form. The tests'
	/// own materials (<see cref="BackendDevice.CreateMaterial"/>) are standard surfaces, whose passes have their own.
	/// </summary>
	internal static void RequireGpuDrivenDepth( BackendDevice backend )
	{
		if ( backend.Shaders.GpuDriven( backend.Shaders.Load( "shaders/depth_only.shader" ) ) is null ) Assert.Inconclusive( "depth_only has no GPU-driven form" );
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

	internal static void Box( List<MeshVertex> vertices, List<uint> indices, Vector3 centre, float half )
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
	/// Compare RGBA16F frames with both a peak error bound and a bound on pixels that differ beyond rounding.
	/// With two Hi-Z phases, triangles that tie at a sample can be drawn in a different order; the slight change
	/// in the resolved edge color doesn't depend on whether occlusion rejected actual geometry.
	/// </summary>
	static bool Same( byte[] a, byte[] b, float maxError = 0.002f, int maxDifferentPixels = 16, float roundingError = 0 )
	{
		Assert.AreEqual( a.Length, b.Length );
		var (pixels, largest) = (0, 0f);
		for ( int p = 0; p + 8 <= a.Length; p += 8 )
		{
			var most = 0f;
			for ( int c = 0; c < 4; c++ ) most = MathF.Max( most, MathF.Abs( (float)BitConverter.ToHalf( a, p + c * 2 ) - (float)BitConverter.ToHalf( b, p + c * 2 ) ) );
			if ( most > roundingError ) pixels++;
			largest = MathF.Max( largest, most );
		}

		return largest <= maxError && pixels <= maxDifferentPixels;
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
