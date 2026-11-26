using NativeEngine;

namespace Sandbox.Animation;

/// <summary>
/// A compiled animation clip (.anim) - a compressed pose stream authored against a
/// <see cref="Skeleton"/>.
/// </summary>
public sealed partial class Clip : Resource, IDisposable
{
	internal HAnimClip native;
	private bool shared;

	public static ClipBuilder Builder => new();

	public override bool IsValid => native.IsValid;

	/// <summary>
	/// Whether the clip is invalid, or has not yet loaded.
	/// </summary>
	public override bool IsError => native.IsNull || !native.IsStrongHandleValid();

	/// <summary>
	/// Clip resource name.
	/// </summary>
	public string Name => ResourcePath;

	private Clip( HAnimClip native, string name, bool procedural )
	{
		this.native = native;
		shared = !procedural;
		SetResourceIdentity( name, procedural ? null : native.GetGuid() );
		if ( !NativeGlue.Resources.IsAnonymousResource( native.GetBindingPtr() ) )
			Game.Resources.RegisterWeak( this );
	}

	/// <summary>
	/// Load an animation clip from the given file.
	/// </summary>
	public static Clip Load( string filename ) => Load( (ResourceId)filename );

	internal static Clip Load( ResourceId id )
	{
		ThreadSafe.AssertIsMainThread();

		if ( id.Guid is Guid guid && Game.Resources.TryGet<Clip>( guid, out var resource ) )
			return resource;

		var path = FixPath( id.ResolvePath() );
		if ( string.IsNullOrWhiteSpace( path ) && id.Guid is null )
			return null;

		if ( Mounting.MountUtility.IsMountPath( path ) )
			return Mounting.Directory.TryLoad( path, out var mounted ) ? mounted as Clip : null;

		return FromNative( NativeGlue.Resources.GetAnimClip( path, id.Guid ?? Guid.Empty ), path );
	}

	internal override void OnMounted( string path )
	{
		ThreadSafe.AssertIsMainThread();
		if ( shared )
		{
			if ( ResourcePath != FixPath( path ) )
				throw new InvalidOperationException( $"Mounted animation clip '{path}' must be created by its loader, not shared from '{ResourcePath}'." );
			if ( Game.Resources.Get<Clip>( ResourcePath ) != this )
				Game.Resources.RegisterWeak( this );
			return;
		}

		RegisterWeakResourceId( path );
		shared = true;
	}

	public static Task<Clip> LoadAsync( string filename )
	{
		ThreadSafe.AssertIsMainThread();
		return ResourceLibrary.LoadAsync<Clip>( FixPath( filename ) );
	}

	public void Dispose()
	{
		if ( !shared )
			Destroy();
	}

	/// <summary>
	/// Try to make it so only one Clip class exists for each clip
	/// </summary>
	internal static Clip FromNative( HAnimClip native, string name, bool procedural = false )
	{
		if ( native.IsNull )
			return null;

		if ( !native.IsStrongHandleValid() )
		{
			native.DestroyStrongHandle();
			return null;
		}

		var instanceId = native.GetBindingPtr().ToInt64();
		if ( NativeResourceCache.TryGetValue<Clip>( instanceId, out var clip ) )
		{
			// If we're using a cached one we don't need this handle, we'll leak
			native.DestroyStrongHandle();
			if ( !NativeGlue.Resources.IsAnonymousResource( clip.native.GetBindingPtr() ) )
				Game.Resources.RegisterWeak( clip );
			return clip;
		}

		clip = new Clip( native, name, procedural );
		NativeResourceCache.Add( instanceId, clip );

		return clip;
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

	internal override void OnReloaded()
	{
		_skeleton = default;
		_events = default;
		base.OnReloaded();
	}

	/// <summary>
	/// How many frames of pose data this clip holds.
	/// </summary>
	public int FrameCount => native.GetNumFrames();

	/// <summary>
	/// Length of the clip in seconds.
	/// </summary>
	public float Duration => native.GetDuration();

	/// <summary>
	/// Frames per second. Zero for single frame clips.
	/// </summary>
	public float FrameRate => native.GetFPS();

	/// <summary>
	/// Whether this clip is a difference to be layered onto another pose rather than a pose itself.
	/// </summary>
	public bool IsAdditive => native.IsAdditive();

	/// <summary>
	/// How many bone tracks the clip encodes.
	/// </summary>
	public int TrackCount => native.GetNumTracks();

	/// <summary>
	/// How many sync events the clip is divided into. Sync events let clips of different
	/// lengths be blended by phase rather than by time.
	/// </summary>
	public int SyncEventCount => native.GetNumSyncEvents();

	/// <summary>
	/// The name of the skeleton this clip was authored against.
	/// </summary>
	public string SkeletonName => native.GetSkeletonName();

	Skeleton _skeleton;

	/// <summary>
	/// The skeleton this clip was authored against. Null if it failed to load.
	/// </summary>
	public Skeleton Skeleton => _skeleton.IsValid() ? _skeleton : _skeleton = Skeleton.FromNative( native.GetSkeleton(), SkeletonName );

	/// <summary>
	/// Whether the clip's skeleton has loaded. Sampling returns identity transforms until it has.
	/// </summary>
	public bool IsBound => native.IsBound();

	/// <summary>
	/// Sample one bone's transform, relative to its parent bone.
	/// </summary>
	/// <param name="cycle">Position through the clip, from 0 to 1.</param>
	/// <param name="bone">Index of the bone in <see cref="Sandbox.Animation.Skeleton.Bones"/>.</param>
	/// <exception cref="ArgumentOutOfRangeException">The cycle is not finite or outside 0 to 1, or the bone index is outside the clip's bone range.</exception>
	public Transform GetLocalTransform( float cycle, int bone )
	{
		ValidateCycle( cycle );
		ArgumentOutOfRangeException.ThrowIfNegative( bone );

		var boneOffset = Skeleton?.BoneOffset ?? 0;
		if ( bone >= TrackCount - boneOffset )
			throw new ArgumentOutOfRangeException( nameof( bone ) );

		return native.GetBoneParentSpaceTransformS2( cycle, bone + boneOffset );
	}

	/// <summary>
	/// Sample the root motion transform, relative to where the clip started.
	/// </summary>
	/// <param name="cycle">Position through the clip, from 0 to 1.</param>
	/// <exception cref="ArgumentOutOfRangeException">The cycle is not finite or outside 0 to 1.</exception>
	public Transform GetRootMotion( float cycle )
	{
		ValidateCycle( cycle );
		return native.GetRootMotionTransformS2( cycle );
	}

	/// <summary>
	/// Sample a named float channel at a position through the clip, from 0 to 1.
	/// Morph weights belong to the "MorphChannel" set. Returns false if the channel is absent.
	/// </summary>
	public bool TryGetFloatChannel( float cycle, string set, string channel, out float value )
	{
		ValidateCycle( cycle );
		ArgumentException.ThrowIfNullOrWhiteSpace( set );
		ArgumentException.ThrowIfNullOrWhiteSpace( channel );
		return native.GetFloatChannelValue( cycle, set, channel, out value );
	}

	private static void ValidateCycle( float cycle )
	{
		if ( !float.IsFinite( cycle ) || cycle is < 0f or > 1f )
			throw new ArgumentOutOfRangeException( nameof( cycle ), cycle, "Cycle must be finite and between 0 and 1." );
	}

	/// <summary>
	/// Average speed the root moves at over the clip, in units per second.
	/// </summary>
	public float AverageLinearVelocity => native.GetAverageLinearVelocity();
}
