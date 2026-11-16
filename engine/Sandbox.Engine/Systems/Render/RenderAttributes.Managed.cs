using System.Runtime.InteropServices;

namespace Sandbox;

public partial class RenderAttributes
{
	/// <summary>
	/// The values of an attribute set kept in managed code, for a backend that isn't Source 2's
	/// (<see cref="RenderBackend.BeginBlock"/>). Null for a native set, which is every set on Source 2.
	/// </summary>
	internal ManagedRenderAttributes Managed { get; private set; }

	/// <summary>
	/// Whether this set keeps its values in managed code rather than in a native <c>CRenderAttributes</c>.
	/// </summary>
	internal bool IsManaged => Managed is not null;

	/// <summary>
	/// A managed copy of a native set's values, kept when the process draws on a backend that lowers attributes itself
	/// (<see cref="KeepManagedCopy"/>): a scene object's under <c>-borealisgpu</c>, which a custom object merges into the
	/// attributes it draws with and a model's draws read as its object attributes. Null otherwise, and always for a set that is <see cref="Managed"/>.
	/// </summary>
	internal ManagedRenderAttributes Shadow { get; private set; }

	/// <summary>
	/// Keep a managed copy of what's set on this native set from now on (<see cref="Shadow"/>), so it merges into managed sets.
	/// </summary>
	internal void KeepManagedCopy()
	{
		if ( Managed is null ) Shadow ??= new ManagedRenderAttributes();
	}

	RenderAttributes( ManagedRenderAttributes managed )
	{
		Managed = managed;
	}

	/// <summary>
	/// An attribute set with no native side: what a render block on a backend other than Source 2's holds.
	/// </summary>
	internal static RenderAttributes CreateManaged() => new( new ManagedRenderAttributes() );

	/// <summary>
	/// An attribute set for the device being recorded for (<see cref="Graphics.SetRecordingBackend"/>, else the
	/// process's): native on Source 2's, as <c>new RenderAttributes()</c> makes, managed on another, so a recording for
	/// that device (a post process's command list under <c>-borealisgpu</c>) can draw with it.
	/// </summary>
	internal static RenderAttributes ForRecording() => Graphics.Backend is Source2RenderBackend ? new RenderAttributes() : CreateManaged();
}

/// <summary>
/// Render attribute values by token, as a backend that lowers them itself reads them: numbers and vectors as floats
/// (a matrix as 16, in memory order: M11, M12 ... M44), textures with a mip, buffers, samplers, constant buffer bytes,
/// and combos.
/// </summary>
internal sealed class ManagedRenderAttributes
{
	/// <summary>A texture and the mip bound (-1 for all).</summary>
	public readonly record struct TextureValue( Texture Texture, int Mip );

	readonly Dictionary<uint, object> values = new();
	readonly Dictionary<uint, int> combos = new();

	/// <summary>Every value by token: <c>float[]</c>, <see cref="TextureValue"/>, <see cref="GpuBuffer"/>, <c>SamplerState</c>, <c>byte[]</c> or a string.</summary>
	public IReadOnlyDictionary<uint, object> Values => values;

	/// <summary>Every combo by token.</summary>
	public IReadOnlyDictionary<uint, int> Combos => combos;

	/// <summary>Changes whenever a value or combo is set or cleared, for a reader that converts them only when they change.</summary>
	public int Version { get; private set; }

	public void Set( StringToken k, params float[] value )
	{
		values[k.Value] = value;
		Version++;
	}

	public void SetObject( StringToken k, object value )
	{
		if ( value is null ) values.Remove( k.Value );
		else values[k.Value] = value;
		Version++;
	}

	public void Set( StringToken k, in Matrix value ) => Set( k, MemoryMarshal.Cast<Matrix, float>( new ReadOnlySpan<Matrix>( in value ) ).ToArray() );

	public void SetCombo( StringToken k, int value )
	{
		combos[k.Value] = value;
		Version++;
	}

	public object Get( StringToken k ) => values.GetValueOrDefault( k.Value );
	public int? GetCombo( StringToken k ) => combos.TryGetValue( k.Value, out var value ) ? value : null;

	public float[] GetFloats( StringToken k ) => values.GetValueOrDefault( k.Value ) as float[];

	public void Clear()
	{
		values.Clear();
		combos.Clear();
		Version++;
	}

	/// <summary>Copy every value and combo into <paramref name="other"/>, replacing those of the same token.</summary>
	public void MergeTo( ManagedRenderAttributes other )
	{
		foreach ( var (k, v) in values ) other.values[k] = v;
		foreach ( var (k, v) in combos ) other.combos[k] = v;
		other.Version++;
	}
}
