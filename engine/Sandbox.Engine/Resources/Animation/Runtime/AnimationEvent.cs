using System.Text.Json.Serialization;

namespace Sandbox.Animation;

[Expose]
public abstract class AnimationEvent
{
	/// <summary>
	/// The renderer that fired this event. Null when reading authored events from a clip.
	/// </summary>
	[JsonIgnore]
	public SkinnedModelRenderer Renderer { get; internal set; }

	internal void DispatchTo( Component.IAnimationEvent handler )
	{
		if ( !Renderer.IsValid() || !Renderer.Active )
			return;

		try
		{
			handler.OnAnimationEvent( this );
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Animation event handler failed" );
		}
	}

	internal static T Deserialize<T>( string typeName, string data ) where T : AnimationEvent
	{
		if ( string.IsNullOrEmpty( typeName ) )
			throw new InvalidOperationException( "Animation event has no type." );

		var eventType = Game.TypeLibrary.GetType<AnimationEvent>( typeName );
		if ( eventType is null || eventType.IsAbstract )
			throw new InvalidOperationException( $"Animation event type '{typeName}' is unavailable or is not an AnimationEvent." );

		if ( !eventType.TargetType.IsAssignableTo( typeof( T ) ) )
			return null;

		return (T)Json.Deserialize( data, eventType.TargetType )
			?? throw new InvalidOperationException( "Animation event has no data." );
	}
}
