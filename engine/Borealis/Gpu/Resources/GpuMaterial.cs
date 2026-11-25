namespace Borealis;

/// <summary>
/// A material: a shader and its values, which the device turns into pipeline state for a shader mode. Equal materials are
/// the same object, so identity works as a sort and batching key.
/// </summary>
public abstract class GpuMaterial : IDisposable
{
	/// <summary>
	/// Its name or asset path, for diagnostics.
	/// </summary>
	public abstract string Name { get; }

	/// <summary>
	/// Why it doesn't draw as its file says (not built, a shader that won't load, a file that can't be read), or null when it
	/// does. A fallback drawn in place of a shared material (the depth-only material) would draw the wrong thing everywhere.
	/// </summary>
	internal string FellBack { get; set; }

	/// <summary>
	/// Whether it blends (its <c>translucent</c> attribute).
	/// </summary>
	public abstract bool IsTranslucent { get; }

	/// <summary>
	/// Whether it alpha tests (its <c>alphatest</c> attribute).
	/// </summary>
	public abstract bool IsAlphaTest { get; }

	/// <summary>
	/// Whether it samples a copy of the frame (its <c>bWantsFBCopyTexture</c> attribute).
	/// </summary>
	public abstract bool ReadsFrameBuffer { get; }

	/// <summary>
	/// Whether its depth can draw with the shared depth-only material (its <c>ShadowFastPath</c> attribute).
	/// </summary>
	public abstract bool ShadowFastPath { get; }

	/// <summary>
	/// Whether its depth draws take the shared depth-only material now, whatever their draws' own fast-path flags: a material
	/// whose shader, once compiled, is known to place vertices from their positions alone, and that draws opaque, one-sided,
	/// without alpha test or depth bias of its own. Read as draw lists are built.
	/// </summary>
	public virtual bool DepthOnlyNow => false;

	/// <summary>
	/// Whether <see cref="DepthOnlyNow"/> may still change by itself (a surface whose program is still compiling): what is
	/// made once from it is made again until it settles.
	/// </summary>
	public virtual bool DepthOnlyPending => false;

	/// <summary>
	/// Whether its vertex stage places vertices itself from what they hold rather than from their positions (a water
	/// surface's clipmap cells): levels simplified from its meshes' positions (<see cref="GpuMesh"/>'s generated levels)
	/// don't keep its shape. Null while that isn't known yet (a surface still compiling).
	/// </summary>
	public virtual bool? PlacesVertices => false;

	/// <summary>
	/// Whether it's known to draw nothing in depth-only passes (shadow maps, the depth prepass), so their draws of it are left
	/// out rather than tried: a translucent surface that casts no shadow of its own (no translucent depth pass, once its
	/// program has compiled) and has no <c>.shader</c> to draw it instead. Read as draws are recorded.
	/// </summary>
	public virtual bool DrawsNoDepth => false;

	/// <summary>
	/// Whether its vertex stage moves vertices over time (wind in foliage), as the shader declares with
	/// <c>BoolAttribute( AnimatesVertices, true )</c>: a static object drawn with it isn't a static caster, as a light's
	/// static shadow cache can't hold its motion, so lights draw it live. Null while that isn't known yet (a surface still compiling).
	/// </summary>
	public virtual bool? AnimatesVertices => false;

	/// <summary>
	/// Whether it's lit from a light probe when its object isn't lightmapped.
	/// </summary>
	public abstract bool NeedsLightProbe { get; }

	/// <summary>
	/// Whether its draws need a stencil to be right (they write one, or test it against something other than zero), so a
	/// view drawing it gets a depth target with stencil (<see cref="RenderSystem.DepthFormat"/>). False where the device
	/// can't tell; such a device keeps stencil on every view (<see cref="GpuDevice.KnowsStencilUse"/>).
	/// </summary>
	public virtual bool NeedsStencil => false;

	/// <summary>
	/// Materials with equal keys share a shader and most of their state, so switching between them is cheap.
	/// </summary>
	public abstract ulong SimilarityKey { get; }

	/// <summary>
	/// Release it. Only for materials this renderer created.
	/// </summary>
	public abstract void Dispose();

	/// <inheritdoc/>
	public override string ToString() => Name;
}
