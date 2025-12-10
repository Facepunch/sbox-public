using Sandbox.Audio;

namespace Sandbox;

/// <summary>
/// Attach audio effect processors to a sound component on the same GameObject.
/// The processors will be applied to the sound before mixing.
/// </summary>
[Expose]
[Category( "Audio" )]
[Title( "Sound Effect" )]
[Icon( "equalizer" )]
[Tint( EditorTint.Green )]
public sealed class SoundEffectComponent : Component
{
	/// <summary>
	/// The audio processors to apply to the sound.
	/// Processors are applied in order.
	/// </summary>
	[Property]
	public List<AudioEventProcessor> Processors { get; set; } = new();

	/// <summary>
	/// The sound component this effect is attached to.
	/// If not set, will automatically find a BaseSoundComponent on the same GameObject.
	/// </summary>
	[Property]
	public BaseSoundComponent TargetSound { get; set; }

	private SoundHandle _attachedHandle;

	protected override void OnEnabled()
	{
		TryAttachProcessors();
	}

	protected override void OnDisabled()
	{
		DetachProcessors();
	}

	protected override void OnUpdate()
	{
		// Check if the sound handle has changed (new sound started)
		var soundComponent = TargetSound ?? Components.Get<BaseSoundComponent>();
		if ( soundComponent is null )
			return;

		var currentHandle = soundComponent.SoundHandleInternal;

		// If the handle changed, reattach the processors
		if ( currentHandle != _attachedHandle )
		{
			DetachProcessors();
			TryAttachProcessors();
		}
	}

	private void TryAttachProcessors()
	{
		if ( Processors is null || Processors.Count == 0 )
			return;

		var soundComponent = TargetSound ?? Components.Get<BaseSoundComponent>();
		if ( soundComponent is null )
			return;

		var handle = soundComponent.SoundHandleInternal;
		if ( handle is null || !handle.IsValid )
			return;

		foreach ( var processor in Processors )
		{
			if ( processor is not null )
			{
				handle.AddProcessor( processor );
			}
		}

		_attachedHandle = handle;
	}

	private void DetachProcessors()
	{
		if ( _attachedHandle is not null && _attachedHandle.IsValid && Processors is not null )
		{
			foreach ( var processor in Processors )
			{
				if ( processor is not null )
				{
					_attachedHandle.RemoveProcessor( processor );
				}
			}
		}

		_attachedHandle = null;
	}
}
