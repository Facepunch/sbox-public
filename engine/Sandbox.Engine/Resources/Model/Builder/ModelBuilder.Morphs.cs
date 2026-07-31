using NativeEngine;
using System.Runtime.InteropServices;
using System.Text;

namespace Sandbox;

public sealed partial class ModelBuilder
{
	private void ApplyMorphTargets()
	{
		foreach ( var mesh in meshes )
		{
			if ( mesh?.MorphTargets is { Count: > 0 } )
				SetMorphTargets( mesh );
		}
	}

	private static unsafe void SetMorphTargets( Mesh mesh )
	{
		var targets = mesh.MorphTargets;
		var morphNameBuilder = new StringBuilder();
		var morphDescs = new Mesh.MorphNativeDesc[targets.Count];
		var allDeltas = new List<MorphDelta>();

		var byteOffset = 0;
		var index = 0;
		foreach ( var (name, deltas) in targets )
		{
			var byteLength = Encoding.UTF8.GetByteCount( name );

			morphDescs[index++] = new Mesh.MorphNativeDesc
			{
				NameOffset = byteOffset,
				NameLength = byteLength,
				StartDelta = allDeltas.Count,
				NumDeltas = deltas.Length
			};

			morphNameBuilder.Append( name );
			byteOffset += byteLength;
			allDeltas.AddRange( deltas );
		}

		var deltaSpan = CollectionsMarshal.AsSpan( allDeltas );
		fixed ( Mesh.MorphNativeDesc* morphDescPointer = morphDescs )
		fixed ( MorphDelta* deltaPointer = deltaSpan )
		{
			MeshGlue.SetMeshMorphData(
				mesh.native,
				(IntPtr)morphDescPointer, targets.Count,
				(IntPtr)deltaPointer, allDeltas.Count,
				morphNameBuilder.ToString() );
		}
	}
}
