namespace Sandbox;

partial class CollisionSoundSystem
{
	const float FadeRate = 10.0f;
	const float ScrapeMinPower = 75.0f;
	const float ScrapeStartVolume = 0.1f;
	const float SlipEpsilon = 1.0f;
	const float ScrapePowerScale = 1.0f / 15500.0f;
	const float SilenceThreshold = 1.0f / 128.0f;
	const int MaxContacts = 8;
	const int MaxScrapeVoices = 4;

	readonly record struct ScrapeKey( GameObject Owner, PhysicsBody Body );

	sealed class Voice
	{
		public SoundHandle Handle;
		public SoundEvent Sound;
		public float BaseVolume;
		public float Volume;
		public float TargetVolume;
		public Vector3 Position;
		public double NextStartTime;
		public bool PlaybackFailed;
		public SoundEvent PlayingSound;
		public PhysicsBody Body;
	}

	readonly Dictionary<ScrapeKey, Voice> _scrapeVoices = [];
	readonly List<ScrapeKey> _finished = [];
	double _lastScrapeStep = double.NegativeInfinity;

	void IScenePhysicsEvents.PostPhysicsStep()
	{
		if ( Application.IsHeadless || Time.Delta <= 0.0f )
			return;

		using var _ = PerformanceStats.Timings.Audio.Scope();
		_lastScrapeStep = Time.NowDouble;

		foreach ( var voice in _scrapeVoices.Values )
			voice.TargetVolume = 0.0f;

		var world = Scene?.HasPhysicsWorld == true ? Scene.PhysicsWorld : null;
		if ( world.IsValid() )
		{
			Span<PhysicsBody3d.ScrapeContact> contacts = stackalloc PhysicsBody3d.ScrapeContact[MaxContacts];
			foreach ( var body in world._world.RegisteredBodies )
			{
				if ( body is PhysicsBody3d body3d && body.EnableCollisionSounds && body.IsValid )
					Evaluate( body3d, contacts );
			}
		}
	}

	void UpdateFriction()
	{
		if ( Application.IsHeadless || Time.Delta <= 0.0f || _scrapeVoices.Count == 0 )
			return;

		using var _ = PerformanceStats.Timings.Audio.Scope();
		UpdateVoices( Time.Delta );
	}

	void Evaluate( PhysicsBody3d body3d, Span<PhysicsBody3d.ScrapeContact> contacts )
	{
		var count = body3d.GetScrapeContacts( contacts );
		if ( count == 0 )
			return;
		var body = body3d.Owner;
		GameObject owner = null;
		var bodyVelocity = Vector3.Zero;
		var rotating = false;
		var initialized = false;
		var scrapePower = 0.0f;
		var dominantPower = 0.0f;
		var scrapePos = Vector3.Zero;
		SoundEvent scrapeSound = null;

		for ( int i = 0; i < count; i++ )
		{
			ref readonly var contact = ref contacts[i];
			if ( contact.FrictionForce <= 0.0f )
				continue;
			var surface = contact.Surface;
			if ( surface is null || !surface.HasScrapeSounds )
				continue;

			if ( !initialized )
			{
				owner = BodySource( body );
				bodyVelocity = body.Velocity;
				rotating = body.AngularVelocity.LengthSquared > 1e-8f;
				initialized = true;
			}
			var other = contact.OtherBody;
			if ( other.IsValid() && (!other.EnableCollisionSounds || (owner is not null && BodySource( other ) == owner)) )
				continue;

			var otherVelocity = other.IsValid() ? other.GetVelocityAtPoint( contact.Point ) : Vector3.Zero;
			var bodyVelAtPoint = rotating ? body.GetVelocityAtPoint( contact.Point ) : bodyVelocity;
			var relative = bodyVelAtPoint - otherVelocity;
			var slipSquared = (relative - contact.Normal * Vector3.Dot( relative, contact.Normal )).LengthSquared;
			if ( slipSquared <= SlipEpsilon * SlipEpsilon )
				continue;

			var sound = surface.GetScrapeSound( contact.OtherSurfaceIndex );
			if ( sound is null )
				continue;
			var power = contact.FrictionForce * MathF.Sqrt( slipSquared );
			scrapePower += power;
			if ( power <= dominantPower )
				continue;
			dominantPower = power;
			scrapeSound = sound;
			scrapePos = contact.Point;
		}

		if ( scrapeSound is null )
			return;
		var mass = body.Mass;
		var powerPerMass = mass > 0.0f ? scrapePower / mass : scrapePower;
		if ( powerPerMass < ScrapeMinPower )
			return;
		var scaled = powerPerMass * ScrapePowerScale;
		SetTarget( new( owner, owner is null ? body : null ), body, scrapeSound, scrapePos, (scaled * scaled).Clamp( 0.0f, 1.0f ) );
	}

	static GameObject BodySource( PhysicsBody body ) =>
		(body.Component as Rigidbody)?.GameObjectSource ?? body.GameObject;

	void SetTarget( ScrapeKey key, PhysicsBody body, SoundEvent sound, Vector3 position, float volume )
	{
		if ( volume <= SilenceThreshold )
			return;

		if ( !_scrapeVoices.TryGetValue( key, out var voice ) )
		{
			if ( sound.Volume.Max * volume <= ScrapeStartVolume )
				return;
			_scrapeVoices[key] = voice = new Voice();
		}
		else if ( volume <= voice.TargetVolume )
			return;

		voice.Sound = sound;
		voice.Body = body;
		voice.TargetVolume = volume;
		voice.Position = position;
	}

	void UpdateVoices( float dt )
	{
		_finished.Clear();
		var playing = 0;
		foreach ( var voice in _scrapeVoices.Values )
		{
			if ( voice.Handle is { IsValid: true, Finished: false } )
				playing++;
		}
		foreach ( var (key, voice) in _scrapeVoices )
		{
			if ( (key.Owner is not null && !key.Owner.IsValid())
				|| !voice.Body.IsValid() || !voice.Body.EnableCollisionSounds || Time.NowDouble - _lastScrapeStep > 0.1
				|| !voice.Sound.IsValid() )
				voice.TargetVolume = 0.0f;
			if ( voice.PlayingSound != voice.Sound )
			{
				if ( voice.Handle is { IsValid: true, Finished: false } )
					playing--;
				voice.Handle?.Stop( 0.1f );
				voice.Handle = null;
				voice.PlayingSound = voice.Sound;
				voice.NextStartTime = 0.0;
				voice.PlaybackFailed = false;
			}
			voice.Volume = voice.Volume.Approach( voice.TargetVolume, FadeRate * dt );
			var starting = false;
			if ( playing < MaxScrapeVoices
				&& voice.Sound.Volume.Max * voice.TargetVolume > ScrapeStartVolume
				&& Time.NowDouble >= voice.NextStartTime
				&& voice.Handle is null or { IsStopped: true } or { Finished: true } )
			{
				voice.NextStartTime = Time.NowDouble + 1.0;
				voice.Handle = Sound.Play( voice.Sound, voice.Position );
				voice.BaseVolume = voice.Handle?.Volume ?? 0.0f;
				if ( voice.Handle is not { IsValid: true } )
				{
					if ( !voice.PlaybackFailed )
						Log.Warning( $"Couldn't play scrape sound {voice.Sound.ResourceName}" );
					voice.PlaybackFailed = true;
				}
				else if ( voice.BaseVolume * voice.TargetVolume <= ScrapeStartVolume )
				{
					voice.Handle.Stop();
					voice.Handle = null;
				}
				else
				{
					voice.PlaybackFailed = false;
					voice.NextStartTime = 0.0;
					starting = true;
					playing++;
				}
			}
			if ( voice.Handle is { IsValid: true } handle )
			{
				handle.Position = voice.Position;
				handle.Volume = voice.BaseVolume * voice.Volume;
				if ( !starting )
				{
					var pitch = voice.Sound.Pitch;
					var targetPitch = pitch.Min + (pitch.Max - pitch.Min) * voice.TargetVolume;
					handle.Pitch += (targetPitch - handle.Pitch) * (FadeRate * dt).Clamp( 0.0f, 1.0f );
				}
			}
			if ( voice.TargetVolume <= SilenceThreshold && voice.Volume <= SilenceThreshold )
			{
				if ( voice.Handle is { IsValid: true, Finished: false } )
					playing--;
				voice.Handle?.Stop( 0.1f );
				voice.Handle = null;
				_finished.Add( key );
			}
		}
		foreach ( var key in _finished )
			_scrapeVoices.Remove( key );
	}

	void DisposeFriction()
	{
		foreach ( var voice in _scrapeVoices.Values )
			voice.Handle?.Stop();
		_scrapeVoices.Clear();
		_finished.Clear();
	}
}
