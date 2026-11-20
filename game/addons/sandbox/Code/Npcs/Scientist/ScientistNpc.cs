using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Schedules;

namespace Sandbox.Npcs.Scientist;

public sealed class ScientistNpc : Npc, Component.IPressable
{
	/// <summary>
	/// Current fear level (0–1). Computed from peak fear and time since last hurt.
	/// </summary>
	public float AfraidLevel
	{
		get
		{
			if ( _peakFear <= 0f ) return 0f;
			if ( _timeSinceHurt <= FearGracePeriod ) return _peakFear;

			var decayTime = _timeSinceHurt - FearGracePeriod;
			return MathF.Max( _peakFear - decayTime * FearDecayRate, 0f );
		}
	}

	/// <summary>
	/// Seconds at full fear before decay begins.
	/// </summary>
	[Property, Group( "Balance" )]
	private float FearGracePeriod { get; set; } = 3f;

	/// <summary>
	/// Fear units lost per second after the grace period.
	/// </summary>
	[Property, Group( "Balance" )]
	private float FearDecayRate { get; set; } = 0.15f;

	/// <summary>
	/// Walking pace while pottering about. Scientists only run when they're scared, or to keep up
	/// with a player they're following.
	/// </summary>
	[Property, Group( "Balance" )]
	public float WalkSpeed { get; set; } = 85f;

	private float _peakFear;
	private GameObject _attacker;
	private TimeSince _timeSinceHurt;
	private bool _isFleeing;
	[Sync( SyncFlags.FromHost )]
	private TimeSince _timeSinceStruggling { get; set; }
	private GameObject _farewellTarget;

	// Voice lines by situation. Each SoundEvent picks a random clip. These replace the old
	// on-screen text -- the scientist speaks instead.
	[Property, Group( "Voice" )] public SoundEvent FollowVoice { get; set; }
	[Property, Group( "Voice" )] public SoundEvent StayVoice { get; set; }
	[Property, Group( "Voice" )] public SoundEvent ScaredVoice { get; set; }
	[Property, Group( "Voice" )] public SoundEvent IdleVoice { get; set; }
	[Property, Group( "Voice" )] public SoundEvent StuckVoice { get; set; }

	/// <summary>
	/// The player this scientist is following, if any. Press USE on the scientist to toggle.
	/// </summary>
	[Sync]
	public GameObject Leader { get; set; }

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		return Leader.IsValid()
			? new IPressable.Tooltip( "#npc.action.stop_following", "person_off", null )
			: new IPressable.Tooltip( "#npc.action.follow", "follow_the_signs", null );
	}

	bool IPressable.CanPress( IPressable.Event e ) => true;

	bool IPressable.Press( IPressable.Event e )
	{
		ToggleFollow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	private void ToggleFollow( GameObject presserObject )
	{
		if ( !presserObject.IsValid() )
			return;

		var player = presserObject.Root.GetComponent<Player>();
		if ( !player.IsValid() )
			return;

		if ( Leader == player.GameObject )
		{
			// Say goodbye to their face before wandering off - the farewell
			// schedule stands still, looks at them and delivers the line
			Leader = null;
			_farewellTarget = player.GameObject;
		}
		else
		{
			Leader = player.GameObject;
			_timeSinceStruggling = 0;
			SayVoice( FollowVoice, ["reaction", "acknowledgement"], int.MaxValue, player.GameObject );
		}

		// Re-think now so following starts/stops immediately.
		EndCurrentSchedule();
	}

	// Normal lines respect the personal cooldown. Max priority always speaks.
	// If lookAt is given, the scientist looks them in the eyes while talking.
	private void SayVoice( SoundEvent voice, TagSet tags = null,
		int priority = 1000, GameObject lookAt = null )
	{
		if ( voice is null ) return;
		if ( priority <= 1000 && !Speech.CanSpeak ) return;

		Speech.TrySay( voice, lookAt: lookAt, tags: tags, priority: priority );
	}

	// Defenceless -- flees anything dangerous, indifferent to everyone else.
	public override string Faction => Factions.Citizen;

	protected override void SetupRelationships() => Fears( Factions.Enemy, Factions.Monster );

	public override ScheduleBase GetSchedule()
	{
		var fear = AfraidLevel;

		// Fear fully decayed — clear state
		if ( fear <= 0f && _peakFear > 0f )
		{
			_peakFear = 0f;
			_attacker = null;
		}

		// Afraid — flee from the attacker
		if ( fear > 0f && _attacker.IsValid() )
		{
			if ( !_isFleeing )
			{
				_isFleeing = true;
				_attacker.GetComponent<Player>()?.PlayerData?.AddStat( "npc.scientist.scare" );
			}

			SayVoice( ScaredVoice, ["reaction", "fear"], 2000, _attacker );

			var flee = GetSchedule<ScientistFleeSchedule>();
			flee.Source = _attacker;
			flee.PanicLevel = fear;
			return flee;
		}

		// Flee from anything we're scared of on sight, before it even hurts us.
		var threat = Senses.GetNearestVisible( Disposition.Fearful );
		if ( threat.IsValid() )
		{
			SayVoice( ScaredVoice, ["reaction", "fear"], 2000, threat );

			var sightFlee = GetSchedule<ScientistFleeSchedule>();
			sightFlee.Source = threat;
			sightFlee.PanicLevel = 0.5f;
			return sightFlee;
		}

		// Not fleeing anything.
		_isFleeing = false;

		// A player is walking into us - they want to get past, step out of their way
		var pusher = Senses.GetPushingPlayer();
		if ( pusher.IsValid() )
		{
			var moveAside = GetSchedule<MoveAsideSchedule>();
			moveAside.Blocker = pusher;
			moveAside.Speed = WalkSpeed;
			return moveAside;
		}

		// Someone just dismissed us - stand, face them, say goodbye, then move on.
		// Fear above still preempts; no goodbyes mid panic.
		if ( _farewellTarget.IsValid() )
		{
			var farewell = GetSchedule<ScientistFarewellSchedule>();
			farewell.Target = _farewellTarget;
			farewell.Voice = StayVoice;
			_farewellTarget = null;
			return farewell;
		}

		// Following a player who recruited us with USE. (Fear above takes priority, so we
		// still cower from danger, then resume following once it passes.)
		if ( Leader.IsValid() )
		{
			// Keep track of whether we're keeping up; if we fall too far behind for too long,
			// give up and stay put.
			if ( WorldPosition.Distance( Leader.WorldPosition ) < 350f )
				_timeSinceStruggling = 0;

			if ( _timeSinceStruggling > 6f )
			{
				var leader = Leader;
				Leader = null;
				SayVoice( StuckVoice, ["reaction", "stuck"], 2000, leader );
				return GetIdleSchedule();
			}

			var follow = GetSchedule<FollowSchedule>();
			follow.Target = Leader;
			return follow;
		}

		return GetIdleSchedule();
	}

	/// <summary>
	/// Pick a random idle behavior so the scientist doesn't just stand around.
	/// </summary>
	private ScheduleBase GetIdleSchedule()
	{
		// Occasionally mutter something while pottering about.
		SayVoice( IdleVoice );

		var roll = Game.Random.Float();

		if ( roll < 0.35f )
		{
			var wander = GetSchedule<ScientistWanderSchedule>();
			wander.Speed = WalkSpeed;
			return wander;
		}

		if ( roll < 0.60f )
		{
			var prop = FindNearbyProp();
			if ( prop.IsValid() )
			{
				var inspect = GetSchedule<ScientistInspectPropSchedule>();
				inspect.PropTarget = prop;
				inspect.Speed = WalkSpeed;
				return inspect;
			}
		}

		return GetSchedule<ScientistIdleSchedule>();
	}

	/// <summary>
	/// Find the nearest prop within range to inspect.
	/// </summary>
	private GameObject FindNearbyProp()
	{
		// TODO: I feel like the senses layer should be able to hand all of this in a cost effective way.

		var nearby = Scene.FindInPhysics( new Sphere( WorldPosition, 2048 ) );

		GameObject best = null;
		float bestDist = float.MaxValue;

		foreach ( var obj in nearby )
		{
			if ( obj == GameObject ) continue;
			if ( obj.GetComponent<Prop>() is null ) continue;

			var dist = WorldPosition.Distance( obj.WorldPosition );
			if ( dist < bestDist )
			{
				bestDist = dist;
				best = obj;
			}
		}

		return best;
	}

	protected override void OnHurt( in DamageInfo damage )
	{
		// Escalate fear — each hit stacks, clamped to 1.
		_peakFear = MathF.Min( _peakFear + damage.Damage / 50f, 1f );
		_attacker = GetAttacker( damage );
		_timeSinceHurt = 0;

		// React immediately so flee picks up.
		EndCurrentSchedule();
	}

	protected override void Die( in DamageInfo damage )
	{
		GetAttacker( damage )?.GetComponent<Player>()?.PlayerData?.AddStat( "npc.scientist.kill" );
		base.Die( damage );
	}
}
