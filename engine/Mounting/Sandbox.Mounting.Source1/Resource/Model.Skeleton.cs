using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private static string[] AddSkeleton( ModelBuilder builder, MdlFile mdl,
		BBox?[] boneBounds, Transform[] boneTransforms, bool parentLocal )
	{
		var boneNames = new string[mdl.Bones.Length];
		var bones = new ModelBuilder.Bone[mdl.Bones.Length];
		for ( int i = 0; i < mdl.Bones.Length; i++ )
		{
			var name = mdl.GetBoneName( i );
			boneNames[i] = string.IsNullOrWhiteSpace( name ) ? $"_bone{i}" : name;
		}
		for ( int i = 0; i < mdl.Bones.Length; i++ )
		{
			var bone = mdl.Bones[i];
			var transform = parentLocal && bone.Parent != -1
				? boneTransforms[bone.Parent].ToLocal( boneTransforms[i] )
				: boneTransforms[i];
			bones[i] = new ModelBuilder.Bone(
				boneNames[i],
				bone.Parent == -1 ? null : boneNames[bone.Parent],
				transform.Position,
				transform.Rotation
			)
			{
				Bounds = boneBounds[i]
			};
		}
		if ( bones.Length > 0 )
			builder.AddBones( bones );
		return boneNames;
	}

	private static Transform[] GetBoneTransforms( MdlFile mdl, Transform[] referencePose )
	{
		if ( referencePose is not null && referencePose.Length != mdl.Bones.Length )
			throw new ArgumentException( "Reference pose must match the MDL skeleton.", nameof( referencePose ) );
		var boneTransforms = new Transform[mdl.Bones.Length];
		var boneStates = new byte[mdl.Bones.Length];
		var ancestors = new Stack<int>();
		for ( int i = 0; i < mdl.Bones.Length; i++ )
		{
			var current = i;
			while ( current != -1 && boneStates[current] != 2 )
			{
				if ( boneStates[current] == 1 )
					throw new InvalidDataException( "MDL bone hierarchy contains a cycle." );
				var parent = mdl.Bones[current].Parent;
				if ( parent < -1 || parent >= mdl.Bones.Length )
					throw new InvalidDataException( "MDL bone has an invalid parent." );
				boneStates[current] = 1;
				ancestors.Push( current );
				current = parent;
			}
			while ( ancestors.TryPop( out current ) )
			{
				var bone = mdl.Bones[current];
				var tx = referencePose?[current] ?? new Transform( bone.Position, bone.Quaternion );
				boneTransforms[current] = bone.Parent == -1 ? tx : boneTransforms[bone.Parent].ToWorld( tx );
				boneStates[current] = 2;
			}
		}
		return boneTransforms;
	}

	private static void AddBoneBounds( MdlFile mdl, ReadOnlySpan<SkinnedVertex> vertices,
		ReadOnlySpan<BBox> morphBounds, BBox?[] boneBounds )
	{
		if ( boneBounds.Length == 0 )
			return;

		var hasMorphs = !morphBounds.IsEmpty;
		for ( var i = 0; i < vertices.Length; ++i )
		{
			var vertex = vertices[i];
			var position = vertex.position;
			var extents = Vector3.Zero;
			if ( hasMorphs )
			{
				position += morphBounds[i].Center;
				extents = morphBounds[i].Extents;
			}

			Add( vertex.blendIndices.r, vertex.blendWeights.r );
			Add( vertex.blendIndices.g, vertex.blendWeights.g );
			Add( vertex.blendIndices.b, vertex.blendWeights.b );

			void Add( int boneIndex, byte weight )
			{
				if ( weight == 0 )
					return;
				var matrix = mdl.Bones[boneIndex].PoseToBone;
				var center = TransformToBone( position, matrix );
				var bounds = new BBox( center, center );
				if ( hasMorphs )
				{
					var boneExtents = new Vector3(
						MathF.Abs( matrix.M11 ) * extents.x + MathF.Abs( matrix.M12 ) * extents.y + MathF.Abs( matrix.M13 ) * extents.z,
						MathF.Abs( matrix.M21 ) * extents.x + MathF.Abs( matrix.M22 ) * extents.y + MathF.Abs( matrix.M23 ) * extents.z,
						MathF.Abs( matrix.M31 ) * extents.x + MathF.Abs( matrix.M32 ) * extents.y + MathF.Abs( matrix.M33 ) * extents.z );
					bounds = new BBox( center - boneExtents, center + boneExtents );
				}
				boneBounds[boneIndex] = boneBounds[boneIndex]?.AddBBox( bounds ) ?? bounds;
			}
		}
	}

	private static Vector3 TransformToBone( Vector3 position, MdlFile.Matrix3x4 matrix ) => new(
		matrix.M11 * position.x + matrix.M12 * position.y + matrix.M13 * position.z + matrix.M14,
		matrix.M21 * position.x + matrix.M22 * position.y + matrix.M23 * position.z + matrix.M24,
		matrix.M31 * position.x + matrix.M32 * position.y + matrix.M33 * position.z + matrix.M34 );

}
