using NativeEngine;
using Sandbox.Rendering;
using System.Diagnostics;

namespace Sandbox.SceneRenderer;

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
	public Texture ContactShadowMask { get; internal set; }

	/// <summary>
	/// Accumulated segment draw counts.
	/// </summary>
	internal RenderStats Recorded;

	// Base constants for per-pass variants.
	ViewConstants constants;
	ViewConstantsKey constantsKey;

	/// <summary>
	/// Native render time, latched during setup for all views, including shadows.
	/// </summary>
	public float Time { get; private set; }

	static readonly StringToken TimeAttribute = new( "Time" );

	/// <summary>
	/// Start a frame of <paramref name="world"/> through <paramref name="view"/>.
	/// </summary>
	internal void Begin( RenderWorld world, RenderView view, in RenderOutput output, IRenderStages stages )
	{
		World = world;
		View = view;
		Output = output;
		Stages = stages;
		Clear = view.ClearColor.ToLinear();
		Space = default;
		Skybox = null;
		ContactShadowMask = null;
		NormalsGBuffer = null;
		Recorded = default;
	}

	/// <summary>
	/// Release per-frame references after submission.
	/// </summary>
	internal void End()
	{
		Stages = null;
		Output = default;
		Skybox = null;
		ContactShadowMask = null;
		NormalsGBuffer = null;
	}

	/// <summary>
	/// Set base view constants and native render time (<c>CSceneSystem::InitializeRenderAttributes</c>).
	/// </summary>
	internal void SetViewConstants( RenderContext context )
	{
		Time = RenderContext.RenderTime;
		context.Attributes.Set( TimeAttribute, Time );
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
	internal void BeginPass( RenderContext rc, in StageTarget target, float toneMapScalar = -1 )
	{
		rc.Bind( target );
		UseViewConstants( rc, new ViewConstantsKey( target.Viewport, target.Size, target.Depth.IsNull ? 1 : target.Samples, target.MinZ, target.MaxZ, toneMapScalar ) );
	}

	/// <summary>
	/// Bind constants for the key, reusing base constants and skipping unchanged bindings.
	/// </summary>
	internal void UseViewConstants( RenderContext rc, in ViewConstantsKey key )
	{
		if ( rc.ViewConstants == key ) return;

		if ( key == constantsKey )
		{
			rc.SetViewConstants( constants, key );
			return;
		}

		var perLayer = new ViewConstants();
		View.FillConstants( ref perLayer, key.Viewport, key.TargetSize, Time, key.Samples, key.MinZ, key.MaxZ );
		perLayer.RandomFloats = randomFloats;
		if ( key.ToneMapScalar >= 0 ) perLayer.ToneMapScalarLinear = key.ToneMapScalar;
		rc.SetViewConstants( perLayer, key );
	}

	/// <summary>
	/// Restore base constants before main-thread layers and engine stages.
	/// </summary>
	internal void UseFrameConstants( RenderContext rc ) => UseViewConstants( rc, constantsKey );

	/// <summary>
	/// Bind HDR depth and optional colour with the frame's constants.
	/// </summary>
	internal void BindFrame( RenderContext rc, bool color = true ) => BeginPass( rc, Target( rc, color, final: false ) );

	/// <summary>
	/// Describe the HDR or final target, with optional colour.
	/// </summary>
	internal StageTarget Target( RenderContext rc, bool color, bool final )
	{
		var output = Output;
		var target = output.Target;
		var srgbWrite = target.SrgbWrite;
		var stageColor = !color ? default : final ? rc.OutputColor( output, out srgbWrite ) : target.Color.native;

		// Retain HDR depth only when output sample counts match; resolved textures have one sample.
		var samples = final && ((output.Texture is not null && output.Scratch is null) || (!output.HasCopy && target.Samples > 1)) ? 1 : target.Samples;

		return new StageTarget
		{
			Context = rc.Native,
			Attributes = rc.Attributes,
			Color = stageColor,
			Depth = samples == target.Samples ? target.Depth.native : default,
			HdrColor = final ? target.Resolved.native : target.Color.native,
			SrgbWrite = srgbWrite,
			IsOutput = final,
			Size = output.Size,
			Viewport = View.Viewport,
			MinZ = DepthMin,
			MaxZ = DepthMax,
			ColorFormat = final && output.Texture is { } texture ? texture.ImageFormat : final && output.HasCopy ? ImageFormat.RGBA8888 : target.Color.ImageFormat,
			Samples = samples,
		};
	}

	/// <summary>
	/// The depth-normals prepass's G-buffer this frame (<see cref="RenderView.DepthNormals"/>), or null when the prepass is
	/// depth only.
	/// </summary>
	internal Texture NormalsGBuffer;

	/// <summary>
	/// How long the camera's stages took this frame (<see cref="RenderStats.StagesMs"/>).
	/// </summary>
	internal double StagesMs;

	/// <summary>
	/// Bind targets and run a camera stage. Invalidate cached bindings afterward because stages may change them.
	/// </summary>
	/// <summary>
	/// Run a stage's async compute effects (<see cref="IRenderStages.RenderAsyncCompute"/>) into <paramref name="rc"/>, on the
	/// compute queue: nothing bound.
	/// </summary>
	internal void RunAsyncCompute( RenderContext rc, Stage stage )
	{
		if ( Stages is null ) return;

		var start = Stopwatch.GetTimestamp();
		Stages.RenderAsyncCompute( stage, Target( rc, color: false, final: false ) );
		rc.Invalidate();
		StagesMs += Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	internal void RunStage( RenderContext rc, Stage stage, bool color = true, bool final = false )
	{
		if ( Stages is null ) return;

		var start = Stopwatch.GetTimestamp();
		var stageTarget = Target( rc, color, final );
		rc.Bind( stageTarget );
		Stages.Render( stage, stageTarget );
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
	/// Draw the whole main-thread layer. Translucent layers copy the frame on the first reader
	/// (<c>CRenderBatchList::DrawCurrentPrimitives</c>).
	/// </summary>
	internal void DrawLayer( RenderContext rc, MeshLayer layer, ref RenderStats stats )
	{
		var copies = layer.LayerType == SceneLayerType.Translucent && !layer.Depth && ReadsFrameBuffer( layer.Runs );
		if ( copies ) rc.BeginFrameBufferReads( System.FrameBufferCopy( View.Viewport ), View.Viewport );

		System.DrawRange( rc, MainPass, layer, 0, RunCount( layer ), ref stats );

		if ( copies ) rc.EndFrameBufferReads();
	}
}
