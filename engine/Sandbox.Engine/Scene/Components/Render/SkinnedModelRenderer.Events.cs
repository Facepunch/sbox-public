using Sandbox.Animation;

namespace Sandbox;

public sealed partial class SkinnedModelRenderer
{
	internal void DispatchAnimationEvent( AnimationEvent data )
	{
		if ( !this.IsValid() || !Active )
			return;

		data.Renderer = this;
		GameObject.RunEvent<IAnimationEvent>( data.DispatchTo, FindMode.EnabledInSelf | FindMode.InAncestors );
	}

	/// <summary>
	/// Called when a footstep event happens
	/// </summary>
	public Action<SceneModel.FootstepEvent> OnFootstepEvent { get; set; }

	private void InternalOnFootstep( SceneModel.FootstepEvent e )
	{
		OnFootstepEvent?.Invoke( e );
	}

	/// <summary>
	/// Called when a generic animation event happens
	/// </summary>
	public Action<SceneModel.GenericEvent> OnGenericEvent { get; set; }

	private void InternalOnGenericEvent( SceneModel.GenericEvent ev )
	{
		if ( OnGenericEvent is not null )
		{
			OnGenericEvent.Invoke( ev );
		}
	}

	/// <summary>
	/// Called when a sound event happens
	/// </summary>
	public Action<SceneModel.SoundEvent> OnSoundEvent { get; set; }

	private void InternalOnSoundEvent( SceneModel.SoundEvent ev )
	{
		if ( OnSoundEvent is not null )
		{
			OnSoundEvent.Invoke( ev );
		}

		// todo - option to disable sounds

		// todo - this sound handle should follow the entity
		// scene sound manager system?
		Sound.Play( ev.Name, ev.Position );
	}

	/// <summary>
	/// Called when an anim tag event happens
	/// </summary>
	public Action<SceneModel.AnimTagEvent> OnAnimTagEvent { get; set; }

	private void InternalOnAnimTagEvent( SceneModel.AnimTagEvent ev )
	{
		if ( OnAnimTagEvent is not null )
		{
			OnAnimTagEvent.Invoke( ev );
		}
	}
}
