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
	/// Whether it's lit from a light probe when its object isn't lightmapped.
	/// </summary>
	public abstract bool NeedsLightProbe { get; }

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
