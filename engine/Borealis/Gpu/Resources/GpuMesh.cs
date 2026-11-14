namespace Borealis;

/// <summary>
/// A model's geometry on the GPU: its vertex and index buffers, addressed by mesh and draw call. A <see cref="RenderMesh"/>
/// describes its draws; the device binds and draws them.
/// </summary>
public abstract class GpuMesh
{
	/// <summary>
	/// Its name or asset path, for diagnostics.
	/// </summary>
	public abstract string Name { get; }

	/// <inheritdoc/>
	public override string ToString() => Name;
}
