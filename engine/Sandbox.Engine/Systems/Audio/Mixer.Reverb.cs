namespace Sandbox.Audio;

public partial class Mixer
{
	/// <summary>
	/// One room's reverb for one listener. Reverb is linear, so every voice in a room adds its send here instead of running a
	/// reverb of its own, and the tail keeps ringing after the voices that fed it have stopped.
	/// </summary>
	sealed class SharedReverb
	{
		public Listener Listener;
		public int KeyLow, KeyMid, KeyHigh;
		public float Low, Mid, High;
		public float TailSeconds;
		public bool Fed;
		public readonly NativeReverbEffect Effect = new();
		public readonly MultiChannelBuffer Send = new( 1 );
		public readonly MultiChannelBuffer Output = new( 1 );

		public void Feed( MultiChannelBuffer send, float level )
		{
			Send.Get( 0 ).MixFrom( send.Get( 0 ), level );
			Fed = true;
		}

		public void Dispose()
		{
			Effect.Dispose();
			Send.Dispose();
			Output.Dispose();
		}
	}

	// Decay times within this ratio share a reverb; a 10% difference in decay isn't audible.
	const float ReverbStep = 1.1f;
	const int MaxReverbs = 16;

	readonly List<SharedReverb> _reverbs = new();
	readonly List<SharedReverb> _idleReverbs = new();

	static int ReverbKey( float decay ) => (int)MathF.Round( MathF.Log( MathF.Max( decay, 0.05f ) ) / MathF.Log( ReverbStep ) );

	/// <summary>
	/// The reverb voices in <paramref name="room"/> heard by <paramref name="listener"/> feed this buffer.
	/// </summary>
	SharedReverb GetReverb( Listener listener, in ReverbSnapshot room )
	{
		int low = ReverbKey( room.DecayTimeLow ), mid = ReverbKey( room.DecayTimeMid ), high = ReverbKey( room.DecayTimeHigh );

		SharedReverb nearest = null;
		int nearestDist = int.MaxValue;
		foreach ( var r in _reverbs )
		{
			if ( r.Listener != listener ) continue;
			int dist = Math.Abs( r.KeyLow - low ) + Math.Abs( r.KeyMid - mid ) + Math.Abs( r.KeyHigh - high );
			if ( dist == 0 ) return r;
			if ( dist < nearestDist ) { nearest = r; nearestDist = dist; }
		}

		// Out of reverbs: share the closest one this listener already has.
		if ( _reverbs.Count >= MaxReverbs ) return nearest;

		SharedReverb reverb;
		if ( _idleReverbs.Count > 0 )
		{
			reverb = _idleReverbs[^1];
			_idleReverbs.RemoveAt( _idleReverbs.Count - 1 );
		}
		else
		{
			reverb = new SharedReverb();
		}

		reverb.Listener = listener;
		reverb.KeyLow = low; reverb.KeyMid = mid; reverb.KeyHigh = high;
		reverb.Low = MathF.Pow( ReverbStep, low );
		reverb.Mid = MathF.Pow( ReverbStep, mid );
		reverb.High = MathF.Pow( ReverbStep, high );
		reverb.Send.Silence();
		reverb.Fed = false;
		_reverbs.Add( reverb );
		return reverb;
	}

	void StartReverbs( VoiceFrameSnapshot snapshot )
	{
		for ( int i = _reverbs.Count - 1; i >= 0; i-- )
		{
			var r = _reverbs[i];
			if ( snapshot.RemovedListeners.Contains( r.Listener ) )
			{
				// Still ringing, so it can't be handed to another room: its tail would come with it.
				_reverbs.RemoveAt( i );
				r.Dispose();
				continue;
			}

			r.Send.Silence();
			r.Fed = false;
		}
	}

	/// <summary>
	/// Run every live reverb into its listener's buffer, before the processors. Unfed ones ring out on silence, then retire.
	/// </summary>
	void MixReverbs()
	{
		float bufferSeconds = AudioEngine.MixBufferSize * AudioEngine.SecondsPerSample;

		for ( int i = _reverbs.Count - 1; i >= 0; i-- )
		{
			var r = _reverbs[i];
			if ( r.Fed ) r.TailSeconds = MathF.Max( r.Low, MathF.Max( r.Mid, r.High ) );
			else if ( (r.TailSeconds -= bufferSeconds) <= 0f )
			{
				RetireReverb( i );
				continue;
			}

			// The send is the same in every channel, so is the reverb: run it once and spread it.
			r.Output.Silence();
			r.Effect.Apply( r.Low, r.Mid, r.High, r.Send, r.Output );

			var output = r.Output.Get( 0 );
			var target = GetListenerBuffer( r.Listener );
			for ( var ch = 0; ch < target.ChannelCount; ch++ )
				target.Get( ch ).MixFrom( output, 1f );
		}
	}

	/// <summary>
	/// Drop every reverb, ringing or not. Ringing ones can't be reused, their tail would come along.
	/// </summary>
	void StopReverbs()
	{
		foreach ( var r in _reverbs ) r.Dispose();
		_reverbs.Clear();
	}

	void RetireReverb( int index )
	{
		var r = _reverbs[index];
		_reverbs.RemoveAt( index );
		r.Listener = null;
		_idleReverbs.Add( r );
	}
}
