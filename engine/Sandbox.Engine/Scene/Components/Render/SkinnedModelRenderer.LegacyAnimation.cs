namespace Sandbox;

#pragma warning disable CS0618

public sealed partial class SkinnedModelRenderer
{
	private AnimationGraph _legacyAnimationGraph;

	/// <summary>Legacy graph override, retained for existing games and serialized scenes.</summary>
	[Property, Hide]
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Graph instead." )]
	public AnimationGraph AnimationGraph
	{
		get => _legacyAnimationGraph;
		set
		{
			if ( _legacyAnimationGraph == value ) return;
			_legacyAnimationGraph = value;

			if ( SceneModel.IsValid() )
				SceneModel.AnimationGraph = value;
		}
	}

	private void ApplyLegacyAnimationGraph( SceneModel sceneModel )
	{
		if ( AnimationGraph is not null )
		{
			sceneModel.AnimationGraph = AnimationGraph;
		}
	}
}
