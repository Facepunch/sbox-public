using NativeEngine;
using Sandbox.Rendering;
using System.Diagnostics;
using System.Linq;

namespace Sandbox.SceneRenderer;

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
	readonly RenderContext context = new();
	readonly TransformBuffer transforms = new();
	readonly ViewEnvironment environment = new();
	readonly LightBinnerFeature lightBinner = new();
	readonly MeshRenderFeature meshes = new();
	readonly ViewPass mainPass = new();
	readonly RenderFrame frame;
	readonly ShadowSystem shadows;
	readonly VolumetricFog fog = new();

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
	/// Swap chain used only for GPU timing of texture renders. Must be named by the first submission.
	/// </summary>
	internal SwapChainHandle_t TimingSwapChain { get; set; }

	/// <summary>
	/// Statistics from the last render.
	/// </summary>
	public RenderStats Stats { get; private set; }

	/// <summary>
	/// Create standard features and validate GPU struct layouts against native sizes.
	/// </summary>
	public RenderSystem()
	{
		ViewConstants.ValidateLayout();
		LightBinnerFeature.GpuLight.ValidateLayout();
		LightBinnerFeature.GpuEnvMap.ValidateLayout();
		LightBinnerFeature.GpuDecal.ValidateLayout();
		shadows = new ShadowSystem( this );
		frame = new RenderFrame( this );

		// Lights first - they're binned before anything draws
		AddFeature( lightBinner );
		AddFeature( meshes );
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
	/// Render a view of a world into a swap chain, and submit it. The caller presents.
	/// </summary>
	internal void Render( RenderWorld world, RenderView view, SwapChainHandle_t swapChain, Vector2 targetSize, IRenderStages stages = null )
	{
		Render( world, view, RenderOutput.ForSwapChain( SwapChainTarget( swapChain, targetSize ), swapChain ), stages );
	}

	/// <summary>
	/// HDR intermediate matching swap-chain size and MSAA, with D32FS8 depth (<c>intermediate_color_stereo_rt</c>).
	/// </summary>
	ViewTarget SwapChainTarget( SwapChainHandle_t swapChain, Vector2 size )
	{
		var pixels = new Vector2Int( (int)size.x, (int)size.y );
		var msaa = RenderOutput.SwapChainMultisample( swapChain );
		if ( swapChainTarget is { } existing && existing.Size == pixels && existing.Color.MultisampleType == msaa )
			return existing;

		// The device retains resources used by in-flight frames.
		swapChainTarget?.Dispose();
		swapChainTarget = ViewTarget.Create( pixels, msaa.FromEngine(), ImageFormat.RGBA16161616F, ImageFormat.D32FS8, "SceneRenderer swap chain" );
		return swapChainTarget;
	}

	ViewTarget swapChainTarget;

	/// <summary>
	/// Blend in an HDR intermediate, then copy to the texture for final stages (<c>CCameraRenderer::RenderToLayer</c>).
	/// </summary>
	internal void Render( RenderWorld world, RenderView view, Texture output, MultisampleAmount msaa, IRenderStages stages )
	{
		ArgumentNullException.ThrowIfNull( output );

		var size = new Vector2Int( output.Width, output.Height );
		if ( textureTarget is null || textureTarget.Size != size || textureTarget.Color.MultisampleType != msaa.ToEngine() )
		{
			textureTarget?.Dispose();
			textureTarget = ViewTarget.Create( size, msaa, ImageFormat.RGBA16161616F, ImageFormat.D32FS8, "SceneRenderer output" );
		}

		// Final stages use MSAA scratch, resolved after all drawing.
		Texture scratch = null;
		if ( msaa != MultisampleAmount.MultisampleNone )
		{
			if ( textureScratch is null || textureScratch.Width != size.x || textureScratch.Height != size.y || textureScratch.ImageFormat != output.ImageFormat || textureScratch.MultisampleType != msaa.ToEngine() )
			{
				textureScratch?.Dispose();
				textureScratch = Texture.CreateRenderTarget().WithSize( size.x, size.y ).WithFormat( output.ImageFormat ).WithMSAA( msaa ).Create( "SceneRenderer output scratch" );
			}

			scratch = textureScratch;
		}

		Render( world, view, RenderOutput.ForTexture( textureTarget, output, scratch ), stages );
	}

	ViewTarget textureTarget;
	Texture textureScratch;

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
	/// Render into a target, drawing <paramref name="stages"/> at native's stage points.
	/// </summary>
	internal void Render( RenderWorld world, RenderView view, ViewTarget target, IRenderStages stages )
	{
		ArgumentNullException.ThrowIfNull( target );
		if ( target.Color is null ) throw new ObjectDisposedException( nameof( ViewTarget ) );

		Render( world, view, RenderOutput.ForTarget( target ), stages );
	}

	void Render( RenderWorld world, RenderView view, in RenderOutput output, IRenderStages stages = null )
	{
		ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( view );

		var stats = new RenderStats { Objects = world.Count };

		view.Update();
		frame.Begin( world, view, output, stages );

		var allocStart = GC.GetAllocatedBytesForCurrentThread();
		var start = Stopwatch.GetTimestamp();
		using ( Zones.Collect.Start() ) Collect( world, view, ref stats );
		var collected = Stopwatch.GetTimestamp();
		var allocCollected = GC.GetAllocatedBytesForCurrentThread();

		using ( Zones.Prepare.Start() ) Prepare( world, view );
		stats.Lights = lightBinner.Count;

		var prepared = Stopwatch.GetTimestamp();
		var allocPrepared = GC.GetAllocatedBytesForCurrentThread();

		lap = prepared;

		// Update fog settings, history and resources.
		fog.BeginFrame( world.Lighting.VolumetricFog, world.FogVolumes.Count > 0 );

		// Acquire the mask before setup writes its index to sun constants.
		_ = Layers;
		frame.ContactShadowMask = contactShadows?.MaskFor( frame );

		// Plan first: serial recording shares the frame context with skybox setup.
		var skyFrame = frame.Skybox;
		recorder ??= new FrameRecorder();
		recorder.Plan( frame, ParallelRecording, Sandbox.Rendering.ManagedSceneRendering.AsyncCompute );

		// overlay_scene_plan, or SceneLab's plan overlay: frames drawn into a window show their plan, published with its times
		var plan = Sandbox.Rendering.ManagedFramePlan.Wanted && (IntPtr)output.SwapChain != IntPtr.Zero ? recorder.Snapshot( view.Name ) : null;

		context.Begin();
		var submitted = false;
		try
		{
			using ( Zones.Setup.Start() ) Setup( frame );

			// Submit skybox setup after main setup, before draws.
			if ( skyFrame is not null )
			{
				skybox.context.Begin( recorder.Parallel ? null : context );
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
			recorder.Submit( (IntPtr)output.SwapChain != IntPtr.Zero ? output.SwapChain : TimingSwapChain );
			submitted = true;
		}
		finally
		{
			if ( !submitted )
			{
				recorder.End();
				context.End();
			}

			recorder.Clear();
			frame.End();
			skyFrame?.End();
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
			plan.Publish();
		}

		// Native counters omit managed draws; report them for overlay_frame.
		Sandbox.Rendering.ManagedSceneRendering.Report( new Sandbox.Rendering.ManagedFrameCounters
		{
			Renders = 1,
			Objects = stats.Objects,
			ObjectsVisible = stats.ObjectsVisible,
			ObjectsSizeCulled = stats.ObjectsSizeCulled,
			Draws = stats.Draws,
			DepthDraws = stats.DepthDraws,
			TranslucentDraws = stats.TranslucentDraws,
			ShadowDraws = stats.ShadowDraws,
			CustomDraws = stats.CustomDraws,
			Instances = stats.Instances,
			Triangles = stats.Triangles,
			Lights = stats.Lights,
			ShadowViews = stats.ShadowViews,
			CollectMs = stats.CollectMs,
			PrepareMs = stats.PrepareMs,
			SetupMs = stats.SetupMs,
			RecordMs = stats.RecordMs,
			RecordWaitMs = stats.RecordWaitMs,
			SubmitMs = stats.SubmitMs,
			WorkerRecordMs = stats.WorkerRecordMs,
		} );
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
		frame.SetViewConstants( context );

		// Create shadow maps before writing their indices into sun constants.
		shadows.Setup( context, frame.ContactShadowMask );
		// Native uses no high-precision lighting offset for skyboxes.
		environment.Apply( context, world.Lighting, lightBinner, shadows.Directional, frame.Space, isSkybox ? default : view.Position, isSkybox ? null : fog, view );

		// Per-view debug mode (ToolsVis.hlsl, SetupShadingMode).
		context.Attributes.Set( ToolsVisModeName, view.ToolsVisMode );

		// Glass reads the native Skybox3DPipeline combo.
		if ( isSkybox ) context.Attributes.SetCombo( SkyboxCombo, 1 );

		for ( int i = 0; i < features.Count; i++ )
			features[i].BeforeUpload( context, transforms );

		var scope = context.BeginGpuScope( "Setup" );
		transforms.Upload( context );
		context.Attributes.Set( TransformBufferName, transforms.Current );
		context.InstanceIds = transforms.InstanceIds;

		for ( int i = 0; i < features.Count; i++ )
			features[i].Setup( context, world, mainPass );
		context.EndGpuScope( scope );
	}

	long lap;
	static readonly StringToken ToolsVisModeName = new( "ToolsVisMode" );

	/// <summary>
	/// Current or last rendered frame.
	/// </summary>
	internal RenderFrame Frame => frame;

	/// <summary>
	/// Setup context and parent attributes for recording segments.
	/// </summary>
	internal RenderContext FrameContext => context;

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

	Texture frameBufferCopy;

	/// <summary>
	/// Persistent mipmapped RGBA16F copy (<c>FindOrCreateFrameBufferScratchTexture</c>).
	/// Created on the main thread; the depth-aware mask reads the previous frame's second mip.
	/// </summary>
	internal Texture FrameBufferCopy( Rect viewport )
	{
		var width = (int)viewport.Width;
		var height = (int)viewport.Height;
		if ( frameBufferCopy is { } existing && existing.Width == width && existing.Height == height ) return existing;

		// The device retains resources used by in-flight frames.
		frameBufferCopy?.Dispose();
		var mips = (int)MathF.Log2( Math.Min( width, height ) ) + 1;
		var builder = Texture.CreateRenderTarget().WithFormat( ImageFormat.RGBA16161616F ).WithSize( width, height ).WithMips( mips ).WithUAVBinding();

		// Keep the general layout for compute mip writes and sampling.
		builder._config.m_nFlags |= NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_GEN_MIP_MAPS;
		frameBufferCopy = builder.Create( "SceneRenderer frame buffer copy" );
		return frameBufferCopy;
	}

	/// <summary>
	/// Draw a run range in feature order. May execute on a worker thread.
	/// </summary>
	internal void DrawRange( RenderContext rc, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats )
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
		transforms.Clear();
		Prepare( world, mainPass, transforms );

		shadows.Prepare( world, mainPass, lightBinner, transforms, features.Count, Shadows, isSkybox );

		// Skyboxes use their own system and view.
		PrepareSkybox( world, view );
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
	/// Release owned GPU resources. The device retains those used by in-flight frames.
	/// </summary>
	public void Dispose()
	{
		skybox?.Dispose();
		skybox = null;
		shadows.Dispose();
		fog.Dispose();
		swapChainTarget?.Dispose();
		swapChainTarget = null;
		textureTarget?.Dispose();
		textureScratch?.Dispose();
		textureTarget = null;
		frameBufferCopy?.Dispose();
		frameBufferCopy = null;
		transforms.Dispose();
		environment.Dispose();

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
