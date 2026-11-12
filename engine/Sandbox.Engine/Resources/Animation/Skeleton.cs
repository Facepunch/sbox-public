using NativeEngine;

namespace Sandbox.Animation;

/// <summary>
/// A compiled skeleton (.skel) - a bone hierarchy and its reference pose. This is the
/// skeleton animation clips and graphs are authored against, independent of any one model.
/// </summary>
public sealed class Skeleton : Resource
{
	internal HAnimSkeleton native;

	public override bool IsValid => native.IsValid;

	/// <summary>
	/// Whether the skeleton is invalid, or has not yet loaded.
	/// </summary>
	public override bool IsError => native.IsNull || !native.IsStrongHandleValid();

	/// <summary>
	/// Skeleton resource name.
	/// </summary>
	public string Name => ResourcePath;

	private Skeleton( HAnimSkeleton native, string name )
	{
		this.native = native;
		RegisterWeakResourceId( name, native.GetGuid() );
	}

	/// <summary>
	/// Load a skeleton from the given file.
	/// </summary>
	public static Skeleton Load( string filename ) => Load( (ResourceId)filename );

	internal static Skeleton Load( ResourceId id )
	{
		ThreadSafe.AssertIsMainThread();

		if ( id.Guid is Guid guid && Game.Resources.TryGet<Skeleton>( guid, out var resource ) )
			return resource;

		var path = id.ResolvePath();
		return FromNative( NativeGlue.Resources.GetAnimSkeleton( path, id.Guid ?? Guid.Empty ), path );
	}

	/// <summary>
	/// Try to make it so only one Skeleton class exists for each skeleton
	/// </summary>
	internal static Skeleton FromNative( HAnimSkeleton native, string name )
	{
		if ( native.IsNull )
			return null;

		if ( !native.IsStrongHandleValid() )
		{
			native.DestroyStrongHandle();
			return null;
		}

		var instanceId = native.GetBindingPtr().ToInt64();
		if ( NativeResourceCache.TryGetValue<Skeleton>( instanceId, out var skeleton ) )
		{
			// If we're using a cached one we don't need this handle, we'll leak
			native.DestroyStrongHandle();
			Game.Resources.RegisterWeak( skeleton );
			return skeleton;
		}

		skeleton = new Skeleton( native, name );
		NativeResourceCache.Add( instanceId, skeleton );

		return skeleton;
	}

	internal override void Destroy()
	{
		if ( !native.IsNull )
		{
			var n = native;
			native = default;

			// Evict from NativeResourceCache so a new wrapper can be created if the same
			// native pointer is reused
			NativeResourceCache.Remove( n.GetBindingPtr().ToInt64() );

			MainThread.Queue( () => n.DestroyStrongHandle() );
		}

		base.Destroy();
	}

	/// <summary>
	/// Recompiling a skeleton reloads it in place, so everything read out of it once is stale.
	/// </summary>
	internal override void OnReloaded()
	{
		_bones = default;
		_maskNames = default;
		_secondarySkeletons = default;

		base.OnReloaded();
	}

	/// <summary>
	/// How many bones this skeleton has.
	/// </summary>
	public int BoneCount => native.GetNumBones() - BoneOffset;

	/// <summary>
	/// How many bones are sampled at the lowest level of detail. Bones are ordered so these
	/// come first, letting distant characters animate only the leading part of the hierarchy.
	/// </summary>
	public int LowLodBoneCount => native.GetLowLODBoneCount() - BoneOffset;

	/// <summary>
	/// Art with several root bones compiles to a made up root holding them all together, since a
	/// skeleton can only have one. Nobody authored it, so it's left out of everything here and the
	/// bones hanging off it read back as the roots they are in the art - which means a bone index
	/// out here is this far along the real one.
	/// </summary>
	internal int BoneOffset => native.HasSyntheticRootBone() ? 1 : 0;

	/// <summary>
	/// Whether this skeleton belongs to a prop rather than a character.
	/// </summary>
	public bool IsProp => native.IsPropSkeleton();

	/// <summary>
	/// One bone of a <see cref="Skeleton"/>, in its reference pose.
	/// </summary>
	/// <param name="Index">Position in <see cref="Bones"/>.</param>
	/// <param name="Name">Bone name.</param>
	/// <param name="ParentIndex">Index of this bone's parent, or -1 for a root.</param>
	/// <param name="LocalTransform">Reference pose relative to the parent bone.</param>
	/// <param name="Transform">Reference pose relative to the model's origin.</param>
	public sealed record Bone( int Index, string Name, int ParentIndex, Transform LocalTransform, Transform Transform );

	List<Bone> _bones;

	/// <summary>
	/// The bones of this skeleton, in their reference pose. Parents always come before their
	/// children, so this can be walked in order.
	/// </summary>
	public IReadOnlyList<Bone> Bones => _bones ??= ReadBones();

	List<Bone> ReadBones()
	{
		var offset = BoneOffset;
		var count = native.GetNumBones();
		var list = new List<Bone>( count - offset );

		for ( var i = offset; i < count; i++ )
		{
			list.Add( new Bone( i - offset, native.GetBoneName( i ), native.GetParentBoneIndex( i ) - offset,
				native.GetBoneParentSpaceTransformS2( i ), native.GetBoneModelSpaceTransformS2( i ) ) );
		}

		return list;
	}

	/// <summary>
	/// How many bone masks this skeleton defines. Masks weight an animation's influence per bone.
	/// </summary>
	public int MaskCount => native.GetNumMaskDefinitions();

	List<string> _maskNames;

	/// <summary>
	/// The names of this skeleton's bone masks. These are what an animation graph names to
	/// weight a pose per bone - a mask authored on a model won't resolve.
	/// </summary>
	public IReadOnlyList<string> MaskNames => _maskNames ??= ReadMaskNames();

	List<string> ReadMaskNames()
	{
		var count = native.GetNumMaskDefinitions();
		var list = new List<string>( count );

		for ( var i = 0; i < count; i++ )
		{
			list.Add( native.GetMaskDefinitionName( i ) );
		}

		return list;
	}

	List<string> _secondarySkeletons;

	/// <summary>
	/// Skeletons attached to this one - a held weapon's skeleton, for example. These are
	/// resource names, they aren't loaded automatically.
	/// </summary>
	public IReadOnlyList<string> SecondarySkeletons => _secondarySkeletons ??= ReadSecondarySkeletons();

	List<string> ReadSecondarySkeletons()
	{
		var count = native.GetNumSecondarySkeletons();
		var list = new List<string>( count );

		for ( var i = 0; i < count; i++ )
		{
			list.Add( native.GetSecondarySkeletonName( i ) );
		}

		return list;
	}
}
