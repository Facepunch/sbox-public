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
		var parameters = FirstPersonParameters( source.Animation.Graph, sourceController );
		for ( int i = 0; i < parameters.Length; i++ )
		{
			var (name, index, kind) = parameters[i];
			switch ( kind )
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

	/// <summary>
	/// Set the world space transforms of the first bones, one per entry, as <see cref="SetBoneWorldTransform"/> would one at a
	/// time. The model and its bone count are read once and the change is reported once, by the last bone going through
	/// <see cref="SetBoneWorldTransform"/>.
	/// </summary>
	internal void SetBoneWorldTransforms( ReadOnlySpan<Transform> transforms )
	{
		var count = Math.Min( transforms.Length, Model?.BoneCount ?? 0 );
		if ( count <= 0 ) return;

		for ( int i = 0; i < count - 1; i++ )
			animNative.SetWorldSpaceRenderBoneTransform( i, transforms[i] );

		SetBoneWorldTransform( count - 1, transforms[count - 1] );
	}

	IReadOnlyList<Sandbox.Animation.Graph.Parameter> _firstPersonParameterSource;
	IntPtr _firstPersonParameterController;
	(StringToken Name, int Index, Sandbox.Animation.GraphValueKind Kind)[] _firstPersonParameters = [];

	/// <summary>
	/// The source graph's parameters that its controller has, with their tokens, indices and types. Resolved once per graph
	/// and controller rather than looked up by name every frame.
	/// </summary>
	(StringToken Name, int Index, Sandbox.Animation.GraphValueKind Kind)[] FirstPersonParameters( Sandbox.Animation.Graph graph, NativeEngine.AnimationGraph2Controller controller )
	{
		var parameters = graph.Parameters;
		if ( ReferenceEquals( parameters, _firstPersonParameterSource ) && _firstPersonParameterController == (IntPtr)controller )
			return _firstPersonParameters;

		var resolved = new List<(StringToken, int, Sandbox.Animation.GraphValueKind)>( parameters.Count );
		for ( int i = 0; i < parameters.Count; i++ )
		{
			var name = new StringToken( parameters[i].Name );
			var index = controller.GetControlParameterIndex( name );
			if ( index < 0 ) continue;
			resolved.Add( (name, index, controller.GetControlParameterType( index )) );
		}

		_firstPersonParameterSource = parameters;
		_firstPersonParameterController = controller;
		_firstPersonParameters = resolved.ToArray();
		return _firstPersonParameters;
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
