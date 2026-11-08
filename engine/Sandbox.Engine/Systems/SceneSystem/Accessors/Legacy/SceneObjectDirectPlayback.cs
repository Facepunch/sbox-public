
using Sandbox;

#pragma warning disable CS0618

/// <summary>
/// Hidden legacy graph accessor. addon code should only ever access DirectPlayback.
/// </summary>
internal sealed class SceneObjectDirectPlayback : AnimGraphDirectPlayback
{
	readonly SceneModel Target;

	public SceneObjectDirectPlayback( SceneModel sceneObject )
	{
		Target = sceneObject;
	}

	public override float StartTime { set => Target.animNative.DirectPlayback_SetSequenceStartTime( value ); }

	public override float TimeNormalized => Target.animNative.DirectPlayback_GetSequenceCycle();

	public override float Duration => Target.animNative.DirectPlayback_GetSequenceDuration();

	public override float Time => TimeNormalized * Duration;

	public override string Name => Target.animNative.DirectPlayback_GetSequence();

	[Obsolete]
	public override int AnimationCount => Target.Model != null ? Target.Model.AnimationCount : 0;

	[Obsolete]
	public override IEnumerable<string> Animations => Target.Model is null ? Enumerable.Empty<string>() : Target.Model.AnimationNames;

	public override IReadOnlyList<string> Sequences => Target.Model is null ? Array.Empty<string>() : Target.Model.SequenceNames;

	public override void Play( string name )
	{
		Target.animNative.DirectPlayback_PlaySequence( name );
	}

	public override void Play( string name, Vector3 target, float heading, float interpTime )
	{
		Target.animNative.DirectPlayback_PlaySequence( name, target, heading, interpTime );
	}

	public override void Cancel()
	{
		Target.animNative.DirectPlayback_CancelSequence();
	}
}
