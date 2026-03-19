using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox.MovieMaker;

#nullable enable

// When MovieProperties get serialized, we write track mappings to GameObject or Component references.
// Here we store those mappings, and handle creating ITarget instances that access them.

[JsonConverter( typeof( BinderConverter ) )]
partial class TrackBinder : IJsonPopulator
{
	/// <summary>
	/// Map track IDs to <see cref="GameObject"/>s and <see cref="Component"/>s.
	/// </summary>
	private readonly Dictionary<Guid, IValid?> _trackIdToTarget = new();

	/// <summary>
	/// Map <see cref="GameObject"/>s and <see cref="Component"/>s to the track IDs they are bound to.
	/// </summary>
	private readonly Dictionary<IValid, HashSet<Guid>> _targetToTrackId = new();

	/// <summary>
	/// Finds track IDs currently explicitly bound to the given <paramref name="gameObjectOrComponent"/>.
	/// </summary>
	public IEnumerable<Guid> GetTrackIds( IValid gameObjectOrComponent ) => _targetToTrackId
		.GetValueOrDefault( gameObjectOrComponent ) ?? [];

	public Guid? GetTrackId( IValid gameObjectOrComponent ) => _targetToTrackId
		.GetValueOrDefault( gameObjectOrComponent )?
		.FirstOrDefault();

	/// <summary>
	/// Returns true if there's an existing mapping for the given <paramref name="trackId"/>,
	/// and outputs that mapping as <paramref name="target"/>. Note that <see langword="null"/>
	/// is a valid binding, to force a track to map to nothing.
	/// </summary>
	public bool TryGetBinding( Guid trackId, out IValid? target ) =>
		_trackIdToTarget.TryGetValue( trackId, out target );

	/// <inheritdoc cref="TryGetBinding"/>
	public bool TryGetBinding<T>( Guid trackId, out T? target ) where T : class, IValid
	{
		if ( !TryGetBinding( trackId, out var obj ) )
		{
			target = null;
			return false;
		}

		if ( obj is { IsValid: false } )
		{
			Unbind( trackId );
			target = null;
			return false;
		}

		target = obj as T;
		return true;
	}

	private bool CanAutoBind( Guid trackId, IValid target )
	{
		return !_targetToTrackId.TryGetValue( target, out var set ) || set.Count == 1 && set.Contains( trackId );
	}

	private void Bind( Guid trackId, IValid? target )
	{
		if ( ReferenceEquals( _trackIdToTarget.GetValueOrDefault( trackId ), target ) ) return;

		if ( target is not null )
		{
			var scene = target switch
			{
				GameObject go => go.Scene,
				Component cmp => cmp.Scene,
				_ => null
			};

			Assert.AreEqual( Scene, scene, "Can't bind to an object from a different scene!" );
		}

		Unbind( trackId );

		_trackIdToTarget.Add( trackId, target );

		if ( target is null ) return;

		_targetToTrackId.GetOrCreate( target ).Add( trackId );
	}

	private void Unbind( Guid trackId )
	{
		if ( !_trackIdToTarget.Remove( trackId, out var target ) ) return;
		if ( target is null ) return;

		if ( !_targetToTrackId.TryGetValue( target, out var set ) ) return;
		if ( !set.Remove( trackId ) ) return;
		if ( set.Count != 0 ) return;

		_targetToTrackId.Remove( target );
	}

	#region Serialization

	private record struct Model(
		ImmutableArray<MappingModel>? GameObjects = null,
		ImmutableArray<MappingModel>? Components = null );

	private record struct MappingModel( Guid Track, Guid? Reference );

	public JsonNode Serialize()
	{
		// TODO: prune mappings if there aren't any matching tracks on any clip in the project?

		// Only serialize bindings to saved objects, otherwise they'll be null after loading anyway

		var model = new Model(
			[
				.._trackIdToTarget
					.Where( x => x.Value is GameObject go && CanBeSaved( go ) )
					.Select( x => new MappingModel( x.Key, ((GameObject)x.Value!).Id ) )
			],
			[
				.._trackIdToTarget
					.Where( x => x.Value is Component cmp && CanBeSaved( cmp ) )
					.Select( x => new MappingModel( x.Key, ((Component)x.Value!).Id ) )
			] );

		return Json.ToNode( model );
	}

	private static bool CanBeSaved( GameObject? go )
	{
		if ( !go.IsValid() ) return false;
		if ( (go.Flags & GameObjectFlags.NotSaved) != 0 ) return false;

		return go.Parent is null || CanBeSaved( go.Parent );
	}

	private static bool CanBeSaved( Component? cmp )
	{
		if ( !cmp.IsValid() ) return false;
		if ( (cmp.Flags & ComponentFlags.NotSaved) != 0 ) return false;

		return CanBeSaved( cmp.GameObject );
	}

	public void Deserialize( JsonNode? node )
	{
		_trackIdToTarget.Clear();
		_targetToTrackId.Clear();

		if ( Json.FromNode<Model?>( node ) is not { } model ) return;

		if ( model.GameObjects is { } objects )
		{
			foreach ( var mapping in objects )
			{
				if ( mapping.Reference is { } id )
				{
					Bind( mapping.Track, Scene.Directory.FindByGuid( id ) );
				}
			}
		}

		if ( model.Components is { } components )
		{
			foreach ( var mapping in components )
			{
				if ( mapping.Reference is { } id )
				{
					Bind( mapping.Track, Scene.Directory.FindComponentByGuid( id ) );
				}
			}
		}
	}

	#endregion

	#region Reference Targets

	private abstract class TargetReference<T>( TrackBinder binder, ITrackReference<GameObject>? parent, Guid id ) : ITrackReference<T>
		where T : class, IValid
	{
		public Guid Id => id;
		public Type TargetType => typeof( T );

		public TrackBinder Binder => binder;

		public abstract string Name { get; }
		public abstract bool IsBound { get; }
		public abstract bool IsActive { get; }
		public ITrackReference<GameObject>? Parent => parent;

		public T? Value => Binder.TryGetBinding<T>( Id, out var target ) ? target : AutoBind();

		public void Reset() => Binder.Unbind( Id );
		public void Bind( T? value ) => Binder.Bind( Id, value );

		private T? AutoBind()
		{
			if ( OnAutoBind() is not { } value ) return null;

			Bind( value );
			return value;
		}

		protected abstract T? OnAutoBind();
	}

	/// <summary>
	/// Target that references a <see cref="GameObject"/> in a scene.
	/// </summary>
	private sealed class GameObjectReference( TrackBinder binder, ITrackReference<GameObject>? parent, string name, Guid id, Guid? referenceId )
		: TargetReference<GameObject>( binder, parent, id ), ITrackReference<GameObject>
	{
		public override string Name => Value?.Name ?? name;
		public override bool IsBound => Value is { IsValid: true, IsDestroyed: false };
		public override bool IsActive => Value is { IsValid: true, IsDestroyed: false, Active: true };

		/// <summary>
		/// If our parent object is bound, try to bind to a child object with a matching name.
		/// If we have no parent, look up by referenceId, or default to a root object with the right name.
		/// </summary>
		protected override GameObject? OnAutoBind()
		{
			if ( referenceId is { } refId && Binder.Scene.Directory.FindByGuid( refId ) is { } match )
			{
				return match;
			}

			var parentObj = Parent is null ? Binder.Scene : Parent.Value;

			return parentObj.IsValid()
				? parentObj.Children.FirstOrDefault( x => x.IsValid && !x.IsDestroyed && x.Name == name && Binder.CanAutoBind( Id, x ) )
				: null;
		}
	}

	private ITrackReference CreateComponentReference( TrackBinder binder, ITrackReference<GameObject>? parent, Type componentType, Guid id, Guid? referenceId )
	{
		var componentRefType = typeof( ComponentReference<> )
			.MakeGenericType( componentType );

		return (ITrackReference)Activator.CreateInstance( componentRefType, binder, parent, id, referenceId )!;
	}

	/// <summary>
	/// Target that references a <see cref="Component"/> in a scene.
	/// </summary>
	private sealed class ComponentReference<T>( TrackBinder binder, ITrackReference<GameObject>? parent, Guid id, Guid? referenceId )
		: TargetReference<T>( binder, parent, id ) where T : Component
	{
		public override string Name => typeof( T ).Name;
		public override bool IsBound => Value is { IsValid: true, GameObject.IsDestroyed: false };
		public override bool IsActive => Value is { IsValid: true, GameObject.IsDestroyed: false, Active: true };

		/// <summary>
		/// If our parent object is bound, try to bind to a component with a matching type.
		/// </summary>
		protected override T? OnAutoBind()
		{
			if ( referenceId is { } refId && Binder.Scene.Directory.FindComponentByGuid( refId ) is T match )
			{
				return match;
			}

			return Parent?.Value is { } go
				? go.Components
					.GetAll<T>( FindMode.EverythingInSelf )
					.FirstOrDefault( x => x.IsValid && !x.GameObject.IsDestroyed && Binder.CanAutoBind( Id, x ) )
				: null;
		}
	}

	#endregion
}

file sealed class BinderConverter : JsonConverter<TrackBinder>
{
	public override TrackBinder Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		var node = JsonSerializer.Deserialize<JsonNode>( ref reader, options );
		var binder = new TrackBinder( Game.ActiveScene );

		binder.Deserialize( node );

		return binder;
	}

	public override void Write( Utf8JsonWriter writer, TrackBinder value, JsonSerializerOptions options )
	{
		JsonSerializer.Serialize( writer, value.Serialize(), options );
	}
}
