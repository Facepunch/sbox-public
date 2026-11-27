using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Holds a list of post processing layers for a camera
/// </summary>
internal partial class PostProcessLayers
{
	public Dictionary<Stage, List<PostProcessLayer>> Layers = new();

	// Layers are rebuilt every frame, so reuse them (and their blit command lists) in creation order
	readonly List<PostProcessLayer> _pool = [];
	int _poolUsed;

	public bool HasLayers => _poolUsed > 0;

	/// <summary>
	/// An effect built this frame reads its motion (<c>Motion::Get</c>: <see cref="MotionBlur"/>), so the renderer resolves its
	/// objects' motion for the frame (<see cref="SceneCamera.ReadsMotionVectors"/>).
	/// </summary>
	public bool ReadsMotionVectors;

	/// <summary>Cleared to be built again since the camera last took these for drawing (<c>CameraComponent.TakePostProcess</c>).</summary>
	internal bool Rebuilt;

	public void Clear()
	{
		Rebuilt = true;
		ReadsMotionVectors = false;
		foreach ( var list in Layers.Values )
			list.Clear();

		_poolUsed = 0;
	}

	/// <summary>
	/// Add a new post process layer to a specific stage
	/// </summary>
	public PostProcessLayer CreateLayer( Stage stage )
	{
		if ( _poolUsed == _pool.Count )
			_pool.Add( new PostProcessLayer() );

		var layer = _pool[_poolUsed++];
		layer.Reset();
		layer.Index = _poolUsed;

		if ( !Layers.TryGetValue( stage, out var list ) )
		{
			list = [];
			Layers[stage] = list;
		}

		list.Add( layer );

		return layer;
	}

	/// <summary>
	/// Called for each stage during this camera's render. This is called on the render thread.
	/// </summary>
	public void OnRenderStage( Stage stage )
	{
		if ( !Layers.TryGetValue( stage, out var list ) )
			return;

		list.Sort();

		foreach ( var entry in list )
		{
			// Already run ahead of this stage on its selected queue
			if ( entry.RanEarly )
			{
				entry.RanEarly = false;
				continue;
			}

			entry.Render();
		}
	}

	/// <summary>
	/// Whether a layer can run ahead of <paramref name="stage"/> on <paramref name="queue"/>.
	/// </summary>
	public bool HasEarlyPostProcess( Stage stage, PostProcessQueue queue )
	{
		if ( !Layers.TryGetValue( stage, out var list ) ) return false;

		foreach ( var entry in list )
		{
			if ( entry.EarlyQueue == queue ) return true;
		}

		return false;
	}

	/// <summary>
	/// Run independent layers on their selected queue, in order, ahead of the stage, which then skips them.
	/// Called on the render thread, inside a <see cref="Graphics"/> block for that queue.
	/// </summary>
	public void RenderEarlyPostProcess( Stage stage, PostProcessQueue queue )
	{
		if ( !Layers.TryGetValue( stage, out var list ) ) return;

		list.Sort();

		foreach ( var entry in list )
		{
			if ( entry.EarlyQueue != queue || entry.RanEarly ) continue;

			entry.Render();
			entry.RanEarly = true;
		}
	}
}

internal record struct WeightedEffect
{
	public BasePostProcess Effect;
	public float Weight;
	/// <summary>
	/// Structural camera or volume owner for camera effects; null for scene defaults or ordinary effects.
	/// </summary>
	public Component Owner;
}

/// <summary>
/// A layer is placed on a specific Render Stage is ordered relative to other layers on that stage
/// </summary>
internal class PostProcessLayer : IComparable<PostProcessLayer>
{
	public CommandList CommandList;
	public int Order;
	public string Name;

	/// <summary>
	/// Where it may run ahead of its stage (<see cref="BasePostProcess.EarlyQueue"/>), and whether it has this frame.
	/// </summary>
	public PostProcessQueue? EarlyQueue;
	public bool RanEarly;

	/// <summary>Creation order this build: equal orders run in it.</summary>
	public int Index;

	/// <summary>
	/// A standard effect drawn by the uber post pass (<see cref="PostProcessLayers.FoldUberLayers"/>), with its parameters in
	/// <see cref="UberSource"/>; none for a layer with its own command list.
	/// </summary>
	public UberPostEffect Uber;
	public RenderAttributes UberSource;

	/// <summary>The effect's own commands, which the uber pass's draw follows in the same list (bloom's chain).</summary>
	public CommandList UberPrefix;

	/// <summary>Owned by this layer for the uber pass of the run it starts: the run's parameters together.</summary>
	public RenderAttributes UberAttributes;

	/// <summary>
	/// Owned by this layer for <see cref="BasePostProcess"/> blits, reused while the layer is.
	/// </summary>
	public CommandList BlitCommandList;

	public void Reset()
	{
		CommandList = null;
		Order = 0;
		Name = null;
		EarlyQueue = null;
		RanEarly = false;
		Index = 0;
		Uber = UberPostEffect.None;
		UberSource = null;
		UberPrefix = null;
	}

	public int CompareTo( PostProcessLayer other )
	{
		var order = Order.CompareTo( other.Order );
		return order != 0 ? order : Index.CompareTo( other.Index );
	}

	/// <summary>
	/// Render this layer. Nothing for one folded into the uber pass of the run before it.
	/// </summary>
	public void Render()
	{
		CommandList?.ExecuteOnRenderThread();
	}
}

/// <summary>Queue for an independent effect that may run before its normal camera stage.</summary>
internal enum PostProcessQueue
{
	Graphics,
	AsyncCompute
}
