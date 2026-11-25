namespace Borealis;

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
	/// Read FrameBufferCopyTexture in translucent layers and contribute to the refraction stencil.
	/// Mirrors <c>SceneObject.Flags.WantsFrameBufferCopy</c>.
	/// </summary>
	public bool WantsFrameBufferCopy { get; set; }

	/// <summary>
	/// Whether what it draws needs a stencil in the view's depth (<see cref="GpuMaterial.NeedsStencil"/>), as far as its
	/// owner knows before it records. A view drawing it gets a depth target with stencil.
	/// </summary>
	public bool NeedsStencil { get; set; }

	/// <summary>
	/// Draw an opaque one in the depth prepass too, as native's prepass does (<c>DepthNormalPrepassLayer</c>); off for
	/// <c>NoZPrepass</c>.
	/// </summary>
	public bool DepthPrepass { get; set; } = true;

	/// <summary>
	/// Cast shadows in the matching opaque/translucent layer (<c>CSceneSystem::AddShadowView</c>).
	/// </summary>
	public bool CastShadows
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ShadowChanged();
		}
	}

	/// <summary>
	/// Enable drawing.
	/// </summary>
	public bool Visible
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ShadowChanged();
		}
	} = true;

	internal int RenderOrder { get; set; }

	/// <summary>
	/// Skip size culling because custom bounds may be absent or huge.
	/// </summary>
	internal override bool SizeCulled => false;

	/// <summary>
	/// Draw with frame targets bound. Targets are restored afterward.
	/// </summary>
	internal abstract void Render( GpuContext context, ViewPass view, MeshLayer layer );

	/// <summary>
	/// Draw a run of custom objects of this one's type that follow one another in a layer, this one first, each with its
	/// transform slot (instance stream 1 from <see cref="CustomDraw.FirstInstance"/>). One at a time unless the type draws
	/// a run together.
	/// </summary>
	internal virtual void RenderRun( GpuContext context, ViewPass view, MeshLayer layer, ReadOnlySpan<CustomDraw> run )
	{
		for ( int i = 0; i < run.Length; i++ )
		{
			if ( i > 0 ) context.RestoreBound();
			context.BindInstances( 1, run[i].FirstInstance );
			run[i].Object.Render( context, view, layer );
		}
	}
}

/// <summary>A custom object in a run drawn together (<see cref="CustomObject.RenderRun"/>), with its first instance.</summary>
internal readonly record struct CustomDraw( CustomObject Object, int FirstInstance );
