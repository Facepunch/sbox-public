using System.Runtime.InteropServices;

namespace Sandbox;

public sealed partial class SceneModel
{
	internal NativeEngine.AnimationGraph2Controller animGraph2Native => animNative.IsNull ? default : animNative.GetAnimGraph2Controller();

	private Sandbox.Animation.AnimationAccessor _animation;
	private Sandbox.Animation.Graph _graphDefinition;
	private DelegateFunctionPointer _animationControllerCreatedCallback;

	/// <summary>
	/// May run on an animation worker thread before evaluation. Handlers must only restore animation state.
	/// </summary>
	internal event Action AnimationControllerCreated;

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	private delegate void AnimationControllerCreatedCallback();

	private void RegisterAnimationControllerCallback()
	{
		if ( _animationControllerCreatedCallback == DelegateFunctionPointer.Null )
			_animationControllerCreatedCallback = DelegateFunctionPointer.Get<AnimationControllerCreatedCallback>( OnAnimationControllerCreated );

		animNative.SetAnimGraph2ControllerCreatedCallback( _animationControllerCreatedCallback );
	}

	private void OnAnimationControllerCreated()
	{
		_animation?.ApplyStoredParameters();
		AnimationControllerCreated?.Invoke();
	}

	/// <summary>Runtime access to this model's animation graph.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public Sandbox.Animation.AnimationAccessor Animation => _animation ??= new( this );

	/// <summary>
	/// Skip animating any bone the skeleton marks as high LOD. Cheaper, for models that are
	/// far away or unimportant. Skipped bones stay in their bind pose.
	/// </summary>
	public bool LowDetailAnimation
	{
		get => animNative.IsLowDetailAnimation();
		set => animNative.SetLowDetailAnimation( value );
	}

	internal Sandbox.Animation.Graph GraphOverride => _graphDefinition;

	internal Sandbox.Animation.Graph GraphDefinition
	{
		get => _graphDefinition ?? Model?.AnimationGraph;
		set
		{
			if ( _graphDefinition == value ) return;

			_graphDefinition = value;
			animNative.SetAnimGraph2( value?.native ?? default );
		}
	}
}
