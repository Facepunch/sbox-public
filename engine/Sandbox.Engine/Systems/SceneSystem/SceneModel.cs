using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// A model scene object that supports animations and can be rendered within a <see cref="SceneWorld"/>.
/// </summary>
[Expose]
public sealed partial class SceneModel : SceneObject
{
	internal CSceneAnimatableObject animNative;

	DelegateFunctionPointer animGraphChangedCallback;

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	internal delegate void AnimGraphChangedCallback( IntPtr p );

	internal SceneModel() { }
	internal SceneModel( HandleCreationData _ ) { }

	public SceneModel( SceneWorld sceneWorld, string model, Transform transform ) : this( sceneWorld, Model.Load( model ), transform ) { }

	public SceneModel( SceneWorld sceneWorld, Model model, Transform transform )
	{
		Assert.IsValid( sceneWorld );
		Assert.NotNull( model );

		if ( !model.HasRenderMeshes() ) model = Model.Error;

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			var flags = Rendering.SceneObjectFlags.CastShadows | Rendering.SceneObjectFlags.IsLoaded;
			var typeFlags = ESceneObjectTypeFlags.NONE;

			MeshSystem.CreateSceneObject( model.native, transform, "AnimatableSceneObjectDesc", flags, typeFlags, sceneWorld, 0x1 );

			if ( !animNative.IsValid )
			{
				Log.Warning( "SceneModel: Didn't get a valid native pointer" );
				throw new System.ArgumentException( "Error creating AnimSceneObject - possible invalid model?" );
			}

			Transform = transform;

			if ( animGraphChangedCallback == DelegateFunctionPointer.Null )
			{
				animGraphChangedCallback = DelegateFunctionPointer.Get<AnimGraphChangedCallback>( OnAnimGraphChanged );
			}

			// TODO
			animNative.InitAnimGraph( animGraphChangedCallback );
		}
	}

	private void OnAnimGraphChanged( IntPtr p )
	{
		_animationGraph = AnimationGraph.FromNative( animNative.GetAnimGraph() );
	}

	internal override void OnNativeInit( CSceneObject ptr )
	{
		base.OnNativeInit( ptr );

		animNative = (NativeEngine.CSceneAnimatableObject)ptr;
	}

	internal override void OnNativeDestroy()
	{
		animNative = default;
		base.OnNativeDestroy();
	}

	/// <summary>
	/// Override the anim graph this scene model uses
	/// </summary>
	[System.Obsolete( "Can use AnimationGraph directly" )]
	public void SetAnimGraph( string name )
	{
		animNative.SetAnimGraph( name );
	}

	private AnimationGraph _animationGraph;

	public AnimationGraph AnimationGraph
	{
		get => _animationGraph;
		set
		{
			animNative.SetAnimGraph( value?.native ?? default );
		}
	}

	public float PlaybackRate
	{
		get => animNative.GetPlaybackRate();
		set => animNative.SetPlaybackRate( value );
	}

	internal override void OnModelChanged()
	{
		base.OnModelChanged();
	}

	/// <summary>
	/// Sets the world space bone transform of a bone by its index.
	/// </summary>
	/// <param name="boneIndex">Bone index to set transform of.</param>
	/// <param name="transform"></param>
	public void SetBoneWorldTransform( int boneIndex, Transform transform )
	{
		// TODO: Throw on index OOB
		animNative.SetWorldSpaceRenderBoneTransform( boneIndex, transform );
	}

	/// <summary>
	/// Returns the world space transform of a bone by its index.
	/// </summary>
	/// <param name="boneIndex">Index of the bone to calculate transform of.</param>
	/// <returns>The world space transform, or an identity transform on failure.</returns>
	public Transform GetBoneWorldTransform( int boneIndex )
	{
		// TODO: Throw on index OOB
		// TODO: Returns nullable to match GetAttachment()?
		return animNative.GetWorldSpaceRenderBoneTransform( boneIndex );
	}

	/// <summary>
	/// Fill <paramref name="dest"/> with this frame's world space bone transforms in a single interop call,
	/// indexed by bone index. Entries past the model's bone count are left as identity. Much cheaper than
	/// calling <see cref="GetBoneWorldTransform(int)"/> in a loop.
	/// </summary>
	internal unsafe void GetBoneWorldTransforms( Span<Transform> dest )
	{
		if ( animNative.IsNull || dest.IsEmpty )
			return;

		fixed ( Transform* p = dest )
			animNative.GetWorldSpaceRenderBoneTransforms( dest.Length, (IntPtr)p );
	}

	/// <summary>
	/// Fill <paramref name="dest"/> with the previous frame's world space bone transforms in a single interop
	/// call - the counterpart to <see cref="GetBoneWorldTransforms"/>, for computing per-bone motion.
	/// </summary>
	internal unsafe void GetBoneWorldPreviousTransforms( Span<Transform> dest )
	{
		if ( animNative.IsNull || dest.IsEmpty )
			return;

		fixed ( Transform* p = dest )
			animNative.GetWorldSpaceRenderBonePreviousTransforms( dest.Length, (IntPtr)p );
	}

	/// <summary>
	/// Fill <paramref name="dest"/> with the parent-space bone transforms in a single interop call, indexed by
	/// bone index. Entries past the model's bone count are left as identity. Much cheaper than calling
	/// <see cref="GetParentSpaceBone(int)"/> in a loop.
	/// </summary>
	internal unsafe void GetParentSpaceBones( Span<Transform> dest )
	{
		if ( animNative.IsNull || dest.IsEmpty )
			return;

		fixed ( Transform* p = dest )
			animNative.GetParentSpaceBones( dest.Length, (IntPtr)p );
	}

	/// <summary>
	/// Returns the world space transform of a bone by its name.
	/// </summary>
	/// <param name="boneName">Name of the bone to calculate transform of.</param>
	/// <returns>The world space transform, or an identity transform on failure.</returns>
	public Transform GetBoneWorldTransform( string boneName )
	{
		// TODO: Returns nullable to match GetAttachment()?
		return animNative.GetWorldSpaceRenderBoneTransform( boneName );
	}

	/// <summary>
	/// Returns the local space transform of a bone by its index.
	/// </summary>
	/// <param name="boneIndex">Index of the bone to calculate transform of.</param>
	/// <returns>The local space transform, or an identity transform on failure.</returns>
	public Transform GetBoneLocalTransform( int boneIndex )
	{
		// TODO: Throw on index OOB
		// TODO: Returns nullable to match GetAttachment()?
		return animNative.GetLocalSpaceRenderBoneTransform( boneIndex );
	}

	/// <summary>
	/// Returns the local space transform of a bone by its name.
	/// </summary>
	/// <param name="boneName">Name of the bone to calculate transform of.</param>
	/// <returns>The local space transform, or an identity transform on failure.</returns>
	public Transform GetBoneLocalTransform( string boneName )
	{
		// TODO: Returns nullable to match GetAttachment()?
		return animNative.GetLocalSpaceRenderBoneTransform( boneName );
	}

	internal Transform GetWorldSpaceAnimationTransform( int boneIndex )
	{
		return animNative.GetWorldSpaceAnimationTransform( boneIndex );
	}

	/// <summary>
	/// Set material group to replace materials of the model as set up in ModelDoc.
	/// </summary>
	public new void SetMaterialGroup( string name ) // TODO - REMOVE ME
	{
		base.SetMaterialGroup( name );
	}

	/// <summary>
	/// Set which body group to use.
	/// </summary>
	public void SetBodyGroup( string name, int value )
	{
		animNative.SetBodyGroup( name, value );
	}

	/// <summary>
	/// Get attachment transform by name.
	/// </summary>
	/// <param name="name">Name of the attachment to calculate transform of.</param>
	/// <param name="worldspace">Whether the transform should be in world space (relative to the scene world), or local space (relative to the scene object)</param>
	/// <returns></returns>
	public Transform? GetAttachment( string name, bool worldspace = true )
	{
		if ( animNative.SBox_GetAttachment( name, worldspace, out var tx ) )
			return tx;

		return null;
	}

	/// <summary>
	/// Allows the scene model to not use the anim graph so it can play sequences directly
	/// </summary>
	public bool UseAnimGraph
	{
		get => animNative.GetShouldUseAnimGraph();
		set
		{
			if ( UseAnimGraph == value ) return;

			animNative.SetShouldUseAnimGraph( value );
		}
	}

	/// <summary>
	/// Get the calculated motion from animgraph since last frame
	/// </summary>
	public Transform RootMotion
	{
		get => animNative.GetRootMotion();
	}

	SceneObjectAnimationSequence _currentSequence;

	/// <summary>
	/// Allows playback of sequences directly, rather than using an animation graph.
	/// Requires <see cref="UseAnimGraph"/> disabled if the scene model has one.
	/// </summary>
	public AnimationSequence CurrentSequence
	{
		get
		{
			// Create on first access
			_currentSequence ??= new SceneObjectAnimationSequence( this );

			return _currentSequence;
		}
	}

	SceneObjectMorphCollection _morphs;

	/// <summary>
	/// Access this sceneobject's morph collection. Morphs are generally used in the model to control
	/// the face, for things like emotions and lip sync.
	/// </summary>
	public MorphCollection Morphs
	{
		get
		{
			// Create on first access
			_morphs ??= new SceneObjectMorphCollection( this );

			return _morphs;
		}
	}

	SceneObjectDirectPlayback _directPlayback;

	/// <summary>
	/// Access this sceneobject's direct playback. Direct playback is used to control the direct playback node in an animgraph
	/// to play sequences directly in code
	/// </summary>
	public AnimGraphDirectPlayback DirectPlayback
	{
		get
		{
			// Create on first access
			_directPlayback ??= new SceneObjectDirectPlayback( this );

			return _directPlayback;
		}
	}
}
