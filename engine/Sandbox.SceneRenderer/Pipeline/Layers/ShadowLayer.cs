using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Draws opaque, faded, then translucent casters with per-view depth bias (<c>CSceneSystem::AddShadowView</c>).
/// Transitions maps to writable before recording; <see cref="ShadowsReadyLayer"/> makes them readable afterward.
/// </summary>
internal sealed class ShadowLayer : MeshLayer
{
	static readonly StringToken DepthMode = new( "Depth" );

	public ShadowLayer() : base( "Shadow" )
	{
		ShaderMode = DepthMode;
		Depth = true;
		Runs = MeshRuns.Opaque | MeshRuns.OpaqueNoPrepass | MeshRuns.Faded | MeshRuns.Translucent;
		LayerType = SceneLayerType.Shadow;
	}

	public override bool IsNeeded( RenderFrame frame ) => frame.System.ShadowMaps.Passes.Length > 0;

	// Shadow maps only: nothing the depth chain or contact shadows touch
	public override bool BesideAsyncCompute => true;

	public override void AddWork( FrameRecorder recorder, RenderFrame frame )
	{
		foreach ( var pass in frame.System.ShadowMaps.Passes )
			recorder.AddWork( frame, pass, this );
	}

	public override void BeforeSegments( RenderFrame frame, RenderContext frameContext ) => frame.System.ShadowMaps.BarrierToWrite( frameContext );

	/// <summary>
	/// Clear the shadow map or initialize it from its static cache.
	/// </summary>
	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start ) => frame.System.ShadowMaps.BeginPass( rc, view, start );
}

/// <summary>
/// Makes shadow maps readable after all shadow draws.
/// </summary>
internal sealed class ShadowsReadyLayer : RenderLayer
{
	public ShadowsReadyLayer() : base( "Shadows Ready" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.System.ShadowMaps.Passes.Length > 0;

	public override bool BesideAsyncCompute => true;

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats ) => frame.System.ShadowMaps.BarrierToRead( rc );
}
