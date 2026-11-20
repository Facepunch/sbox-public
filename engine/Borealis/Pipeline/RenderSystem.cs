using System.Diagnostics;
using System.Linq;

namespace Borealis;

/// <summary>
/// Renders a world through Collect, Prepare, Setup and Draw phases.
/// Owns persistent features and GPU resources; <see cref="RenderFrame"/> holds per-frame state.
/// Layers are ordered in <c>RenderSystem.Layers.cs</c> and recorded by <see cref="FrameRecorder"/>.
/// </summary>
public sealed partial class RenderSystem : IDisposable
{
	static readonly StringToken TransformBufferName = new( "g_TransformBuffer" );

	readonly List<RenderFeature> features = new();
	readonly Dictionary<Type, int> featureByType = new();
	readonly GpuContext context = RenderHost.Current.Device.CreateContext();
	readonly TransformBuffer transforms = new();
	readonly ViewEnvironment environment = new();
	readonly LightBinnerFeature lightBinner = new();
	readonly MeshRenderFeature meshes = new();
	readonly AggregateRenderFeature aggregates = new();
	readonly ViewPass mainPass = new();
	readonly RenderFrame frame;
	readonly ShadowSystem shadows;
	readonly VolumetricFog fog = new();

	internal readonly OcclusionCuller Occlusion = new();
	internal readonly OcclusionHistory OcclusionHistory = new();

	/// <summary>
	/// Textures this system's renders borrow, given back when each is submitted.
	/// </summary>
	internal TransientTextures Transients { get; } = new();

	internal bool OcclusionActive { get; private set; }

	/// <summary>
	/// Persistent camera fog and history.
	/// </summary>
	internal VolumetricFog Fog => fog;

	/// <summary>
	/// Enable the depth prepass to reduce forward overdraw. Defaults to true.
	/// </summary>
	public bool DepthPrepass { get; set; } = true;

	/// <summary>
	/// Enable worker recording. When disabled, record all segments on the main thread into one context.
	/// </summary>
	public bool ParallelRecording { get; set; } = ParallelRecordingDefault;

	/// <summary>
	/// Default parallel-recording setting for new render systems.
	/// </summary>
	public static bool ParallelRecordingDefault { get; set; } = true;

	/// <summary>
	/// Lazily created recorder shared with the 3D skybox.
	/// </summary>
	FrameRecorder recorder;

	/// <summary>
	/// Enable sun and local-light shadow maps. Defaults to true.
	/// </summary>
	public bool Shadows { get; set; } = true;

	/// <summary>
	/// Draw the world's 2D sky. Defaults to true, matching <c>r_drawskybox</c>.
	/// </summary>
	public bool DrawSky { get; set; } = true;

	/// <summary>
	/// This frame's shadow views and shadow constants.
	/// </summary>
	internal ShadowSystem ShadowMaps => shadows;

	/// <summary>
	/// Last collected and prepared main-view pass.
	/// </summary>
	internal ViewPass MainPass => mainPass;

	/// <summary>
	/// Prepared frame transforms.
	/// </summary>
	internal TransformBuffer Transforms => transforms;

	/// <summary>
	/// Statistics from the last render.
	/// </summary>
	public RenderStats Stats { get; private set; }

	/// <summary>
	/// Counts renders, by any system, from 1: what objects bring per-render state up to date against
	/// (<see cref="MeshObject.RefreshAttributes"/>).
	/// </summary>
	internal static int RenderNumber { get; private set; } = 1;

	/// <summary>
	/// Make the shaders render systems share now, on the main thread, before anything renders. Layers otherwise make them
	/// as they first render, and the first frames can draw differently.
	/// </summary>
	public static void WarmUp()
	{
		SharedPasses.WarmUp();
		Gpu.ContactShadows.WarmUp();
		OutputFlipLayer.WarmUp();
		Borealis.Shadows.ShadowSystem.WarmUp();
	}

	/// <summary>
	/// Create standard features and validate GPU struct layouts against native sizes.
	/// </summary>
	public RenderSystem()
	{
		ViewConstants.ValidateLayout();
		LightBinnerFeature.GpuLight.ValidateLayout();
		LightBinnerFeature.GpuEnvMap.ValidateLayout();
		LightBinnerFeature.GpuDecal.ValidateLayout();
		AggregateObject.FragmentDraw.ValidateLayout();
		shadows = new ShadowSystem( this );
		frame = new RenderFrame( this );

		// Lights first - they're binned before anything draws
		AddFeature( lightBinner );
		AddFeature( meshes );
		AddFeature( aggregates );

		// Opaque custom objects after the meshes and aggregates, as native draws the world before dynamic objects
		AddFeature( new LateCustomFeature( meshes ) );

		// Static mesh draws join the aggregates' GPU-driven call, after the mesh feature prepares each view
		aggregates.Meshes = meshes;
		meshes.Aggregates = aggregates;
	}

	/// <summary>
	/// The feature of a given type, or null.
	/// </summary>
	internal T GetFeature<T>() where T : RenderFeature => features.OfType<T>().FirstOrDefault();

	/// <summary>
	/// Append a feature. Earlier features take priority for accepted object types.
	/// </summary>
	internal void AddFeature( RenderFeature feature )
	{
		ArgumentNullException.ThrowIfNull( feature );

		feature.Index = features.Count;
		features.Add( feature );
		featureByType.Clear();
	}

	/// <summary>
	/// Reserve reverse-Z depth 0–0.02 for the skybox (<c>CRenderingPipelineStandard::GetAdjustedViewport</c>).
	/// </summary>
	internal const float MainWorldMinZ = 0.02f;

	/// <summary>
	/// The depth format its views draw into: 32-bit float (reverse-Z), with an 8-bit stencil once something it drew needed
	/// one (<see cref="GpuMaterial.NeedsStencil"/>), or always on a device that can't tell (<see cref="GpuDevice.KnowsStencilUse"/>).
	/// A view whose prepared draws need a stencil gets it before it records (<see cref="GiveStencil"/>) and keeps it, so
	/// nothing is made again from frame to frame.
	/// </summary>
	internal TextureFormat DepthFormat => stencil || StencilAlways || RenderHost.Current.Device is not { KnowsStencilUse: true } ? TextureFormat.D32FS8 : TextureFormat.D32;

	/// <summary><c>-depth-stencil</c>: every view's depth has a stencil from the start, as before, for comparisons.</summary>
	internal static readonly bool StencilAlways = Array.Exists( Environment.GetCommandLineArgs(), a => a.Equals( "-depth-stencil", StringComparison.OrdinalIgnoreCase ) );

	// Something this system drew needed a stencil: its views' depth keeps one from then on
	bool stencil;

	/// <summary>
	/// Before the frame records: if its prepared draws (its own or its 3D skybox's) need a stencil, the depth it draws into
	/// gets one, made again once if it has none, and keeps it. A depth the caller owns (<see cref="ViewTarget"/> over its
	/// textures) is left as it is.
	/// </summary>
	void GiveStencil( RenderFrame skyFrame )
	{
		if ( RenderHost.Current.Device is not { KnowsStencilUse: true } ) return;
		if ( !stencil && (meshes.WantsStencil( mainPass ) || (skyFrame is not null && skybox.meshes.WantsStencil( skyFrame.MainPass ))) ) stencil = true;
		if ( !stencil || frame.Output.Target is not { } target || target.Depth?.Format.HasStencil() != false ) return;

		// A flipped view draws with the view's depth through a target of its own
		if ( target == flipTarget && flipSource is { } source )
		{
			if ( !source.GiveStencil() ) return;
			flipTarget = new ViewTarget( flipTarget.Color, source.Depth, flipResolved );
			frame.Output = frame.Output with { Target = flipTarget };
			return;
		}

		target.GiveStencil();
	}

	/// <summary>
	/// Blend in an HDR intermediate, then copy to the texture for final stages (<c>CCameraRenderer::RenderToLayer</c>).
	/// </summary>
	internal void Render( RenderWorld world, RenderView view, GpuTexture output, int samples, IRenderStages stages )
	{
		ArgumentNullException.ThrowIfNull( output );

		var size = new Vector2Int( output.Width, output.Height );
		if ( textureTarget is null || textureTarget.Size != size || textureTarget.Samples != samples )
		{
			textureTarget?.Dispose();
			textureTarget = ViewTarget.Create( size, samples, TextureFormat.RGBA16161616F, DepthFormat, "Borealis output" );
		}

		// Final stages use MSAA scratch, resolved after all drawing.
		GpuTexture scratch = null;
		if ( samples > 1 )
		{
			if ( textureScratch is null || textureScratch.Size != size || textureScratch.Format != output.Format || textureScratch.Samples != samples )
			{
				textureScratch?.Dispose();
				textureScratch = RenderHost.Current.Device.CreateTexture( TextureDesc.Target( size.x, size.y, output.Format, samples ), "Borealis output scratch" );
			}

			scratch = textureScratch;
		}

		// A flipped view draws into a texture of its own, then into the output mirrored
		var flipped = view.FlipX || view.FlipY;
		var drawn = flipped ? FlipColor( size, output.Format, 1 ) : output;
		Render( world, view, RenderOutput.ForTexture( textureTarget, drawn, scratch ) with { FlipInto = flipped ? output : null, FlipX = view.FlipX, FlipY = view.FlipY }, stages );
	}

	ViewTarget textureTarget;
	GpuTexture textureScratch;

	// What a flipped view draws into before it's mirrored into its output (RenderOutput.FlipInto)
	GpuTexture flipColor, flipResolved;
	ViewTarget flipTarget, flipSource;

	/// <summary>
	/// The colour a flipped view draws into, made again when the size, format or sample count changes.
	/// </summary>
	GpuTexture FlipColor( Vector2Int size, TextureFormat format, int samples )
	{
		if ( flipColor is { } existing && existing.Size == size && existing.Format == format && existing.Samples == samples ) return existing;

		// The device retains resources used by in-flight frames.
		flipColor?.Dispose();
		flipResolved?.Dispose();
		flipResolved = null;
		flipTarget = null;

		var device = RenderHost.Current.Device;
		flipColor = device.CreateTexture( TextureDesc.Target( size.x, size.y, format, samples ), "Borealis flipped output" );
		if ( samples > 1 ) flipResolved = device.CreateTexture( TextureDesc.Target( size.x, size.y, format ), "Borealis flipped output resolved" );
		return flipColor;
	}

	/// <summary>
	/// A flipped view of a target: colour of its own in the target's format, with the target's depth, which stays as drawn.
	/// </summary>
	RenderOutput FlippedOutput( ViewTarget target, RenderView view )
	{
		var color = FlipColor( target.Size, target.Color.Format, target.Samples );
		if ( flipTarget is null || flipTarget.Depth != target.Depth ) flipTarget = new ViewTarget( color, target.Depth, flipResolved );
		flipSource = target;
		return RenderOutput.ForTarget( flipTarget ) with { FlipInto = target.Resolved, FlipX = view.FlipX, FlipY = view.FlipY };
	}

	/// <summary>
	/// Render and submit to a target. The viewport uses target pixels; MSAA resolves into <see cref="ViewTarget.Resolved"/>.
	/// </summary>
	public void Render( RenderWorld world, RenderView view, ViewTarget target )
	{
		ArgumentNullException.ThrowIfNull( target );
		if ( target.Color is null ) throw new ObjectDisposedException( nameof( ViewTarget ) );

		Render( world, view, RenderOutput.ForTarget( target ) );
	}

	/// <summary>
	/// Render into a target, drawing <paramref name="stages"/> at native's stage points. A frame shown in a window
	/// (<paramref name="window"/>) gives the host its plan and debug views' report (<see cref="RenderOutput.Window"/>).
	/// </summary>
	internal void Render( RenderWorld world, RenderView view, ViewTarget target, IRenderStages stages, bool window = false )
	{
		ArgumentNullException.ThrowIfNull( target );
		if ( target.Color is null ) throw new ObjectDisposedException( nameof( ViewTarget ) );

		var output = view.FlipX || view.FlipY ? FlippedOutput( target, view ) : RenderOutput.ForTarget( target );
		Render( world, view, output with { Window = window }, stages );
	}

	void Render( RenderWorld world, RenderView view, in RenderOutput output, IRenderStages stages = null )
	{
		ArgumentNullException.ThrowIfNull( world );

		// Not while another render of the world is under way (a child view's): its frame reads them
		if ( world.Rendering == 0 ) world.RefreshDrawnVolumes();
		world.Rendering++;
		try
		{
			Record( world, view, output, stages );
		}
		finally
		{
			world.Rendering--;
		}
	}

	void Record( RenderWorld world, RenderView view, in RenderOutput output, IRenderStages stages )
	{
		using var rendererAllocations = Borealis.RendererAllocations.Measure();
		ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( view );

		var stats = new RenderStats { Objects = world.Count };
		RenderNumber++;

		view.Update();
		frame.Begin( world, view, output, stages );
		BloomObjects = null;

		var allocStart = GC.GetAllocatedBytesForCurrentThread();
		var start = Stopwatch.GetTimestamp();
		using ( Zones.Collect.Start() ) Collect( world, view, ref stats );
		var collected = Stopwatch.GetTimestamp();
		var allocCollected = GC.GetAllocatedBytesForCurrentThread();

		try
		{
			using ( Zones.Prepare.Start() ) Prepare( world, view );
		}
		catch
		{
			shadows.Abandon();
			throw;
		}

		stats.Lights = lightBinner.Count;
		if ( frame.DebugViews != DebugViews.None ) DescribeCulling( world, view, stats );

		var prepared = Stopwatch.GetTimestamp();
		var allocPrepared = GC.GetAllocatedBytesForCurrentThread();

		lap = prepared;

		// Update fog settings, history and resources.
		fog.BeginFrame( world.Lighting.VolumetricFog, world.FogVolumes.Count > 0 && !view.Reflection );

		// Acquire the mask before setup writes its index to sun constants.
		_ = Layers;
		frame.ContactShadowMask = contactShadows?.MaskFor( frame );
		frame.Normals = reflections?.NormalsFor( frame );
		if ( frame.Output.FlipInto is not null ) OutputFlipLayer.WarmUp();

		// Plan first: serial recording shares the frame context with skybox setup.
		var skyFrame = frame.Skybox;
		GiveStencil( skyFrame );
		recorder ??= new FrameRecorder();
		var host = RenderHost.Current;
		recorder.Plan( frame, ParallelRecording, host.AsyncCompute );

		// The dev tools's frame plan: frames drawn into a window show their plan, published with its times
		var plan = host.WantsFramePlan && output.Window ? recorder.Snapshot( view.Name ) : null;
		recorder.Measuring = plan is not null;
		if ( plan is not null )
		{
			plan.StartTicks = start;
			plan.MainThread = Environment.CurrentManagedThreadId;
		}

		// Setup merges the camera's attributes into the frame's parameters, so each frame starts them empty: one the camera
		// stops setting mustn't keep its last value. Native makes a view's attributes again every frame.
		context.Attributes?.Clear();
		context.Begin();
		var submitted = false;
		try
		{
			// Skinning can be recorded on a worker only when the frame's draws aren't on the setup context it must precede
			meshes.SkinOnWorker = recorder.Parallel;
			if ( skybox is not null ) skybox.meshes.SkinOnWorker = recorder.Parallel;

			using ( Zones.Setup.Start() ) Setup( frame );

			if ( (frame.DebugViews & DebugViews.ShadowMaps) != 0 ) shadows.Describe( DebugReport, view.Viewport );

			// Submit skybox setup after main setup, before draws.
			if ( skyFrame is not null )
			{
				skybox.context.Attributes?.Clear();
				skybox.context.Begin( recorder.SeparateContexts ? null : context );
				skybox.Setup( skyFrame );
			}
			stats.SetupMs = Lap();

			// Frame attributes are read-only during recording.
			using ( Zones.Record.Start() ) recorder.Record( ref stats );
			if ( plan is not null ) recorder.FillTimes( plan );
			RenderStats.AddCounts( ref stats, frame.Recorded );
			if ( skyFrame is not null )
			{
				var skyStats = skybox.Stats;
				RenderStats.AddCounts( ref skyStats, skyFrame.Recorded );
				skybox.Stats = skyStats;
				RenderStats.AddCounts( ref stats, skyStats );
			}
			Lap();

			using var _ = Zones.Submit.Start();
			var handedOver = Stopwatch.GetTimestamp();

			// When the window's frame was handed over, so the dev tools can place its plan on their timeline
			var tag = output.Window ? host.Submitting( handedOver, view ) : 0;
			if ( plan is not null ) { plan.SubmitTicks = handedOver; plan.SubmitTag = tag; plan.View = view; }
			recorder.Submit();
			submitted = true;
			shadows.Submitted();
			skybox?.shadows.Submitted();

			// A skybox whose frame wasn't planned drew nothing with its setup work
			skybox?.EndSetupWork( true );
		}
		finally
		{
			if ( !submitted )
			{
				EndSetupWork( false );
				skybox?.EndSetupWork( false );
				recorder.End();
				context.End();

				// The cache mustn't claim maps that were never drawn
				shadows.Abandon();
			}

			recorder.Clear();
			frame.End();
			skyFrame?.End();
			Transients.EndRender();
			skybox?.Transients.EndRender();
			RenderHost.Current.Device.EndSubmitting();
		}
		stats.SubmitMs = Lap();


		var drawn = Stopwatch.GetTimestamp();
		var allocDrawn = GC.GetAllocatedBytesForCurrentThread();

		// Track steady-state allocations.
		stats.CollectAllocBytes = allocCollected - allocStart;
		stats.PrepareAllocBytes = allocPrepared - allocCollected;
		stats.DrawAllocBytes = allocDrawn - allocPrepared;

		stats.CollectMs = Stopwatch.GetElapsedTime( start, collected ).TotalMilliseconds;
		stats.PrepareMs = Stopwatch.GetElapsedTime( collected, prepared ).TotalMilliseconds;
		stats.DrawMs = Stopwatch.GetElapsedTime( prepared, drawn ).TotalMilliseconds;
		if ( OcclusionActive )
			stats.ObjectsOcclusionCulled = Occlusion.Culled;
		Stats = stats;

		if ( plan is not null )
		{
			plan.CollectMs = stats.CollectMs;
			plan.PrepareMs = stats.PrepareMs;
			plan.SetupMs = stats.SetupMs;
			plan.RecordMs = stats.RecordMs;
			plan.RecordWaitMs = stats.RecordWaitMs;
			plan.SubmitMs = stats.SubmitMs;
			plan.WorkerMs = stats.WorkerRecordMs;
			host.PublishFramePlan( plan );
		}

		host.Rendered( stats );

		// Copied, so the frames this system renders into textures don't change what the host shows
		if ( frame.DebugViews != DebugViews.None && output.Window )
		{
			DebugReport.CopyTo( publishedReport );
			host.PublishDebugViews( publishedReport );
		}
	}

	/// <summary>
	/// What the debug views found this frame, for <see cref="RenderHost.PublishDebugViews"/>.
	/// </summary>
	internal DebugViewReport DebugReport { get; } = new();

	readonly DebugViewReport publishedReport = new();

	/// <summary>
	/// Start this frame's debug report with the main view's culling, and the LODs its meshes drew at.
	/// </summary>
	void DescribeCulling( RenderWorld world, RenderView view, in RenderStats stats )
	{
		var report = DebugReport;
		report.Clear();
		report.Views = frame.DebugViews;
		report.ViewName = view.Name;
		report.Viewport = view.Viewport;
		report.Frame = RenderHost.Current.FrameCount;
		report.Objects = world.Count;
		report.Visible = stats.ObjectsVisible;
		report.Culled[(int)CullReason.Frustum] = stats.ObjectsFrustumCulled;
		report.Culled[(int)CullReason.Size] = stats.ObjectsSizeCulled;
		report.Culled[(int)CullReason.Tags] = stats.ObjectsTagCulled;
		report.OcclusionCulling = OcclusionActive;
		report.Occluded = OcclusionActive ? Occlusion.Culled : 0;

		var reasons = mainPass.CullReasons;
		var objects = world.Objects;
		foreach ( var index in mainPass.Visible( meshes ) )
		{
			if ( objects[index] is not MeshObject { Mesh: { } mesh } ) continue;
			if ( reasons is not null && reasons[index] != 0 ) continue;

			if ( mesh.LodCount > 1 ) report.Lods[Math.Min( meshes.LodOf( index ), DebugViewReport.MaxLods - 1 )]++;
			else report.SingleLod++;
		}
	}

	/// <summary>
	/// Set constants and resources, then upload and dispatch before any render pass.
	/// Skybox ambient and fog come from <see cref="RenderFrame.Space"/>.
	/// </summary>
	void Setup( RenderFrame frame )
	{
		var world = frame.World;
		var view = frame.View;

		// Camera attributes precede pipeline overrides.
		view.CameraAttributes?.MergeTo( context.Attributes );
		view.ViewAttributes?.MergeTo( context.Attributes );
		frame.SetViewConstants( context );

		// Create shadow maps before writing their indices into sun constants.
		shadows.Setup( context, frame.ContactShadowMask );
		// Native uses no high-precision lighting offset for skyboxes.
		environment.Apply( context, world.Lighting, lightBinner, shadows.Directional, frame.Space, isSkybox ? default : view.Position, isSkybox ? null : fog, view );

		// Per-view debug mode (ToolsVis.hlsl, SetupShadingMode): the shaders' modes, not the engine's tools passes
		context.Attributes.Set( ToolsVisModeName, ToolsViews.ShaderMode( view ) );

		// Over the camera's attributes, as CameraRenderer.Configure sets it
		context.Attributes.Set( LightModulatorsDisabledName, !view.LightModulators );

		// No normals G-buffer: Normals::Sample rebuilds normals from depth and Roughness::Sample answers 1, for every view,
		// since the frame's attributes outlive the frame
		context.Attributes.Set( NormalsTextureIndexName, -1 );

		// A reflection's, over the camera's (CameraRenderer.Configure): nothing in it reads prepass normals
		if ( view.Reflection )
		{
			context.Attributes.Set( IsReflectionName, true );
			context.Attributes.Set( ReflectionShadowsName, view.ReflectionShadows );
			context.Attributes.Set( NeedsDepthNormalsName, false );
		}

		// Glass reads the native 3D skybox pipeline's combo (Skybox3DPipeline).
		if ( isSkybox ) context.Attributes.SetCombo( SkyboxCombo, 1 );

		for ( int i = 0; i < features.Count; i++ )
			features[i].BeforeUpload( context, transforms );

		var scope = context.BeginGpuScope( "Setup" );
		transforms.Upload( context );
		context.Attributes.Set( TransformBufferName, transforms.Current );
		context.InstanceIds = transforms.InstanceIds;
		context.Occlusion = null;
		if ( OcclusionActive )
		{
			Occlusion.Upload( context, transforms.InstanceCount );
			context.Occlusion = Occlusion;
		}

		for ( int i = 0; i < features.Count; i++ )
		{
			// An oblique projection's near plane leans with its clip plane, and light clusters' rays meet it behind the camera,
			// or nowhere where they run along it: bin lights with the view's near plane square to it
			var square = features[i] == lightBinner && view.Projection is not null;
			if ( square ) frame.UseSquareConstants( context );
			features[i].Setup( context, world, mainPass );
			if ( square ) frame.UseFrameConstants( context );
		}

		// The light binner writes the view's set before its culling reads it; a frame without one writes it here
		context.WriteViewSet();
		context.EndGpuScope( scope );

		for ( int i = 0; i < features.Count; i++ )
			features[i].BeginSetupWork();
	}

	/// <summary>
	/// Finish the features' setup work on workers: submit it right after this system's setup context, or discard it.
	/// </summary>
	internal void EndSetupWork( bool submit )
	{
		for ( int i = 0; i < features.Count; i++ )
			features[i].EndSetupWork( submit );
	}

	long lap;
	static readonly StringToken ToolsVisModeName = new( "ToolsVisMode" );
	static readonly StringToken LightModulatorsDisabledName = new( "LightModulatorsDisabled" );
	static readonly StringToken IsReflectionName = new( "IsReflection" );
	static readonly StringToken ReflectionShadowsName = new( "ReflectionShadows" );
	static readonly StringToken NeedsDepthNormalsName = new( "NeedsDepthNormals" );
	static readonly StringToken NormalsTextureIndexName = new( "NormalsTextureIndex" );

	/// <summary>
	/// Current or last rendered frame.
	/// </summary>
	internal RenderFrame Frame => frame;

	/// <summary>
	/// Setup context and parent attributes for recording segments.
	/// </summary>
	internal GpuContext FrameContext => context;

	/// <summary>
	/// The quarter-resolution bloom objects the last frame drew (<c>QuarterResEffectsBloomInputTexture</c>), or null when it drew
	/// none: what a camera's bloom effect after the frame adds. Valid until this system renders again.
	/// </summary>
	internal GpuTexture BloomObjects { get; set; }

	/// <summary>
	/// Milliseconds since the last lap, starting the next.
	/// </summary>
	double Lap()
	{
		var now = Stopwatch.GetTimestamp();
		var ms = Stopwatch.GetElapsedTime( lap, now ).TotalMilliseconds;
		lap = now;
		return ms;
	}

	/// <summary>
	/// Total runs across features for this view and layer.
	/// </summary>
	internal int RunCount( ViewPass view, MeshLayer layer )
	{
		var runs = 0;
		for ( int i = 0; i < features.Count; i++ )
			runs += features[i].RunCount( view, layer );
		return runs;
	}

	/// <summary>
	/// Write estimated draw counts per run, in feature order.
	/// </summary>
	internal void RunWeights( ViewPass view, MeshLayer layer, Span<int> weights )
	{
		var start = 0;
		for ( int i = 0; i < features.Count; i++ )
		{
			var runs = features[i].RunCount( view, layer );
			if ( runs > 0 ) features[i].RunWeights( view, layer, weights.Slice( start, runs ) );
			start += runs;
		}
	}

	/// <summary>
	/// Mark the runs that must record on the main thread, in feature order, as <see cref="RunWeights"/> lays them out.
	/// </summary>
	internal void MainThreadRuns( ViewPass view, MeshLayer layer, Span<bool> mainThread )
	{
		var start = 0;
		for ( int i = 0; i < features.Count; i++ )
		{
			var runs = features[i].RunCount( view, layer );
			if ( runs > 0 ) features[i].MainThreadRuns( view, layer, mainThread.Slice( start, runs ) );
			start += runs;
		}
	}

	/// <summary>
	/// Whether every feature can record <paramref name="view"/>'s <paramref name="layer"/> on a worker thread.
	/// </summary>
	internal bool CanRecordOffMainThread( ViewPass view, MeshLayer layer )
	{
		for ( int i = 0; i < features.Count; i++ )
		{
			if ( !features[i].CanRecordOffMainThread( view, layer ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// Whether the main view's selected runs read the frame-buffer copy.
	/// </summary>
	internal bool ReadsFrameBuffer( MeshRuns runs )
	{
		for ( int i = 0; i < features.Count; i++ )
		{
			if ( features[i].ReadsFrameBuffer( mainPass, runs ) ) return true;
		}

		return false;
	}

	GpuTexture frameBufferCopy;

	/// <summary>
	/// Persistent mipmapped RGBA16F copy (<c>FindOrCreateFrameBufferScratchTexture</c>).
	/// Created on the main thread; the depth-aware mask reads the previous frame's second mip.
	/// </summary>
	internal GpuTexture FrameBufferCopy( Rect viewport )
	{
		var width = (int)viewport.Width;
		var height = (int)viewport.Height;
		if ( frameBufferCopy is { } existing && existing.Width == width && existing.Height == height ) return existing;

		// The device retains resources used by in-flight frames.
		frameBufferCopy?.Dispose();
		var mips = (int)MathF.Log2( Math.Min( width, height ) ) + 1;

		// Keep the general layout for compute mip writes and sampling.
		var desc = TextureDesc.Target( width, height, TextureFormat.RGBA16161616F, mips: mips ) with { Storage = true, GenerateMips = true };
		frameBufferCopy = RenderHost.Current.Device.CreateTexture( desc, "Borealis frame buffer copy" );
		return frameBufferCopy;
	}

	/// <summary>
	/// Draw a run range in feature order. May execute on a worker thread.
	/// </summary>
	internal void DrawRange( GpuContext rc, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats )
	{
		// A colour layer of a view in a geometric debug view draws every draw with the tools pass (ToolsViews)
		if ( rc is null )
		{
			DrawFeatures( rc, view, layer, first, count, ref stats );
			return;
		}

		rc.ToolsPass = layer.TakesToolsPass && !view.IsShadow ? ToolsViews.PassOf( view.View ) : ToolsPass.None;
		try
		{
			DrawFeatures( rc, view, layer, first, count, ref stats );
		}
		finally
		{
			rc.ToolsPass = ToolsPass.None;
		}
	}

	void DrawFeatures( GpuContext rc, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats )
	{
		var end = first + count;
		var start = 0;
		for ( int i = 0; i < features.Count && start < end; i++ )
		{
			var runs = features[i].RunCount( view, layer );
			var from = Math.Max( first - start, 0 );
			var to = Math.Min( end - start, runs );
			if ( from < to ) features[i].Draw( rc, view, layer, from, to - from, ref stats );
			start += runs;
		}
	}

	/// <summary>
	/// Prepare main-view runs and transforms, shadow views, then the 3D skybox.
	/// </summary>
	internal void Prepare( RenderWorld world, RenderView view )
	{
		// Begun by the collect when it left resident objects out
		transforms.World = world;
		if ( !collectedResident ) transforms.Clear();
		collectedResident = false;

		// Shadows, skyboxes and views without a prepass never cull
		var wanted = RenderHost.Current.OcclusionCulling && DepthPrepass && !isSkybox && !view.Capture;
		Occlusion.Begin();
		meshes.Occlusion = wanted ? Occlusion : null;
		aggregates.Occlusion = meshes.Occlusion;
		meshes.Parallel = ParallelRecording;
		meshes.DebugViews = frame.DebugViews;

		// Lights first: shadow views are chosen from them. The shadow views then cull on workers while the main view's
		// other features prepare here - culling only reads what preparing doesn't write
		using ( Zones.LightPrepare.Start() ) lightBinner.Prepare( world, mainPass, transforms );
		shadows.Select( world, mainPass, lightBinner, features.Count, Shadows, isSkybox );

		// Resident casters reach the shadow views through their resident lists: the culls leave them out
		aggregates.LeaveResidentOut( world, shadows.Passes, transforms );
		var shadowCull = shadows.BeginCull();
		try
		{
			for ( int i = 0; i < features.Count; i++ )
			{
				if ( features[i] != lightBinner ) features[i].Prepare( world, mainPass, transforms );
			}
		}
		finally
		{
			shadowCull.Join();
		}

		meshes.Occlusion = null;
		aggregates.Occlusion = null;
		OcclusionActive = wanted && Occlusion.InstanceCount > 0;
		OcclusionHistory.BeginFrame( OcclusionActive, view.Viewport, (frame.DebugViews & DebugViews.CullReasons) != 0 );
		Occlusion.Previous = OcclusionHistory.EarlyDepth;
		Occlusion.RejectEarly = OcclusionHistory.RejectsEarly;

		using ( Zones.ShadowPrepare.Start() ) shadows.PreparePasses( transforms );

		// Skyboxes use their own system and view.
		using ( Zones.SkyboxPrepare.Start() ) PrepareSkybox( world, view );
	}

	internal void Prepare( RenderWorld world, ViewPass view, TransformBuffer transforms )
	{
		for ( int i = 0; i < features.Count; i++ )
		{
			if ( view.IsShadow && !features[i].DrawsShadows ) continue;
			features[i].Prepare( world, view, transforms );
		}
	}

	/// <summary>
	/// Whether a prepared view draws from a resident list, besides what its visible lists hold.
	/// </summary>
	internal bool DrawsResident( ViewPass view ) => aggregates.DrawsResident( view );

	/// <summary>
	/// Release owned GPU resources. The device retains those used by in-flight frames.
	/// </summary>
	public void Dispose()
	{
		Transients.Dispose();
		skybox?.Dispose();
		skybox = null;
		shadows.Dispose();
		fog.Dispose();
		textureTarget?.Dispose();
		textureScratch?.Dispose();
		textureTarget = null;
		flipColor?.Dispose();
		flipResolved?.Dispose();
		flipColor = flipResolved = null;
		flipTarget = null;
		frameBufferCopy?.Dispose();
		frameBufferCopy = null;
		transforms.Dispose();
		environment.Dispose();
		Occlusion.Dispose();
		OcclusionHistory.Dispose();

		foreach ( var feature in features )
			feature.Dispose();

		if ( layers is not null )
		{
			foreach ( var layer in layers )
				layer.Dispose();
		}

		// Segment contexts own native attributes.
		recorder?.Dispose();
		recorder = null;

		context.Dispose();
	}
}
