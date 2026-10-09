using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Run lists in draw order. Membership, sorting and draw rules are defined in <c>MeshRenderFeature.Lists.cs</c>.
/// </summary>
[Flags]
internal enum MeshRuns : ushort
{
	Opaque = 1,

	/// <summary>
	/// Opaque objects kept out of the prepass (<see cref="MeshObject.DepthPrepass"/> off).
	/// </summary>
	OpaqueNoPrepass = 2,

	/// <summary>
	/// Opaque objects whose tint alpha is under 1, drawn with <c>D_OPAQUE_FADE</c>.
	/// </summary>
	Faded = 4,

	Translucent = 8,

	/// <summary>
	/// A map's static overlays (<see cref="LayerMatch.StaticOverlay"/>), in render order.
	/// </summary>
	StaticOverlay = 16,

	/// <summary>
	/// Unfaded opaque overlays, redrawn in front of the world.
	/// </summary>
	OverlayOpaque = 32,

	/// <summary>
	/// Unfaded translucent overlays, redrawn back to front.
	/// </summary>
	OverlayTranslucent = 64,

	/// <summary>
	/// Unfaded bloom objects, redrawn into the bloom target.
	/// </summary>
	Bloom = 128,

	/// <summary>
	/// Objects drawn only after post processing, depth tested (<see cref="LayerMatch.OverlayWithDepth"/>), in native's full sort.
	/// </summary>
	OverlayWithDepth = 256,

	/// <summary>
	/// Objects drawn only after post processing, over everything (<see cref="LayerMatch.OverlayWithoutDepth"/>).
	/// </summary>
	OverlayWithoutDepth = 512,

	/// <summary>
	/// After-UI objects, including faded ones, in native's full sort order.
	/// </summary>
	AfterUI = 1024,

	/// <summary>
	/// Unfaded translucent frame-buffer readers for the refraction stencil.
	/// </summary>
	Refraction = 2048,

	/// <summary>
	/// Unfaded opaque overlays in the prepass. Drawn before the world; excluded from <see cref="Opaque"/>.
	/// </summary>
	OverlayPrepass = 4096,

	/// <summary>
	/// The main view's decal geometry (<see cref="MeshObject.Decal"/>), faded too, in native's full sort.
	/// </summary>
	Decal = 8192,
}

/// <summary>
/// Draws a view's sorted runs. Shared layers split runs into segments, each bound by <see cref="Begin"/>.
/// Worker recording requires <see cref="RenderFeature.CanRecordOffMainThread"/>.
/// </summary>
internal abstract class MeshLayer : RenderLayer
{
	protected MeshLayer( string name ) : base( name ) { }

	/// <summary>
	/// Required shader mode. Materials without it are skipped.
	/// </summary>
	public StringToken ShaderMode { get; protected init; }

	/// <summary>
	/// Depth-only rendering: use the fast-path material or strip opaque pixel shaders; disable lightmaps.
	/// </summary>
	public bool Depth { get; protected init; }

	/// <summary>
	/// A depth layer that writes normals and roughness when the view draws depth normals
	/// (<see cref="RenderView.DepthNormals"/>): materials' own depth pixel shaders, no fast-path material, nothing stripped.
	/// </summary>
	public bool WritesNormals { get; protected init; }

	/// <summary>
	/// Force the depth-only material, including overrides (<c>CSceneSystem::ShouldOverrideDepthMaterial</c>).
	/// </summary>
	public bool ForceDepthFastPath { get; protected init; }

	/// <summary>
	/// The runs it draws.
	/// </summary>
	public MeshRuns Runs { get; protected init; }

	/// <summary>
	/// Whether to draw custom objects using <see cref="LayerType"/>.
	/// </summary>
	public bool DrawsCustomObjects { get; protected init; } = true;

	/// <summary>
	/// Whether it draws custom objects in <paramref name="view"/>. The prepass only draws them for a view with depth normals:
	/// that's for AO, which runs before the opaque pass would draw them, and a layer with custom objects records on the main
	/// thread - terrain culls its meshlets on the CPU each pass - so views that don't need it keep the prepass on workers.
	/// </summary>
	internal bool DrawsCustomObjectsIn( ViewPass view ) => DrawsCustomObjects && (!WritesNormals || (view.View.DepthNormals && !view.IsShadow));

	/// <summary>
	/// A layer whose draws copy the frame buffer reads glass's refraction stencil and the depth chain, which the copy's
	/// depth-aware mask uses: a translucent forward layer with frame buffer readers in its lists, as
	/// <see cref="RenderFrame.DrawLayer"/> copies (<see cref="MeshRenderFeature.FrameBufferCopyingLists"/>).
	/// </summary>
	public override FrameResources Reads( RenderFrame frame ) => LayerType == SceneLayerType.Translucent && !Depth && frame.ReadsFrameBuffer( Runs & MeshRenderFeature.FrameBufferCopyingLists )
		? FrameResources.DepthChain | FrameResources.RefractionStencil
		: FrameResources.None;

	/// <summary>
	/// Native layer type used by custom objects.
	/// </summary>
	public SceneLayerType LayerType { get; protected init; } = SceneLayerType.Opaque;

	public override bool IsShared => true;

	/// <summary>
	/// Add the main view's work when this layer is shared.
	/// </summary>
	public override void AddWork( FrameRecorder recorder, RenderFrame frame )
	{
		if ( IsShared ) recorder.AddWork( frame, frame.MainPass, this );
	}

	/// <summary>
	/// Bind the view's target. Clear or initialize it when <paramref name="start"/> is true.
	/// </summary>
	public abstract void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start );

	/// <summary>
	/// Record the main view's unshared layer.
	/// </summary>
	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		Begin( frame, rc, frame.MainPass, start: true );
		frame.DrawLayer( rc, this, ref stats );
	}
}
