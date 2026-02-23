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
			entry.Render();
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

	public int CompareTo( PostProcessLayer other ) => Order.CompareTo( other.Order );

	/// <summary>
	/// Render this layer
	/// </summary>
	public void Render()
	{
		CommandList.ExecuteOnRenderThread();
	}
}
