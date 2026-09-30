namespace Sandbox.SceneRenderer;

/// <summary>
/// Self-drawing object sorted with meshes (<c>CManagedSceneObject</c>, <c>CManagedRendererDesc</c>).
/// Defaults to translucent; transform and tint are bound through instance stream 1.
/// </summary>
public abstract class CustomObject : RenderObject
{
	/// <summary>
	/// The tint written into its transform slot.
	/// </summary>
	public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// Draw as opaque (<c>IS_OPAQUE</c>); defaults to translucent.
	/// </summary>
	public bool IsOpaque { get; set; }

	/// <summary>
	/// Draw an opaque one in the depth prepass too, as native's prepass does (<c>DepthNormalPrepassLayer</c>); off for
	/// <c>NoZPrepass</c>.
	/// </summary>
	public bool DepthPrepass { get; set; } = true;

	/// <summary>
	/// Cast shadows in the matching opaque/translucent layer (<c>CSceneSystem::AddShadowView</c>).
	/// </summary>
	public bool CastShadows { get; set; }

	/// <summary>
	/// Enable drawing.
	/// </summary>
	public bool Visible { get; set; } = true;

	/// <summary>
	/// Skip size culling because custom bounds may be absent or huge.
	/// </summary>
	internal override bool SizeCulled => false;

	/// <summary>
	/// Draw with frame targets bound. Targets are restored afterward.
	/// </summary>
	internal abstract void Render( RenderContext context, ViewPass view, MeshLayer layer );
}
