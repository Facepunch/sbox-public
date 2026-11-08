using Sandbox.Audio;

namespace Sandbox;

/// <summary>
/// Apply DSP to the global mixer and optional listener-specific groups when their listeners enter volumes.
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
			entry.Mixer.RemoveProcessor( entry.Processor );
		_entries.Clear();
		_listenerVolumes.Clear();
		_active.Clear();
		_finished.Clear();
	}

	readonly record struct Key( string Effect, Mixer Mixer, Listener Listener );
	record class Entry( DspProcessor Processor, Mixer Mixer );
	readonly Dictionary<Key, Entry> _entries = [];
	readonly Dictionary<Listener, DspVolume> _listenerVolumes = [];
	readonly HashSet<Key> _active = [];
	readonly List<Key> _finished = [];

	internal static bool IsActive { get; private set; }

	void Update()
	{
		using var _ = PerformanceStats.Timings.Audio.Scope();

		if ( Scene.IsEditor )
			return;

		DspVolume global = null;
		_listenerVolumes.Clear();
		var position = Sound.Listener.Position;
		foreach ( var volume in Scene.Query<DspVolume>() )
		{
			var listener = volume.TargetListener;
			if ( listener is not null && !listener.IsValid() )
				continue;
			if ( !((Volumes.VolumeSystem.IVolume)volume).Test( listener?.Position ?? position ) )
				continue;

			if ( listener is null )
			{
				if ( !global.IsValid() || volume.Priority >= global.Priority )
					global = volume;
			}
			else if ( !_listenerVolumes.TryGetValue( listener, out var previous ) || volume.Priority >= previous.Priority )
			{
				_listenerVolumes[listener] = volume;
			}
		}

		_active.Clear();
		IsActive = global.IsValid() && !string.IsNullOrWhiteSpace( global.Dsp.Name );
		Activate( global );
		foreach ( var volume in _listenerVolumes.Values )
			Activate( volume );
		_listenerVolumes.Clear();

		_finished.Clear();
		foreach ( var entry in _entries )
		{
			var target = _active.Contains( entry.Key ) ? 1 : 0;
			entry.Value.Processor.Mix = entry.Value.Processor.Mix.Approach( target, Time.Delta );
			if ( entry.Value.Processor.Mix <= 0 )
				_finished.Add( entry.Key );
		}
		foreach ( var key in _finished )
		{
			_entries[key].Mixer.RemoveProcessor( _entries[key].Processor );
			_entries.Remove( key );
		}
		_finished.Clear();
		_active.Clear();
	}

	/// <summary>
	/// Maintain one effect per resolved mixer and listener. Targeted groups add to the existing global group.
	/// Retain the actual mixer so fading or disposal removes the processor from its original owner.
	/// </summary>
	void Activate( DspVolume volume )
	{
		if ( !volume.IsValid() || string.IsNullOrWhiteSpace( volume.Dsp.Name ) )
			return;

		var mixer = volume.TargetMixer.Get() ?? Mixer.FindMixerByName( "Game" );
		if ( mixer is null )
			return;

		var key = new Key( volume.Dsp.Name, mixer, volume.TargetListener );
		_active.Add( key );
		if ( _entries.ContainsKey( key ) )
			return;

		var processor = new DspProcessor
		{
			Effect = volume.Dsp,
			Mix = 0,
			TargetListener = volume.TargetListener
		};
		mixer.AddProcessor( processor );
		_entries[key] = new Entry( processor, mixer );
	}
}
