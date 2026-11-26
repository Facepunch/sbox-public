using System.Diagnostics;

namespace Borealis;

/// <summary>
/// Reusable per-view frame state and pass helpers. Persistent resources belong to <see cref="System"/>.
/// The skybox has a separate frame recorded by the same <see cref="FrameRecorder"/>.
/// </summary>
internal sealed class RenderFrame
{
	/// <summary>
	/// Owner of persistent rendering resources.
	/// </summary>
	public RenderSystem System { get; }

	public RenderFrame( RenderSystem system )
	{
		System = system;
	}

	/// <summary>
	/// World and view set during collection.
	/// </summary>
	public RenderWorld World { get; internal set; }
	public RenderView View { get; internal set; }

	/// <summary>
	/// The main view's culled and prepared objects.
	/// </summary>
	public ViewPass MainPass => System.MainPass;

	/// <summary>
	/// HDR target and final output.
	/// </summary>
	public RenderOutput Output { get; internal set; }

	/// <summary>
	/// The camera's stages, run at native's hook points, or null.
	/// </summary>
	public IRenderStages Stages { get; internal set; }

	/// <summary>Whether this frame's current plan schedules any work on the compute queue.</summary>
	internal bool AsyncCompute;

	/// <summary>
	/// Linear clear colour converted from the view's gamma colour (<c>CRenderingPipelineStandard::AddLayersToView</c>).
	/// </summary>
	public Color Clear { get; internal set; }

	/// <summary>
	/// Optional skybox frame inserted at <see cref="Skybox3DLayer"/>.
	/// </summary>
	public RenderFrame Skybox { get; internal set; }

	/// <summary>
	/// Ambient and fog source, scaled into skybox space when needed.
	/// </summary>
	public Gpu.ViewEnvironment.ViewSpace Space { get; internal set; }

	/// <summary>
	/// Whether this frame draws a skybox into its parent's target.
	/// </summary>
	public bool IsSkybox => System.IsSkyboxSystem;

	/// <summary>
	/// The depth range its world draws into, within the viewport's.
	/// </summary>
	public float DepthMin => System.DepthMin;
	public float DepthMax => System.DepthMax;

	/// <summary>
	/// Optional sun contact-shadow mask.
	/// </summary>
	public GpuTexture ContactShadowMask { get; internal set; }

	/// <summary>
	/// The copy of the frame its translucent frame buffer readers sample, made on the main thread when it prepares; null when
	/// nothing in the main view reads it.
	/// </summary>
	public GpuTexture FrameBufferCopy { get; internal set; }

	/// <summary>Where on the main view's screen what reads <see cref="FrameBufferCopy"/> is.</summary>
	internal FrameBufferReadArea FrameBufferReadArea { get; set; }

	/// <summary>
	/// The opaque pass's normals and roughness while this frame traces screen-space reflections, or null
	/// (<see cref="ScreenSpaceReflectionsLayer.NormalsFor"/>).
	/// </summary>
	public GpuTexture Normals { get; internal set; }

	/// <summary>
	/// Accumulated segment draw counts.
	/// </summary>
	internal RenderStats Recorded;

	// Base constants for per-pass variants.
	ViewConstants constants;

	/// <summary>The view constants the frame drew with, for what draws over it after (post processing).</summary>
	internal ref readonly ViewConstants Constants => ref constants;
	ViewConstantsKey constantsKey;

	/// <summary>
	/// Native render time, latched during setup for all views, including shadows.
	/// </summary>
	public float Time { get; private set; }

	static readonly StringToken TimeAttribute = new( "Time" );
	static readonly StringToken DisableScreenSpaceEffects = new( "DisableScreenSpaceEffects" );

	/// <summary>
	/// Start a frame of <paramref name="world"/> through <paramref name="view"/>.
	/// </summary>
	internal void Begin( RenderWorld world, RenderView view, in RenderOutput output, IRenderStages stages )
	{
		World = world;
		View = view;
		Output = output;
		Stages = stages;
		// A view counting overdraw starts black
		Clear = ToolsViews.Counts( view ) ? Color.Black : view.ClearColor.ToLinear();
		Space = default;
		Skybox = null;
		ContactShadowMask = null;
		Normals = null;
		Recorded = default;
		ColorBeforeOpaque = !System.IsSkyboxSystem && stages is not null && stages.DrawsAt( RenderStage.AfterDepthPrepass );

		// The 3D skybox draws as it is
		DebugViews = System.IsSkyboxSystem ? DebugViews.None : RenderHost.Current.DebugViews;
	}

	/// <summary>
	/// Borealis's debug views this frame draws (<see cref="RenderHost.DebugViews"/>).
	/// </summary>
	internal DebugViews DebugViews;

	/// <summary>
	/// The camera's command lists draw at <see cref="RenderStage.AfterDepthPrepass"/>
	/// (<see cref="IRenderStages.DrawsAt"/>): that stage clears the frame's colour and draws into colour and depth, as
	/// native's hook does, and the opaque pass keeps what it drew instead of clearing.
	/// </summary>
	internal bool ColorBeforeOpaque;

	/// <summary>
	/// Release per-frame references after submission.
	/// </summary>
	internal void End()
	{
		Stages = null;
		Output = default;
		Skybox = null;
		ContactShadowMask = null;
		Normals = null;
	}

	/// <summary>
	/// Set base view constants and native render time (<c>CSceneSystem::InitializeRenderAttributes</c>).
	/// </summary>
	internal void SetViewConstants( GpuContext context )
	{
		Time = View.Time ?? GpuContext.RenderTime;
		context.Attributes.Set( TimeAttribute, Time );
		// The managed skybox is a child frame; AO and SSR belong to the main camera.
		context.Attributes.Set( DisableScreenSpaceEffects, IsSkybox );
		constants = new ViewConstants();
		View.FillConstants( ref constants, Output.Size, Time, Output.Samples, DepthMin, DepthMax );

		// Native draws new ones every time it fills view constants (FillOutViewConstants); SSR jitters its noise with them.
		// Drawn once a frame here, on the main thread, from a seeded generator so runs repeat
		randomFloats = new( random.NextSingle(), random.NextSingle(), random.NextSingle(), random.NextSingle() );
		constants.RandomFloats = randomFloats;

		constantsKey = new ViewConstantsKey( View.Viewport, Output.Size, Output.Samples, DepthMin, DepthMax );
		context.SetViewConstants( constants, constantsKey );
	}

	// This frame's g_vRandomFloats, for every pass's constants
	Vector4 randomFloats;
	readonly Random random = new( 0 );

	/// <summary>
	/// Base view-constant key inherited by segments.
	/// </summary>
	internal ViewConstantsKey ConstantsKey => constantsKey;

	/// <summary>
	/// Bind targets and matching constants (<c>CreatePerLayerViewConstants</c>).
	/// Uses depth's sample count, or one without depth. Nonnegative <paramref name="toneMapScalar"/> overrides exposure.
	/// </summary>
	internal void BeginPass( GpuContext rc, in StageTarget target, float toneMapScalar = -1 )
	{
		rc.Bind( target );
		UseViewConstants( rc, new ViewConstantsKey( target.Viewport, target.Size, target.Depth is null ? 1 : target.Samples, target.MinZ, target.MaxZ, toneMapScalar ) );
	}

	/// <summary>
	/// Bind constants for the key, reusing base constants and skipping unchanged bindings.
	/// </summary>
	internal void UseViewConstants( GpuContext rc, in ViewConstantsKey key )
	{
		if ( rc.ViewConstants == key ) return;

		if ( key == constantsKey )
		{
			rc.SetViewConstants( constants, key );
			return;
		}

		var perLayer = constants;
		View.FillPassConstants( ref perLayer, key.Viewport, key.TargetSize, key.Samples, key.MinZ, key.MaxZ );
		if ( key.ToneMapScalar >= 0 ) perLayer.ToneMapScalarLinear = key.ToneMapScalar;
		rc.SetViewConstants( perLayer, key );
	}

	/// <summary>
	/// The frame's constants with the view's near plane square to it (<see cref="RenderView.SquareViewToProjection"/>), for
	/// compute that fits the view's volume, such as light clusters. <see cref="UseFrameConstants"/> puts the frame's back.
	/// </summary>
	internal void UseSquareConstants( GpuContext rc )
	{
		var square = new ViewConstants();
		View.FillConstants( ref square, constantsKey.Viewport, constantsKey.TargetSize, Time, constantsKey.Samples, constantsKey.MinZ, constantsKey.MaxZ, square: true );
		square.RandomFloats = randomFloats;
		rc.SetViewConstants( square, null );
	}

	/// <summary>
	/// Restore base constants before main-thread layers and engine stages.
	/// </summary>
	internal void UseFrameConstants( GpuContext rc ) => UseViewConstants( rc, constantsKey );

	/// <summary>
	/// Bind HDR depth and optional colour with the frame's constants; the opaque pass also binds <see cref="Normals"/>.
	/// </summary>
	internal void BindFrame( GpuContext rc, bool color = true, bool normals = false ) => BeginPass( rc, Target( rc, color, final: false, normals ) );

	/// <summary>
	/// Describe the HDR or final target, with optional colour, and with <see cref="Normals"/> when asked.
	/// </summary>
	internal StageTarget Target( GpuContext rc, bool color, bool final, bool normals = false )
	{
		var output = Output;
		var target = output.Target;
		var srgbWrite = target.SrgbWrite;
		var stageColor = !color ? null : final ? rc.OutputColor( output, out srgbWrite ) : target.Color;

		// Retain HDR depth only when output sample counts match; resolved textures have one sample.
		var samples = final && ((output.Texture is not null && output.Scratch is null) || (output.Texture is null && target.Samples > 1)) ? 1 : target.Samples;

		return new StageTarget
		{
			Context = rc,
			Attributes = rc.Attributes,
			Color = stageColor,
			Depth = samples == target.Samples ? target.Depth : null,
			Normals = normals && !final ? Normals : null,
			HdrColor = final ? target.Resolved : target.Color,
			SrgbWrite = srgbWrite,
			IsOutput = final,
			Size = output.Size,
			Viewport = View.Viewport,
			MinZ = DepthMin,
			MaxZ = DepthMax,
			ColorFormat = final && output.Texture is { } texture ? texture.Format : target.Color.Format,
			Samples = samples,
		};
	}


	/// <summary>
	/// How long the camera's stages took this frame (<see cref="RenderStats.StagesMs"/>).
	/// </summary>
	internal double StagesMs;

	/// <summary>
	/// Run independent effects ahead of their normal stage, without binding frame targets.
	/// </summary>
	internal void RunEarlyPostProcess( GpuContext rc, RenderStage stage, StageQueue queue )
	{
		if ( Stages is null ) return;

		var start = Stopwatch.GetTimestamp();
		Stages.RenderEarlyPostProcess( stage, queue, Target( rc, color: false, final: false ) );
		rc.Invalidate();
		StagesMs += Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	/// <summary>
	/// Trace screen-space reflections without frame targets bound, or keep their history with colour bound.
	/// </summary>
	internal void RunScreenSpaceReflections( GpuContext rc, bool history )
	{
		if ( Stages is null ) return;

		var start = Stopwatch.GetTimestamp();
		Stages.RenderScreenSpaceReflections( history, Target( rc, color: history, final: false, normals: !history ) );
		rc.Invalidate();
		StagesMs += Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	internal void RunStage( GpuContext rc, RenderStage stage, bool color = true, bool final = false )
	{
		if ( Stages is not { } stages || !stages.RendersInsideFrame( stage ) ) return;

		var start = Stopwatch.GetTimestamp();
		var stageTarget = Target( rc, color, final );
		rc.Bind( stageTarget );
		stages.Render( stage, stageTarget );
		rc.Invalidate();
		StagesMs += Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	/// <summary>
	/// How many runs the main view's <paramref name="layer"/> draws.
	/// </summary>
	internal int RunCount( MeshLayer layer ) => System.RunCount( MainPass, layer );

	/// <summary>
	/// Whether any of the main view's <paramref name="runs"/> read the frame buffer copy.
	/// </summary>
	internal bool ReadsFrameBuffer( MeshRuns runs ) => System.ReadsFrameBuffer( runs );

	/// <summary>
	/// Draw the whole main-thread layer (<see cref="DrawRange"/>).
	/// </summary>
	internal void DrawLayer( GpuContext rc, MeshLayer layer, ref RenderStats stats ) => DrawRange( rc, MainPass, layer, 0, RunCount( layer ), ref stats );

	/// <summary>
	/// Draw <paramref name="count"/> of <paramref name="view"/>'s runs of <paramref name="layer"/> from <paramref name="first"/>.
	/// Translucent layers copy the frame on the first reader (<c>CRenderBatchList::DrawCurrentPrimitives</c>); in a layer split
	/// across contexts, the part with that reader copies it and the parts after it read that copy. May execute on a worker thread.
	/// </summary>
	internal void DrawRange( GpuContext rc, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats )
	{
		var copies = FrameBufferCopy is not null && view == MainPass && layer.LayerType == MeshLayerType.Translucent && !layer.Depth && ReadsFrameBuffer( layer.Runs );
		if ( copies )
		{
			// Parts are submitted in order: one after the first reader's finds the copy made
			var reader = first > 0 ? System.FirstFrameBufferReader( view, layer ) : -1;
			rc.BeginFrameBufferReads( FrameBufferCopy, View.Viewport, madeBefore: reader >= 0 && reader < first );
			rc.FrameBufferReadArea = FrameBufferReadArea;
		}

		System.DrawRange( rc, view, layer, first, count, ref stats );

		if ( copies ) rc.EndFrameBufferReads();
	}
}
