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

	/// <summary>
	/// The generated levels of a draw of a model without LODs of its own (docs/reference/borealis-gpu-driven.md, "Generated
	/// LODs"), asked for the first time a view wants them: <see cref="GeneratedLevels.Pending"/> until they're built and in
	/// the draw's index page, <see cref="GeneratedLevels.None"/> for a draw that has none or a device that makes none.
	/// </summary>
	internal virtual GeneratedLevels LevelsFor( in RenderMesh.Draw draw ) => GeneratedLevels.None;
}

/// <summary>
/// A draw's generated levels: each one's indices (its first index and count, relative to the draw's index buffer as
/// <see cref="RenderMesh.Draw.StartIndex"/> is) and its error, a fraction of the draw's bounds' half diagonal, growing level
/// by level.
/// </summary>
internal sealed class GeneratedLevels( (int StartIndex, int IndexCount)[] ranges, float[] errors )
{
	/// <summary>The draw has no levels: it draws whole.</summary>
	public static readonly GeneratedLevels None = new( [], [] );

	/// <summary>The draw's levels are being built, or wait to be published.</summary>
	public static readonly GeneratedLevels Pending = new( [], [] );

	public (int StartIndex, int IndexCount)[] Ranges { get; } = ranges;

	public float[] Errors { get; } = errors;
}
