using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Describes a per-vertex delta for a morph target (blend shape).
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public readonly record struct MorphDelta( int VertexIndex, Vector3 PositionDelta, Vector3 NormalDelta );

public partial class Mesh
{
	private Dictionary<string, MorphDelta[]> _morphTargets;

	internal IReadOnlyDictionary<string, MorphDelta[]> MorphTargets => _morphTargets;

	/// <summary>
	/// Add a morph target (blend shape) to this mesh. Each delta describes how a vertex
	/// should be displaced when the morph is fully active.
	/// Morph targets are applied when the mesh is created through <see cref="ModelBuilder"/>.
	/// </summary>
	/// <param name="name">Name of the morph target, used as the flex controller name</param>
	/// <param name="deltas">Per-vertex position and normal deltas</param>
	public Mesh AddMorph( string name, ReadOnlySpan<MorphDelta> deltas )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		if ( deltas.Length == 0 )
			throw new ArgumentException( "Morph deltas cannot be empty", nameof( deltas ) );

		_morphTargets ??= new( StringComparer.OrdinalIgnoreCase );
		_morphTargets[name] = deltas.ToArray();

		return this;
	}

	[StructLayout( LayoutKind.Sequential )]
	internal struct MorphNativeDesc
	{
		public int NameOffset;
		public int NameLength;
		public int StartDelta;
		public int NumDeltas;
	}
}
