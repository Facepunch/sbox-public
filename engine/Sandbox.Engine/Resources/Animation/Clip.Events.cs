namespace Sandbox.Animation;

public sealed partial class Clip
{
	readonly record struct AuthoredEvent( string Type, string Data, float StartTime, float Duration );

	AuthoredEvent[] _events;

	/// <summary>
	/// Read all game-defined events and their authored timing, without playing the clip.
	/// Events are returned in start-time order, with a fresh payload for each enumeration.
	/// Native engine events are not included.
	/// </summary>
	public IEnumerable<ClipEvent<AnimationEvent>> GetEvents() => GetEvents<AnimationEvent>();

	/// <summary>
	/// Read game-defined events assignable to <typeparamref name="T"/> and their authored timing,
	/// without playing the clip. Events are returned in start-time order, with a fresh payload
	/// for each enumeration. Times are in seconds at the clip's original playback speed.
	/// </summary>
	/// <exception cref="InvalidOperationException">An authored event type is unavailable or its payload is missing.</exception>
	/// <exception cref="System.Text.Json.JsonException">An authored event payload contains invalid JSON.</exception>
	public IEnumerable<ClipEvent<T>> GetEvents<T>() where T : AnimationEvent
	{
		foreach ( var entry in _events ??= ReadEvents() )
		{
			if ( AnimationEvent.Deserialize<T>( entry.Type, entry.Data ) is { } data )
				yield return new ClipEvent<T>( data, entry.StartTime, entry.Duration );
		}
	}

	AuthoredEvent[] ReadEvents()
	{
		ThreadSafe.AssertIsMainThread();
		var events = new List<AuthoredEvent>();
		var count = native.GetNumEvents();

		for ( var i = 0; i < count; i++ )
		{
			if ( native.GetEventName( i ) != "AE_MANAGED_EVENT" )
				continue;

			var payload = native.GetEventData( i, out var startTime, out var duration );
			if ( payload.IsNull )
				throw new InvalidOperationException( $"Animation event in '{Name}' has no payload." );

			events.Add( new AuthoredEvent( payload.GetMemberString( "Type" ), payload.GetMemberString( "Data" ), startTime, duration ) );
		}

		return events.OrderBy( x => x.StartTime ).ToArray();
	}
}
