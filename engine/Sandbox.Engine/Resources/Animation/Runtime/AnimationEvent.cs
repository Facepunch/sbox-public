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

	internal const int MaxTypeNameLength = 1024;
	internal const int MaxJsonBytes = 64 * 1024;

	internal static TypeDescription ResolveType( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) || name.Length > MaxTypeNameLength ) return null;
		// Only cache the registered types. Asset-provided names can be arbitrary.
		var types = Game.TypeLibrary.GetTypes<AnimationEvent>();
		TypeDescription alias = null;
		for ( int i = 0; i < types.Count; i++ )
		{
			var type = types[i];
			if ( type.FullName == name ) return type;
			if ( (alias is null || alias.IsDynamicAssembly) && type.IsNamed( name ) ) alias = type;
		}
		return alias;
	}

	internal static T Deserialize<T>( string typeName, string data ) where T : AnimationEvent
	{
		if ( string.IsNullOrEmpty( typeName ) )
			throw new InvalidOperationException( "Animation event has no type." );

		var eventType = ResolveType( typeName );
		if ( eventType is null || eventType.IsAbstract )
			throw new InvalidOperationException( "Unregistered animation event type." );

		if ( !eventType.TargetType.IsAssignableTo( typeof( T ) ) )
			return null;

		if ( data is null || data.Length > MaxJsonBytes || System.Text.Encoding.UTF8.GetByteCount( data ) > MaxJsonBytes )
			throw new InvalidOperationException( "Animation event payload is missing or too large." );

		return (T)Json.Deserialize( data, eventType.TargetType )
			?? throw new InvalidOperationException( "Animation event has no data." );
	}
}
