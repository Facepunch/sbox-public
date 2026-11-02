namespace Sandbox;

public sealed partial class SkinnedModelRenderer
{
	Sandbox.Animation.Graph _graphDefinition;
	Sandbox.Animation.AnimationAccessor _animation;

	/// <summary>Runtime animation graph access that follows this renderer's scene model across recreation.</summary>
	[Property, Group( "Animation" ), ShowIf( nameof( UseAnimGraph ), true ), InlineEditor( Label = false )]
	public Sandbox.Animation.AnimationAccessor Animation => _animation ??= new( this );

	bool _lowDetailAnimation;

	/// <summary>
	/// Skip animating any bone the skeleton marks as high LOD. Cheaper, for models that are
	/// far away or unimportant. Skipped bones stay in their bind pose.
	/// </summary>
	[Property, Group( "Animation" ), ShowIf( nameof( UseAnimGraph ), true )]
	public bool LowDetailAnimation
	{
		get => _lowDetailAnimation;
		set
		{
			if ( _lowDetailAnimation == value ) return;

			_lowDetailAnimation = value;

			if ( SceneModel.IsValid() )
			{
				SceneModel.LowDetailAnimation = value;
			}
		}
	}

	internal Sandbox.Animation.Graph GraphOverride => _graphDefinition;

	internal Sandbox.Animation.Graph GraphDefinition
	{
		get => _graphDefinition ?? Model?.AnimationGraph;
		set
		{
			if ( _graphDefinition == value ) return;

			_graphDefinition = value;

			if ( SceneModel.IsValid() )
			{
				SceneModel.Animation.Graph = value;
				if ( Scene.IsEditor )
					UpdateAnimationAfterParameterChange();
			}
		}
	}

	private void ApplyAnimationGraph( SceneModel sceneModel )
	{
		if ( _graphDefinition is not null )
			sceneModel.Animation.Graph = _graphDefinition;
	}

	private void OnAnimationControllerCreated()
	{
		_animation?.ApplyStoredParameters();
	}
}
