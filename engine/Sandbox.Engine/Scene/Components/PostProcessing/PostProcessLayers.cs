using Sandbox.Rendering;
namespace Sandbox;

/// <summary>
/// Holds a list of post processing layers for a camera
/// </summary>
internal class PostProcessLayers
{
	public Dictionary<Stage, List<PostProcessLayer>> Layers = new();

	public void Clear()
	{
		Layers.Clear();
	}

	/// <summary>
	/// Add a new post process layer to a specific stage
	/// </summary>
	public PostProcessLayer CreateLayer( Stage stage )
	{
		PostProcessLayer layer = new();

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
			// Already run on the async compute queue this frame
			if ( entry.RanAsync )
			{
				entry.RanAsync = false;
				continue;
			}

			entry.Render();
		}
	}

	/// <summary>
	/// Whether a layer at <paramref name="stage"/> can run on the async compute queue (<see cref="BasePostProcess.AsyncCompute"/>).
	/// </summary>
	public bool HasAsyncCompute( Stage stage )
	{
		if ( !Layers.TryGetValue( stage, out var list ) ) return false;

		foreach ( var entry in list )
		{
			if ( entry.AsyncCompute ) return true;
		}

		return false;
	}

	/// <summary>
	/// Run the layers at <paramref name="stage"/> that can run on the async compute queue, in order, ahead of the stage, which
	/// then skips them. Called on the render thread, inside a <see cref="Graphics"/> block on the compute queue.
	/// </summary>
	public void RenderAsyncCompute( Stage stage )
	{
		if ( !Layers.TryGetValue( stage, out var list ) ) return;

		list.Sort();

		foreach ( var entry in list )
		{
			if ( !entry.AsyncCompute ) continue;

			entry.Render();
			entry.RanAsync = true;
		}
	}

	/// <summary>
	/// Whether a layer reads the depth-normals prepass's G-buffer (<see cref="BasePostProcess.NeedsDepthNormals"/>).
	/// </summary>
	public bool NeedsDepthNormals
	{
		get
		{
			foreach ( var list in Layers.Values )
			{
				foreach ( var entry in list )
				{
					if ( entry.NeedsDepthNormals ) return true;
				}
			}

			return false;
		}
	}
}

internal record struct WeightedEffect
{
	public BasePostProcess Effect;
	public float Weight;
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
	/// Whether it can run on the async compute queue (<see cref="BasePostProcess.AsyncCompute"/>), and whether it has this frame.
	/// </summary>
	public bool AsyncCompute, RanAsync;

	public int CompareTo( PostProcessLayer other ) => Order.CompareTo( other.Order );

	/// <summary>
	/// Render this layer
	/// </summary>
	public void Render()
	{
		CommandList.ExecuteOnRenderThread();
	}
}
