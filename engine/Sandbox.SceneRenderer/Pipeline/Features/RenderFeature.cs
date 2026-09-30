namespace Sandbox.SceneRenderer;

/// <summary>
/// Prepares and draws culled objects, equivalent to native's <c>ISceneObjectDesc</c>.
/// Layers select draw rules; features record run ranges, potentially on worker threads.
/// </summary>
internal abstract class RenderFeature
{
	/// <summary>
	/// The object type this feature draws. Subclasses of it are drawn too.
	/// </summary>
	public abstract Type ObjectType { get; }

	/// <summary>
	/// Accepts <see cref="ObjectType"/> and subclasses by default.
	/// </summary>
	internal virtual bool Accepts( Type type ) => ObjectType.IsAssignableFrom( type );

	/// <summary>
	/// Position in the render system's feature list, which indexes each view's per-feature lists.
	/// </summary>
	internal int Index { get; set; } = -1;

	/// <summary>
	/// Whether to receive shadow views, filtered by <see cref="CastsShadow"/>.
	/// </summary>
	internal virtual bool DrawsShadows => false;

	/// <summary>
	/// Whether an object goes into shadow maps.
	/// </summary>
	internal virtual bool CastsShadow( RenderObject obj ) => false;

	/// <summary>
	/// Sort visible objects and write instance data once per view, before recording.
	/// </summary>
	internal virtual void Prepare( RenderWorld world, ViewPass view, TransformBuffer transforms ) { }

	/// <summary>
	/// Finalize transforms using native calls forbidden during Prepare, such as vertex-cache allocation.
	/// </summary>
	internal virtual void BeforeUpload( RenderContext context, TransformBuffer transforms ) { }

	/// <summary>
	/// Upload and dispatch before render passes, with main-view constants set.
	/// </summary>
	internal virtual void Setup( RenderContext context, RenderWorld world, ViewPass view ) { }

	/// <summary>
	/// Number of runs available for this view and layer.
	/// </summary>
	internal virtual int RunCount( ViewPass view, MeshLayer layer ) => 0;

	/// <summary>
	/// Estimated draws per run for load balancing. Defaults to one per run.
	/// </summary>
	internal virtual void RunWeights( ViewPass view, MeshLayer layer, Span<int> weights ) => weights.Fill( 1 );

	/// <summary>
	/// Record a prepared run range. May run concurrently when <see cref="CanRecordOffMainThread"/> permits.
	/// </summary>
	internal virtual void Draw( RenderContext context, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats ) { }

	/// <summary>
	/// Whether recording avoids engine code that requires the main thread.
	/// </summary>
	internal virtual bool CanRecordOffMainThread( ViewPass view, MeshLayer layer ) => true;

	/// <summary>
	/// Whether selected runs need a frame-buffer copy before drawing.
	/// </summary>
	internal virtual bool ReadsFrameBuffer( ViewPass view, MeshRuns runs ) => false;

	internal virtual void Dispose() { }
}
