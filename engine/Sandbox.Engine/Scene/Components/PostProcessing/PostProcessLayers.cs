using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Holds a list of post processing layers for a camera
/// </summary>
internal class PostProcessLayers
{
	public Dictionary<Stage, List<PostProcessLayer>> Layers = new();
	readonly HashSet<Stage> _earlyScheduled = [];

	// Layers and stage lists made before, used again after each clear: a camera's layers are made again every frame
	readonly List<PostProcessLayer> _made = new();
	readonly Stack<List<PostProcessLayer>> _spareLists = new();
	int _used;

	public void Clear()
	{
		foreach ( var list in Layers.Values )
		{
			list.Clear();
			_spareLists.Push( list );
		}

		Layers.Clear();
		_earlyScheduled.Clear();
		_used = 0;
	}

	/// <summary>
	/// Add a new post process layer to a specific stage
	/// </summary>
	public PostProcessLayer CreateLayer( Stage stage )
	{
		PostProcessLayer layer;
		if ( _used < _made.Count )
		{
			layer = _made[_used];
			layer.Reset();
		}
		else
		{
			layer = new();
			_made.Add( layer );
		}

		_used++;

		if ( !Layers.TryGetValue( stage, out var list ) )
		{
			list = _spareLists.TryPop( out var spare ) ? spare : [];
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

		var scheduled = _earlyScheduled.Contains( stage );
		if ( !scheduled )
			list.Sort();

		foreach ( var entry in list )
		{
			// Native records procedural layers concurrently. Assign effects before jobs start so this
			// hook can be recorded before the early effects without executing them twice.
			if ( scheduled && entry.EarlyQueue.HasValue ) continue;

			// Already run ahead of this stage on its selected queue
			if ( entry.RanEarly )
			{
				entry.RanEarly = false;
				continue;
			}

			entry.Render();
		}
	}

	/// <summary>Assign early effects before native starts recording procedural layers on worker threads.</summary>
	internal void ScheduleEarlyPostProcess( Stage stage, bool enabled )
	{
		if ( !enabled )
		{
			_earlyScheduled.Remove( stage );
			return;
		}

		if ( !Layers.TryGetValue( stage, out var list ) )
			return;

		list.Sort();
		_earlyScheduled.Add( stage );
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

		var scheduled = _earlyScheduled.Contains( stage );
		if ( !scheduled )
			list.Sort();

		foreach ( var entry in list )
		{
			if ( entry.EarlyQueue != queue || (!scheduled && entry.RanEarly) ) continue;

			entry.Render();
			if ( !scheduled )
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
	/// Whether it reads the depth-normals prepass's G-buffer (<see cref="BasePostProcess.NeedsDepthNormals"/>).
	/// </summary>
	public bool NeedsDepthNormals;

	/// <summary>
	/// Where it may run ahead of its stage (<see cref="BasePostProcess.EarlyQueue"/>), and whether it has this frame.
	/// </summary>
	public PostProcessQueue? EarlyQueue;
	public bool RanEarly;

	public int CompareTo( PostProcessLayer other ) => Order.CompareTo( other.Order );

	/// <summary>As new, to be used again (<see cref="PostProcessLayers.CreateLayer"/>).</summary>
	internal void Reset()
	{
		CommandList = null;
		Order = 0;
		Name = null;
		NeedsDepthNormals = false;
		EarlyQueue = null;
		RanEarly = false;
	}

	/// <summary>
	/// Render this layer
	/// </summary>
	public void Render()
	{
		CommandList.ExecuteOnRenderThread();
	}
}

/// <summary>Queue for an independent effect that may run before its normal camera stage.</summary>
internal enum PostProcessQueue
{
	Graphics,
	AsyncCompute
}
