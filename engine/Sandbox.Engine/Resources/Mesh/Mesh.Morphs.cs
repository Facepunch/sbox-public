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

		if ( deltas.Length > MaxMorphDeltas )
			throw new ArgumentException( $"A morph can have at most {MaxMorphDeltas} deltas", nameof( deltas ) );

		_morphTargets ??= new( StringComparer.OrdinalIgnoreCase );

		if ( _morphTargets.Count >= MaxMorphTargets && !_morphTargets.ContainsKey( name ) )
			throw new InvalidOperationException( $"A mesh can have at most {MaxMorphTargets} morph targets" );

		_morphTargets[name] = deltas.ToArray();

		return this;
	}

	internal const int MaxMorphTargets = 4096;
	internal const int MaxMorphDeltas = 1 << 24;

	internal static bool TryGetMorphAtlasSize( int vertexCount, int morphCount, out int width, out int height )
	{
		width = 0;
		height = 0;

		if ( vertexCount <= 0 || morphCount <= 0 )
			return false;

		// Mirrors SetMeshMorphData: two regions per morph, 512 vertices per row, int16 texture dimensions
		var targetWidth = Math.Min( 512, vertexCount );
		var targetHeight = (vertexCount - 1) / targetWidth + 1;
		var atlasHeight = (long)targetHeight * morphCount * 2;

		if ( atlasHeight > short.MaxValue )
			return false;

		width = targetWidth;
		height = (int)atlasHeight;
		return true;
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
