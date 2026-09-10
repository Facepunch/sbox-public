using Sandbox.Audio;

namespace Sandbox;

/// <summary>
/// Apply DSP to mixer when listener is inside a DspVolume
/// </summary>
[Expose]
sealed class DspVolumeGameSystem : GameObjectSystem<DspVolumeGameSystem>
{
	public DspVolumeGameSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 0, Update, "Dsp Update" );
	}

	public override void Dispose()
	{
		base.Dispose();

		foreach ( var entry in _entries.Values )
		{
			entry.mixerHandle.Get()?.RemoveProcessor( entry.processor );
		}
	}

	record class Entry( DspProcessor processor, MixerHandle mixerHandle );
	Dictionary<(string effect, MixerHandle mixer), Entry> _entries = new();

	internal static bool IsActive { get; private set; }

	void Update()
	{
		using var _ = PerformanceStats.Timings.Audio.Scope();

		if ( Scene.IsEditor )
			return;

		int lastPriority = int.MinValue;
		string found = default;
		MixerHandle foundMixer = default;

		foreach ( var volume in Scene.Volumes.FindAll<DspVolume>( Sound.Listener.Position ) )
		{
			int priority = volume.Priority;

			if ( priority < lastPriority )
				continue;

			lastPriority = priority;
			found = volume.Dsp.Name;
			foundMixer = volume.TargetMixer;
		}

		IsActive = !string.IsNullOrWhiteSpace( found );

		var activeKey = (found, foundMixer);

		if ( !string.IsNullOrWhiteSpace( found ) && !_entries.ContainsKey( activeKey ) )
		{
			var mixer = foundMixer.Get() ?? Mixer.FindMixerByName( "Game" );
			if ( mixer is not null )
			{
				var processor = new DspProcessor();

				processor.Effect = found;
				processor.Mix = 0;

				mixer.AddProcessor( processor );
				_entries[activeKey] = new Entry( processor, foundMixer );
			}
		}

		foreach ( var entry in _entries )
		{
			var mixTarget = entry.Key == activeKey ? 1 : 0;

			entry.Value.processor.Mix = entry.Value.processor.Mix.Approach( mixTarget, Time.Delta );
		}

		foreach ( var entry in _entries.Where( x => x.Value.processor.Mix <= 0 ).ToArray() )
		{
			entry.Value.mixerHandle.Get()?.RemoveProcessor( entry.Value.processor );
			_entries.Remove( entry.Key );
		}
	}
}
