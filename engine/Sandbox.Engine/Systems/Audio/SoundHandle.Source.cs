using Sandbox.Audio;

namespace Sandbox;

partial class SoundHandle
{
	private DirectSoundModel _acousticModel;
	private Dictionary<Listener, DirectSoundModel> _acousticModels;
	private BinauralEffect _binauralEffect;
	private Dictionary<Listener, BinauralEffect> _binauralEffects;

	internal DirectSoundModel GetDirectSoundModel( Listener listener )
	{
		if ( _acousticModels?.TryGetValue( listener, out var source ) == true )
			return source;

		return _acousticModel;
	}

	internal BinauralEffect GetBinaural( Listener listener )
	{
		if ( _binauralEffects?.TryGetValue( listener, out var binaural ) == true )
			return binaural;

		return _binauralEffect;
	}

	private void UpdateAcousticModel( DirectSoundModel source )
	{
		source.ListenLocal = ListenLocal;
		source.AirAbsorption = AirAbsorption;
		source.Occlusion = OcclusionEnabled;
		source.DistanceAttenuation = DistanceAttenuation;
		source.ReverbAmount = Reverb;
		source.Distance = Distance;
		source.Falloff = Falloff;
		source.Update( Transform );
	}

	private void UpdateSources( IReadOnlyList<Listener> removedListeners )
	{
		if ( ListenLocal )
		{
			_acousticModel ??= new DirectSoundModel();
			UpdateAcousticModel( _acousticModel );
			_binauralEffect ??= new BinauralEffect();

			if ( _acousticModels is not null )
			{
				foreach ( var source in _acousticModels.Values )
					Audio.MixingThread.QueueAcousticModelDisposal( source );

				_acousticModels.Clear();
				_acousticModels = default;
			}

			if ( _binauralEffects is not null )
			{
				foreach ( var binaural in _binauralEffects.Values )
					Audio.MixingThread.QueueBinauralDisposal( binaural );

				_binauralEffects.Clear();
				_binauralEffects = default;
			}

			return;
		}

		if ( _acousticModel is not null )
		{
			Audio.MixingThread.QueueAcousticModelDisposal( _acousticModel );
			_acousticModel = null;
		}

		if ( _binauralEffect is not null )
		{
			Audio.MixingThread.QueueBinauralDisposal( _binauralEffect );
			_binauralEffect = null;
		}

		if ( _acousticModels is { Count: > 0 } && removedListeners.Count > 0 )
		{
			for ( var i = 0; i < removedListeners.Count; i++ )
			{
				var removed = removedListeners[i];
#pragma warning disable CA2000 // ownership transferred to disposal queue immediately
				if ( _acousticModels.Remove( removed, out var source ) )
					Audio.MixingThread.QueueAcousticModelDisposal( source );

				if ( _binauralEffects?.Remove( removed, out var binaural ) == true )
					Audio.MixingThread.QueueBinauralDisposal( binaural );
#pragma warning restore CA2000
			}
		}

		var scene = Scene;
		foreach ( var listener in Listener.ActiveList )
		{
			if ( listener.Scene != scene )
				continue;

			_acousticModels ??= new( ReferenceEqualityComparer.Instance );
			_binauralEffects ??= new( ReferenceEqualityComparer.Instance );

			if ( !_acousticModels.TryGetValue( listener, out var src ) )
			{
				src = new DirectSoundModel();
				_acousticModels[listener] = src;

				// Prime occlusion on creation so it doesn't start at full volume. Fires once per source.
				SoundSimulationSystem.Get( scene )?.PrimeNewModel( this, listener, src );
			}

			UpdateAcousticModel( src );

			if ( !_binauralEffects.ContainsKey( listener ) )
				_binauralEffects[listener] = new BinauralEffect();
		}
	}

	private void DisposeSources()
	{
		Audio.MixingThread.QueueAcousticModelDisposal( _acousticModel );
		_acousticModel = default;

		if ( _acousticModels is not null )
		{
			foreach ( var source in _acousticModels.Values )
				Audio.MixingThread.QueueAcousticModelDisposal( source );

			_acousticModels.Clear();
			_acousticModels = default;
		}

		Audio.MixingThread.QueueBinauralDisposal( _binauralEffect );
		_binauralEffect = default;

		if ( _binauralEffects is not null )
		{
			foreach ( var binaural in _binauralEffects.Values )
				Audio.MixingThread.QueueBinauralDisposal( binaural );

			_binauralEffects.Clear();
			_binauralEffects = default;
		}

		Audio.MixingThread.QueueReverbDisposal( _reverb );
		_reverb = null;
		SourceRoom = default;
	}
}
