using Sandbox.Rendering;

namespace Sandbox.UI;

/// <summary>
/// Collects all descriptor types for a panel's cached render data.
/// Rebuilt when dirty, drawn during the gather phase.
/// </summary>
internal class RenderLayer
{
	// Per-panel spatial/render state
	public Matrix TransformMat;
	public PanelRenderer.GPUScissor Scissor;

	// Draw descriptors
	public List<ShadowDrawDescriptor> OuterShadows = [];
	public List<BackdropDrawDescriptor> Backdrops = [];
	public List<BoxDrawDescriptor> Boxes = [];
	public List<ShadowDrawDescriptor> InsetShadows = [];
	public List<OutlineDrawDescriptor> Outlines = [];

	public int Total => OuterShadows.Count + Backdrops.Count + Boxes.Count + InsetShadows.Count + Outlines.Count;
	public bool IsEmpty => Total == 0;

	public void AddShadow( in ShadowDrawDescriptor desc )
	{
		if ( desc.Inset )
			InsetShadows.Add( desc );
		else
			OuterShadows.Add( desc );
	}

	/// <summary>
	/// Clear all descriptors from this layer.
	/// </summary>
	public void Clear()
	{
		OuterShadows.Clear();
		Backdrops.Clear();
		Boxes.Clear();
		InsetShadows.Clear();
		Outlines.Clear();
	}

	// Pool management
	static readonly List<RenderLayer> Pool = new();
	static int activeCount;

	internal static int ActiveCount => activeCount;
	internal static int PoolCount => Pool.Count;

	public static RenderLayer Rent()
	{
		activeCount++;

		if ( Pool.Count > 0 )
		{
			var layer = Pool[^1];
			Pool.RemoveAt( Pool.Count - 1 );
			layer.Clear();
			return layer;
		}

		return new RenderLayer();
	}

	public static void Return( RenderLayer layer )
	{
		activeCount--;
		layer.Clear();
		Pool.Add( layer );
	}
}
