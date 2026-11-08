using Sandbox;

namespace MenuProject.UI;

internal interface IVoiceChatMember
{
	string DisplayName { get; }
	ulong Id { get; }
	float Amplitude { get; }
	RealTimeSince LastHeard { get; }

	/// <summary>
	/// Held through ordinary pauses, so rows don't come and go mid-sentence.
	/// </summary>
	public bool IsTalking => LastHeard <= 1.5f;
	public bool IsTimedOut => LastHeard > 60f;
}

partial class VoiceChatPanel
{
	[MenuConVar( "menu_mock_voice", Help = "Mock in-game voice chat" )]
	internal static bool Mock { get; set; }

	private IVoiceChatMember[] _mockMembers;

	private Dictionary<Connection, VoiceChatMember> _members { get; } = new();

	/// <summary>
	/// Everyone talking, in the order they started. Only the first few get a row.
	/// </summary>
	private List<Speaker> Speakers { get; } = new();

	/// <summary>
	/// How many people are talking without a row.
	/// </summary>
	private int Overflow => Speakers.Count - MaxSpeakers;

	private void TickMembers()
	{
		Speakers.RemoveAll( x => !x.Source.IsTalking );

		if ( Mock )
		{
			_mockMembers ??= [.. CreateMockMembers()];

			foreach ( var member in _mockMembers )
			{
				if ( member.IsTalking )
				{
					TryAddSpeaker( member );
				}
			}
		}
		else
		{
			foreach ( var pair in _members )
			{
				IVoiceChatMember member = pair.Value;

				if ( member.IsTalking )
				{
					TryAddSpeaker( member );
				}
				else if ( member.IsTimedOut )
				{
					_members.Remove( pair.Key );
				}
			}
		}
	}

	private void TryAddSpeaker( IVoiceChatMember member )
	{
		foreach ( var speaker in Speakers )
		{
			if ( speaker.Source == member ) return;
		}

		Speakers.Add( new Speaker( member ) );
	}

	private IEnumerable<IVoiceChatMember> CreateMockMembers()
	{
		return MenuUtility.Friends
			.Where( x => !x.IsMe )
			.OrderBy( x => x.Id )
			.Take( 16 )
			.Select( x => new MockVoiceChatMember( x ) );
	}

	public void PacketReceived( Connection connection, SoundHandle sound )
	{
		if ( !_members.TryGetValue( connection, out var member ) )
		{
			_members[connection] = member = new VoiceChatMember( connection );
		}

		member.PacketReceived( sound );
	}

	internal sealed class Speaker( IVoiceChatMember source )
	{
		private const double MinActivityUpdatePeriod = 0.001;
		private const double MaxActivityUpdatePeriod = 1.0;

		private const float AmplitudeScale = 8f;
		private const float AmplitudeDecayPeriod = 0.25f;

		private float _activity;
		private RealTimeSince _lastActivityUpdate = MaxActivityUpdatePeriod;

		public IVoiceChatMember Source => source;
		public string DisplayName => source.DisplayName;
		public ulong Id => source.Id;

		/// <summary>
		/// How much someone is talking, 0 to 1.
		/// </summary>
		public float Activity
		{
			get
			{
				UpdateActivity();
				return _activity;
			}
		}

		/// <summary>
		/// Update <see cref="Activity"/> to be a smoothed-out <see cref="IVoiceChatMember.Amplitude"/>.
		/// </summary>
		private void UpdateActivity()
		{
			var dt = (float)_lastActivityUpdate;

			if ( dt <= MinActivityUpdatePeriod ) return;

			_lastActivityUpdate = 0;

			// Reset activity if it's been a long time

			if ( dt >= MaxActivityUpdatePeriod )
			{
				_activity = 0f;
				return;
			}

			_activity = MathX.ExponentialDecay( _activity, source.Amplitude * AmplitudeScale, AmplitudeDecayPeriod, dt );
		}
	}
}

internal sealed class VoiceChatMember( Connection connection ) : IVoiceChatMember
{
	private SoundHandle _lastActiveSound;

	public string DisplayName => connection.DisplayName;
	public ulong Id => connection.SteamId;
	public float Amplitude => _lastActiveSound is { IsValid: true } sound ? sound.Amplitude : 0f;
	public RealTimeSince LastHeard { get; private set; }

	public void PacketReceived( SoundHandle sound )
	{
		_lastActiveSound = sound;
		LastHeard = 0;
	}
}

file sealed class MockVoiceChatMember( Friend friend ) : IVoiceChatMember
{
	private readonly float _seed = Random.Shared.Float( -128f, 128f );

	private RealTimeUntil _nextUpdate;
	private RealTimeSince _lastUpdate;

	private bool _isTalking;

	public string DisplayName => friend.Name;
	public ulong Id => friend.Id;

	public float Amplitude
	{
		get
		{
			Update();

			if ( !_isTalking ) return 0f;

			return (Sandbox.Utility.Noise.Perlin( RealTime.Now * 256f, _seed ) * 2f - 1f).Clamp( 0f, 1f );
		}
	}

	public RealTimeSince LastHeard
	{
		get
		{
			Update();
			return _isTalking ? 0 : _lastUpdate;
		}
	}

	private void Update()
	{
		if ( !_nextUpdate ) return;

		_isTalking = !_isTalking;

		_nextUpdate = Random.Shared.Float( 1f, _isTalking ? 4f : 10f );
		_lastUpdate = 0;
	}
}
