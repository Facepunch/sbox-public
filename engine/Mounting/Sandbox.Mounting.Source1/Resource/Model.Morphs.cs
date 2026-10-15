using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private const int STUDIOHDR_FLAGS_VERT_ANIM_FIXED_POINT_SCALE = 0x00200000;

	private static BBox[] AddMeshMorphs( MdlFile mdl, MdlFile.StudioMesh mesh, ReadOnlySpan<int> originalToOutput,
		string[] flexNames, Mesh renderMesh, int vertexCount, VertexTransform[] vertexTransforms )
	{
		bool useFixedPoint = (mdl.Flags & STUDIOHDR_FLAGS_VERT_ANIM_FIXED_POINT_SCALE) != 0;
		var fixedPointScale = mdl.VertAnimFixedPointScale;
		var morphs = new Dictionary<string, MorphDelta[]>( StringComparer.OrdinalIgnoreCase );
		for ( int flexIndex = 0; flexIndex < mesh.NumFlexes; flexIndex++ )
		{
			var flex = mdl.GetMeshFlex( mesh, flexIndex );

			if ( flex.FlexDesc < 0 || flex.FlexDesc >= flexNames.Length )
				continue;

			var name = flexNames[flex.FlexDesc];
			if ( string.IsNullOrWhiteSpace( name ) )
				continue;
			var pairName = flex.FlexPair == 0 ? null : flexNames[flex.FlexPair];
			if ( pairName is not null && string.IsNullOrWhiteSpace( pairName ) )
				throw new InvalidDataException( "MDL paired flex has an empty descriptor name." );
			if ( string.Equals( name, pairName, StringComparison.OrdinalIgnoreCase ) )
				pairName = null;

			var vertAnims = mdl.GetFlexVertAnims( flex );
			var deltaCount = 0;
			var pairedDeltaCount = 0;
			foreach ( var anim in vertAnims )
			{
				if ( originalToOutput[anim.Index] < 0 )
					continue;
				if ( pairName is null || anim.Side < 255 )
					deltaCount++;
				if ( pairName is not null && anim.Side > 0 )
					pairedDeltaCount++;
			}
			if ( deltaCount == 0 && pairedDeltaCount == 0 )
				continue;

			var deltas = deltaCount == 0 ? Array.Empty<MorphDelta>() : GC.AllocateUninitializedArray<MorphDelta>( deltaCount );
			var pairedDeltas = pairedDeltaCount == 0 ? Array.Empty<MorphDelta>() : GC.AllocateUninitializedArray<MorphDelta>( pairedDeltaCount );
			var deltaIndex = 0;
			var pairedDeltaIndex = 0;

			foreach ( var anim in vertAnims )
			{
				var remappedIndex = originalToOutput[anim.Index];
				if ( remappedIndex < 0 )
					continue;

				Vector3 posDelta, normalDelta;

				if ( useFixedPoint )
				{
					posDelta = new Vector3(
						anim.DeltaX * fixedPointScale,
						anim.DeltaY * fixedPointScale,
						anim.DeltaZ * fixedPointScale
					);
					normalDelta = new Vector3(
						anim.NormalDeltaX * fixedPointScale,
						anim.NormalDeltaY * fixedPointScale,
						anim.NormalDeltaZ * fixedPointScale
					);
				}
				else
				{
					// Legacy format: deltas stored as float16 (half-float)
					posDelta = new Vector3(
						HalfToFloat( anim.DeltaX ),
						HalfToFloat( anim.DeltaY ),
						HalfToFloat( anim.DeltaZ )
					);
					normalDelta = new Vector3(
						HalfToFloat( anim.NormalDeltaX ),
						HalfToFloat( anim.NormalDeltaY ),
						HalfToFloat( anim.NormalDeltaZ )
					);
				}

				var pairWeight = pairName is null ? 0 : anim.Side * (1.0f / 255.0f);
				if ( pairWeight < 1 )
					deltas[deltaIndex++] = new MorphDelta( remappedIndex, posDelta * (1 - pairWeight), normalDelta * (1 - pairWeight) );
				if ( pairWeight > 0 )
					pairedDeltas[pairedDeltaIndex++] = new MorphDelta( remappedIndex, posDelta * pairWeight, normalDelta * pairWeight );
			}

			if ( deltas.Length > 0 )
				morphs[name] = deltas;
			if ( pairedDeltas.Length > 0 )
				morphs[pairName] = pairedDeltas;
		}

		var bounds = morphs.Count > 0 ? new BBox[vertexCount] : null;
		foreach ( var (name, deltas) in morphs )
		{
			foreach ( ref var delta in deltas.AsSpan() )
			{
				if ( !IsFinite( delta.PositionDelta ) || !IsFinite( delta.NormalDelta ) )
					throw new InvalidDataException( "Model morph contains a non-finite delta." );
				ref var vertexBounds = ref bounds[delta.VertexIndex];
				vertexBounds.Mins += Vector3.Min( Vector3.Zero, delta.PositionDelta );
				vertexBounds.Maxs += Vector3.Max( Vector3.Zero, delta.PositionDelta );
			}
			if ( vertexTransforms is not null )
				foreach ( ref var delta in deltas.AsSpan() )
					delta = new MorphDelta( delta.VertexIndex, vertexTransforms[delta.VertexIndex].Direction( delta.PositionDelta ),
						vertexTransforms[delta.VertexIndex].Direction( delta.NormalDelta ) );
			renderMesh.AddMorph( name, deltas.AsSpan() );
		}
		return bounds;
	}

	private static float HalfToFloat( short value )
	{
		return (float)BitConverter.Int16BitsToHalf( value );
	}
}
