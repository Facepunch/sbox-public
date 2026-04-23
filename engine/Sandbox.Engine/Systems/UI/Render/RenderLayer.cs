using Sandbox.Rendering;

namespace Sandbox.UI;

/// <summary>
/// All descriptor types build into this
/// This is higher than the GPUBoxInstance because of the texture refs
/// but I want the texture refs so we can refresh bindless indices at render time
/// But this should be refactored down into the descriptor itself somehow, since we want to support custom ones..
/// </summary>
internal struct RenderInstance
{
	public GPUBoxInstance GPU;
	public BlendMode BlendMode;
	public int Pass;
	public Texture BackgroundImage;
	public Texture BorderImage;
}

/// <summary>
/// Collects all draw instances for a panel's cached render data.
/// Rebuilt when dirty, drawn during the gather phase.
/// </summary>
internal class RenderLayer
{
	// Per-panel spatial/render state
	public Matrix TransformMat;
	public PanelRenderer.GPUScissor Scissor;

	// Draw descriptors
	public List<RenderInstance> Instances = [];

	// These should be in the above list, but I'm bored of coding this
	public List<BackdropDrawDescriptor> Backdrops = [];

	// Trackers whilst building
	int _buildPass;
	BlendMode _buildBlendMode = BlendMode.Normal;
	bool _buildAnyBox;

	public int Total => Instances.Count + Backdrops.Count;
	public bool IsEmpty => Total == 0;

	public void AddShadow( in ShadowDrawDescriptor desc )
	{
		Instances.Add( new RenderInstance
		{
			GPU = GPUBoxInstance.FromShadow( desc ),
			BlendMode = desc.Inset ? _buildBlendMode : BlendMode.Normal,
			Pass = desc.Inset ? _buildPass : 0,
		} );
	}

	public void AddBox( in BoxDrawDescriptor desc )
	{
		if ( desc.IsTwoPass ) return;

		if ( _buildAnyBox && desc.OverrideBlendMode != _buildBlendMode )
			_buildPass++;

		_buildBlendMode = desc.OverrideBlendMode;
		_buildAnyBox = true;

		Instances.Add( new RenderInstance
		{
			GPU = GPUBoxInstance.From( desc ),
			BlendMode = desc.OverrideBlendMode,
			Pass = _buildPass,
			BackgroundImage = desc.HasImage ? desc.BackgroundImage : null,
			BorderImage = desc.HasBorderImage ? desc.BorderImageTexture : null,
		} );
	}

	public void AddOutline( in OutlineDrawDescriptor desc )
	{
		Instances.Add( new RenderInstance
		{
			GPU = GPUBoxInstance.FromOutline( desc ),
			BlendMode = _buildBlendMode,
			Pass = _buildPass,
		} );
	}

	/// <summary>
	/// Clear all instances from this layer.
	/// </summary>
	public void Clear()
	{
		Instances.Clear();
		Backdrops.Clear();
		_buildPass = 0;
		_buildBlendMode = BlendMode.Normal;
		_buildAnyBox = false;
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
