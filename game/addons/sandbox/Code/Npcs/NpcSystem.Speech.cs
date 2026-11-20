namespace Sandbox.Npcs;

/// <summary>NPC speech coordination.</summary>
public sealed partial class NpcSystem
{
	private sealed class SpeechReservation
	{
		public Npc Speaker { get; init; }
		public TagSet Tags { get; init; }
		public int Priority { get; init; }
		public float Radius { get; init; }
		public float EndTime { get; init; }
	}

	private sealed record SpeechCooldown( Vector3 Position, string Tag, int Priority, float Radius, float Expiry );

	private readonly List<SpeechReservation> _activeSpeech = [];
	private readonly List<SpeechCooldown> _speechCooldowns = [];

	/// <summary>
	/// Attempts to reserve speech around an NPC. Higher priorities preempt lower priorities.
	/// The reservation expires after <paramref name="duration"/> unless stopped early.
	/// </summary>
	/// <param name="speaker">NPC requesting the reservation.</param>
	/// <param name="duration">Length of the selected speech in seconds.</param>
	/// <param name="tags">Optional tags matched against tag cooldowns.</param>
	/// <param name="priority">Priority used for blocking and preemption. Defaults to 1000; <see cref="int.MaxValue"/> always speaks.</param>
	/// <param name="radius">Coordination radius for this line. Defaults to 1500.</param>
	public bool TryBeginSpeech( Npc speaker, float duration, TagSet tags = null, int priority = 1000, float radius = 1500f )
	{
		if ( !Networking.IsHost || !speaker.IsValid() || duration <= 0f )
			return false;

		radius = MathF.Max( radius, 0f );
		PruneSpeechState();
		EndExpiredSpeech();

		var position = speaker.WorldPosition;
		var conflicts = _activeSpeech.Where( x => x.Speaker != speaker
			&& IsWithinSpeechRadius( position, radius, x.Speaker.WorldPosition, x.Radius ) ).ToArray();

		var ownReservation = _activeSpeech.FirstOrDefault( x => x.Speaker == speaker );
		if ( ownReservation is not null )
			conflicts = [.. conflicts, ownReservation];

		if ( priority < int.MaxValue && conflicts.Any( x => x.Priority >= priority ) )
			return false;

		if ( priority < int.MaxValue && _speechCooldowns.Any( x => x.Priority >= priority
			&& (x.Tag is null || (tags is not null && tags.Has( x.Tag )))
			&& IsWithinSpeechRadius( position, radius, x.Position, x.Radius ) ) )
			return false;

		foreach ( var conflict in conflicts )
		{
			conflict.Speaker.Speech.Stop();
		}

		_activeSpeech.Add( new SpeechReservation
		{
			Speaker = speaker,
			Tags = tags,
			Priority = priority,
			Radius = radius,
			EndTime = Time.Now + duration
		} );

		return true;
	}

	/// <summary>Releases all speech reservations owned by an NPC before their normal expiry.</summary>
	/// <param name="speaker">NPC whose reservations should be released.</param>
	public void StopSpeech( Npc speaker )
	{
		_activeSpeech.RemoveAll( x => x.Speaker == speaker );
	}

	/// <summary>Adds a timed tag cooldown around a position.</summary>
	/// <param name="position">Center of the cooldown area.</param>
	/// <param name="tag">Speech tag blocked by the cooldown.</param>
	/// <param name="duration">Cooldown duration in seconds.</param>
	/// <param name="radius">Cooldown radius. Defaults to 1500.</param>
	/// <param name="priority">Requests above this priority bypass the cooldown.</param>
	public void SetTagCooldown( Vector3 position, string tag, float duration, float radius = 1500f, int priority = 1000 )
	{
		if ( !Networking.IsHost || string.IsNullOrWhiteSpace( tag ) || duration <= 0f )
			return;

		_speechCooldowns.Add( new SpeechCooldown( position, tag, priority, MathF.Max( radius, 0f ), Time.Now + duration ) );
	}

	/// <summary>Returns whether two speech areas overlap, using the larger radius.</summary>
	/// <param name="firstPosition">Center of the first speech area.</param>
	/// <param name="firstRadius">Radius of the first speech area.</param>
	/// <param name="secondPosition">Center of the second speech area.</param>
	/// <param name="secondRadius">Radius of the second speech area.</param>
	public static bool IsWithinSpeechRadius( Vector3 firstPosition, float firstRadius, Vector3 secondPosition, float secondRadius )
	{
		var radius = MathF.Max( MathF.Max( firstRadius, secondRadius ), 0f );
		return firstPosition.DistanceSquared( secondPosition ) <= radius * radius;
	}

	private void UpdateSpeech()
	{
		PruneSpeechState();
		EndExpiredSpeech();
	}

	private void EndExpiredSpeech()
	{
		foreach ( var reservation in _activeSpeech.ToArray() )
		{
			if ( Time.Now >= reservation.EndTime )
				EndSpeech( reservation );
		}
	}

	private void EndSpeech( SpeechReservation reservation )
	{
		_activeSpeech.Remove( reservation );

		if ( !reservation.Speaker.IsValid() )
			return;

		_speechCooldowns.Add( new SpeechCooldown(
			reservation.Speaker.WorldPosition,
			null,
			reservation.Priority,
			reservation.Radius,
			Time.Now + Game.Random.Float( 2.8f, 3.2f )
		) );
	}

	private void PruneSpeechState()
	{
		_activeSpeech.RemoveAll( x => !x.Speaker.IsValid() );
		_speechCooldowns.RemoveAll( x => Time.Now >= x.Expiry );
		_npcs.RemoveWhere( x => !x.IsValid() );
	}
}
