using System.Runtime.InteropServices;

namespace Sandbox;

public sealed partial class SceneModel
{
	internal NativeEngine.AnimationGraph2Controller animGraph2Native => animNative.IsNull ? default : animNative.GetAnimGraph2Controller();

	/// <summary>Drive an independent first-person preview with the body's live animation inputs.</summary>
	internal void PrepareFirstPersonAnimation( SceneModel source, bool enabled )
	{
		Animation.Graph = source.Animation.Graph;
		if ( source.Animation.Graph is null )
		{
			PrepareLegacyFirstPersonAnimation( source, enabled );
			return;
		}

		UseAnimGraph = enabled && source.UseAnimGraph && source.Animation.IsValid;
		if ( !UseAnimGraph ) return;
		// Controllers can be created lazily when a graph is first evaluated or reloaded.
		if ( !Animation.IsValid ) Update( 0 );
		if ( !Animation.IsValid )
		{
			UseAnimGraph = false;
			return;
		}

		var sourceController = source.animGraph2Native;
		var targetController = animGraph2Native;
		var parameters = source.Animation.Graph.Parameters;
		for ( int i = 0; i < parameters.Count; i++ )
		{
			var name = parameters[i].Name;
			var index = sourceController.GetControlParameterIndex( name );
			if ( index < 0 ) continue;
			switch ( sourceController.GetControlParameterType( index ) )
			{
				case Sandbox.Animation.GraphValueKind.Bool:
					targetController.SetParamBool( name, sourceController.GetParamBool( name ) );
					break;
				case Sandbox.Animation.GraphValueKind.Float:
					targetController.SetParamFloat( name, sourceController.GetParamFloat( name ) );
					break;
				case Sandbox.Animation.GraphValueKind.Vector:
					targetController.SetParamVector( name, sourceController.GetParamVector( name ) );
					break;
				case Sandbox.Animation.GraphValueKind.Id:
					targetController.SetParamID( name, new StringToken( sourceController.GetControlParameterIDHash( index ) ) );
					break;
				case Sandbox.Animation.GraphValueKind.Target:
					// Preserve bone targets and unset targets as well as transform targets.
					if ( source.Animation.Debug.TryGetValue( index, out var target ) )
						targetController.SetParamTarget( name, target.TargetTransform, target.IsTargetSet, target.IsBoneTarget, target.TargetBoneId );
					break;
			}
		}
		Animation.Parameters.TrySet( "b_firstperson", true );
	}

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
