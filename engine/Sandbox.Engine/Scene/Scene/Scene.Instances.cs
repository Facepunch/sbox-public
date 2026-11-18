using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>Links an instance's objects to their identities in the shared definition.</summary>
internal sealed record SceneInstance( Guid Source, ImmutableDictionary<Guid, Guid> Objects );
/// <summary>The shared contents of a scene-local instance, including nested instances.</summary>
internal sealed record SceneInstanceDefinition( JsonObject Object, ImmutableDictionary<Guid, SceneInstance> Instances );
internal sealed class SceneInstanceCycleException() : InvalidOperationException( "Scene instances cannot contain themselves." );
/// <summary>Immutable authoring data that can be captured cheaply by scene undo.</summary>
internal sealed record SceneInstanceState(
	ImmutableDictionary<Guid, SceneInstanceDefinition> Definitions,
	ImmutableDictionary<Guid, SceneInstance> Instances )
{
	internal static SceneInstanceState Empty { get; } = new(
		ImmutableDictionary<Guid, SceneInstanceDefinition>.Empty,
		ImmutableDictionary<Guid, SceneInstance>.Empty );
}

public partial class Scene
{
	internal const string InstancesKey = "Instances";
	internal SceneInstanceState InstanceState { get; set; } = SceneInstanceState.Empty;

	/// <summary>Finds the nearest instance containing an object.</summary>
	internal GameObject GetInstanceRoot( GameObject gameObject )
	{
		if ( InstanceState.Instances.IsEmpty ) return null;
		for ( var current = gameObject; current.IsValid() && current != this; current = current.Parent )
		{
			if ( !current.IsDestroyed && InstanceState.Instances.ContainsKey( current.Id ) )
				return current;
		}

		return null;
	}

	/// <summary>Turns a group into the first copy of a scene-local shared definition.</summary>
	internal void CreateInstance( GameObject root )
	{
		if ( !IsEditor || root.Scene != this || root.IsPrefabInstance )
			throw new InvalidOperationException( "A scene instance requires an independent root in an editor scene." );

		var instance = new SceneInstance( Guid.NewGuid(), ImmutableDictionary<Guid, Guid>.Empty );
		var (definition, mapped) = CaptureInstance( root, instance );
		InstanceState = InstanceState with
		{
			Definitions = InstanceState.Definitions.Add( instance.Source, definition ),
			Instances = InstanceState.Instances.SetItem( root.Id, mapped )
		};
	}

	/// <summary>Leaves the instance's contents as ordinary scene objects.</summary>
	internal void UnlinkInstance( GameObject root )
	{
		InstanceState = InstanceState with { Instances = InstanceState.Instances.Remove( root.Id ) };
	}

	/// <summary>Captures shared content without including the copy's placement or root settings.</summary>
	private (SceneInstanceDefinition Definition, SceneInstance Instance) CaptureInstance( GameObject root, SceneInstance instance )
	{
		using var sceneScope = Push();
		using var suppressBlobs = BlobDataSerializer.Suppress();
		var json = root.Serialize();
		var toSource = instance.Objects.ToDictionary( x => x.Value, x => x.Key );
		var objects = instance.Objects.ToBuilder();
		foreach ( var id in SceneUtility.CreateUniqueGuidLookup( json ).Keys )
		{
			if ( toSource.ContainsKey( id ) ) continue;
			var sourceId = Guid.NewGuid();
			toSource.Add( id, sourceId );
			objects.Add( sourceId, id );
		}

		var nested = ImmutableDictionary.CreateBuilder<Guid, SceneInstance>();
		foreach ( var child in root.GetAllObjects( false ).Where( x => x != root ) )
		{
			if ( !InstanceState.Instances.TryGetValue( child.Id, out var childInstance ) )
				continue;
			var parentInstance = GetInstanceRoot( child.Parent );
			if ( parentInstance != root && parentInstance?.IsAncestor( root ) == true )
				continue;

			if ( !toSource.TryGetValue( child.Id, out var canonicalChild ) ) continue;
			nested.Add( canonicalChild, childInstance with
			{
				Objects = childInstance.Objects
					.Where( x => toSource.ContainsKey( x.Value ) )
					.ToImmutableDictionary( x => x.Key, x => toSource[x.Value] )
			} );
		}

		RemapInstanceIds( json, toSource );
		foreach ( var node in InstanceObjects( json ) )
		{
			if ( node[GameObject.JsonKeys.Id] is JsonValue value &&
				value.TryGetValue<Guid>( out var id ) && nested.ContainsKey( id ) )
			{
				node.Remove( GameObject.JsonKeys.Components );
				node.Remove( GameObject.JsonKeys.Children );
			}
		}

		if ( InstanceState.Definitions.TryGetValue( instance.Source, out var previous ) )
		{
			foreach ( var (key, value) in previous.Object )
			{
				if ( key is GameObject.JsonKeys.Components or GameObject.JsonKeys.Children ) continue;
				json[key] = value?.DeepClone();
			}
		}

		return (new( json, nested.ToImmutable() ), instance with { Objects = objects.ToImmutable() });
	}

	/// <summary>Applies authored content changes to linked copies while preserving their identities.</summary>
	internal void SynchronizeInstances( IEnumerable<Guid> roots )
	{
		var originalDefinitions = InstanceState.Definitions;
		var updated = new HashSet<Guid>();
		var authored = new HashSet<Guid>();
		var candidates = roots.Select( Directory.FindByGuid )
			.Where( x => x.IsValid() && !x.IsDestroyed )
			.SelectMany( x => x.GetAllObjects( false ) ).Reverse().Distinct().ToArray();
		if ( candidates.Length == 0 ) return;
		ValidateInstanceDefinitions( originalDefinitions, InstanceState.Instances.Values.Select( x => x.Source ) );
		foreach ( var root in candidates )
		{
			if ( !root.IsValid() || root.IsDestroyed || !InstanceState.Instances.TryGetValue( root.Id, out var instance ) )
				continue;
			instance = MapNestedInstanceObjects( instance );
			InstanceState = InstanceState with { Instances = InstanceState.Instances.SetItem( root.Id, instance ) };

			var (definition, mapped) = CaptureInstance( root, instance );
			var previous = originalDefinitions[instance.Source];
			if ( JsonNode.DeepEquals( Json.ToNode( definition ), Json.ToNode( previous ) ) )
				continue;
			if ( updated.Contains( instance.Source ) )
			{
				if ( !JsonNode.DeepEquals( Json.ToNode( definition ), Json.ToNode( InstanceState.Definitions[instance.Source] ) ) )
					throw new InvalidOperationException( "Multiple copies of a scene instance were changed differently. Edit one copy at a time." );
				authored.Add( root.Id );
				continue;
			}

			var definitions = InstanceState.Definitions.SetItem( instance.Source, definition );
			ValidateInstanceDefinitions( definitions );
			InstanceState = InstanceState with
			{
				Definitions = definitions,
				Instances = InstanceState.Instances.SetItem( root.Id, mapped )
			};
			updated.Add( instance.Source );
			authored.Add( root.Id );
		}

		if ( updated.Count == 0 ) return;

		var copies = InstanceState.Instances
			.Where( x => updated.Contains( x.Value.Source ) && !authored.Contains( x.Key ) )
			.Select( x => Directory.FindByGuid( x.Key ) )
			.Where( x => x.IsValid() && !x.IsDestroyed ).ToHashSet();

		// Leave the edited hierarchy intact, including native mesh handles held by the active tool.
		RefreshInstanceRoots( copies.Where( x =>
		{
			for ( var parent = x.Parent; parent.IsValid() && parent != this; parent = parent.Parent )
			{
				if ( copies.Contains( parent ) ) return false;
			}
			return true;
		} ) );
	}

	/// <summary>Refreshes content without replacing existing object and component identities.</summary>
	private void RefreshInstanceRoots( IEnumerable<GameObject> roots )
	{
		using var sceneScope = Push();
		using var batch = CallbackBatch.Isolated();
		foreach ( var root in roots )
		{
			var instance = MapNestedInstanceObjects( InstanceState.Instances[root.Id] );
			var placement = root.Serialize( new GameObject.SerializeOptions { IgnoreChildren = true, IgnoreComponents = true } );
			var previousRoots = root.GetAllObjects( false ).Where( x => x != root ).Select( x => x.Id ).ToArray();
			var expandedRoots = new HashSet<Guid>();
			var json = ExpandInstance( root.Id, placement, instance, [], expandedRoots );
			// Discard links for children that are no longer nested instances.
			InstanceState = InstanceState with
			{
				Instances = InstanceState.Instances.RemoveRange( previousRoots.Where( x => !expandedRoots.Contains( x ) ) )
			};
			root.Deserialize( json, new GameObject.DeserializeOptions { IsRefreshing = true } );
		}
	}

	/// <summary>Matches nested identities to the shared definition without rebuilding objects.</summary>
	private SceneInstance MapNestedInstanceObjects( SceneInstance instance )
	{
		var definition = InstanceState.Definitions[instance.Source];
		if ( definition.Instances.IsEmpty ) return instance;
		var objects = instance.Objects.ToBuilder();
		var canonicalIds = instance.Objects.ToDictionary( x => x.Value, x => x.Key );
		foreach ( var (id, nested) in definition.Instances )
		{
			if ( !objects.TryGetValue( id, out var rootId ) ||
				!InstanceState.Instances.TryGetValue( rootId, out var current ) || current.Source != nested.Source )
				continue;
			current = MapNestedInstanceObjects( current );
			foreach ( var (childId, canonical) in nested.Objects )
			{
				if ( !current.Objects.TryGetValue( childId, out var actual ) ) continue;
				if ( canonicalIds.TryGetValue( actual, out var previous ) && previous != canonical )
					objects.Remove( previous );
				if ( objects.TryGetValue( canonical, out var old ) && old != actual )
					canonicalIds.Remove( old );
				objects[canonical] = actual;
				canonicalIds[actual] = canonical;
			}
		}
		return instance with { Objects = objects.ToImmutable() };
	}

	/// <summary>Includes definitions that contain one of the changed sources.</summary>
	internal HashSet<Guid> DependentInstanceSources( IEnumerable<Guid> sources )
	{
		var affected = sources.ToHashSet();
		bool changed;
		do
		{
			changed = false;
			foreach ( var (id, definition) in InstanceState.Definitions )
			{
				if ( definition.Instances.Values.Any( x => affected.Contains( x.Source ) ) )
					changed |= affected.Add( id );
			}
		} while ( changed );
		return affected;
	}

	/// <summary>Expands shared contents into a copy's object IDs and local placement.</summary>
	private JsonObject ExpandInstance( Guid rootId, JsonObject placement, SceneInstance instance, HashSet<Guid> visiting, HashSet<Guid> expandedRoots )
	{
		if ( !visiting.Add( instance.Source ) )
			throw new SceneInstanceCycleException();
		if ( !InstanceState.Definitions.TryGetValue( instance.Source, out var definition ) )
			throw new InvalidOperationException( $"Missing scene instance definition {instance.Source}." );
		expandedRoots.Add( rootId );

		var mappings = instance.Objects.ToBuilder();
		foreach ( var canonical in SceneUtility.CreateUniqueGuidLookup( definition.Object ).Keys )
		{
			if ( !mappings.ContainsKey( canonical ) )
				mappings.Add( canonical, DeriveInstanceId( rootId, canonical ) );
		}

		foreach ( var nested in definition.Instances.Values )
		{
			foreach ( var canonical in nested.Objects.Values )
			{
				if ( !mappings.ContainsKey( canonical ) )
					mappings.Add( canonical, DeriveInstanceId( rootId, canonical ) );
			}
		}

		var rootCanonicalId = definition.Object[GameObject.JsonKeys.Id].GetValue<Guid>();
		mappings[rootCanonicalId] = rootId;
		instance = instance with { Objects = mappings.ToImmutable() };
		if ( IsEditor )
			InstanceState = InstanceState with { Instances = InstanceState.Instances.SetItem( rootId, instance ) };

		var json = definition.Object.DeepClone().AsObject();
		RemapInstanceIds( json, mappings );
		var canonicalIds = mappings.ToDictionary( x => x.Value, x => x.Key );
		foreach ( var node in InstanceObjects( json ).ToArray() )
		{
			var id = node[GameObject.JsonKeys.Id]?.GetValue<Guid>() ?? Guid.Empty;
			var canonical = canonicalIds.GetValueOrDefault( id );
			if ( !definition.Instances.TryGetValue( canonical, out var nested ) ) continue;

			var mapped = nested with { Objects = nested.Objects.ToImmutableDictionary( x => x.Key, x => mappings[x.Value] ) };
			if ( InstanceState.Instances.TryGetValue( id, out var current ) && current.Source == mapped.Source )
				mapped = mapped with { Objects = current.Objects.SetItems( mapped.Objects ) };
			var expanded = ExpandInstance( id, node, mapped, visiting, expandedRoots );
			node.ReplaceWith( expanded );
		}

		foreach ( var (key, value) in placement )
		{
			if ( key is GameObject.JsonKeys.Components or GameObject.JsonKeys.Children ) continue;
			json[key] = value?.DeepClone();
		}

		visiting.Remove( instance.Source );
		return json;
	}

	/// <summary>Keeps duplicates linked, with independent definitions when copying across scenes.</summary>
	internal void CopyInstanceLinks( GameObject original, IReadOnlyDictionary<Guid, Guid> clonedIds )
	{
		if ( !IsEditor || !original.Scene.IsEditor || original.Scene.InstanceState.Instances.IsEmpty ) return;
		var source = original.Scene.ExportInstanceState( clonedIds.Keys );
		if ( source.Instances.IsEmpty ) return;
		if ( original.Scene != this )
			source = ForkInstanceSources( source );
		ImportInstanceLinks( source, clonedIds );
	}

	/// <summary>Attaches authoring links after objects have been cloned or pasted.</summary>
	internal void ImportInstanceLinks( SceneInstanceState source, IReadOnlyDictionary<Guid, Guid> clonedIds )
	{
		var instances = InstanceState.Instances.ToBuilder();
		foreach ( var (id, instance) in source.Instances )
		{
			if ( !clonedIds.TryGetValue( id, out var clonedId ) ) continue;
			instances[clonedId] = instance with
			{
				Objects = instance.Objects.Where( x => clonedIds.ContainsKey( x.Value ) )
					.ToImmutableDictionary( x => x.Key, x => clonedIds[x.Value] )
			};
		}

		var definitions = InstanceState.Definitions;
		foreach ( var (id, definition) in source.Definitions )
		{
			if ( !definitions.ContainsKey( id ) )
				definitions = definitions.Add( id, definition );
		}
		InstanceState = new( definitions, instances.ToImmutable() );

		var imported = source.Instances.Keys.Where( clonedIds.ContainsKey ).Select( x => clonedIds[x] ).ToHashSet();
		var roots = imported.Select( Directory.FindByGuid ).Where( x =>
			x.IsValid() && !imported.Contains( GetInstanceRoot( x.Parent )?.Id ?? Guid.Empty ) );
		RefreshInstanceRoots( roots );
	}

	/// <summary>Saves only definitions and links still used by the scene.</summary>
	internal JsonNode SerializeInstances()
	{
		if ( !IsEditor ) return null;
		var state = ExportInstanceState( InstanceState.Instances.Keys );
		return state.Instances.IsEmpty ? null : Json.ToNode( state );
	}

	/// <summary>Collects the definitions needed by a set of instance roots.</summary>
	internal SceneInstanceState ExportInstanceState( IEnumerable<Guid> roots )
	{
		var ids = roots.ToHashSet();
		var live = InstanceState.Instances
			.Where( x => ids.Contains( x.Key ) && Directory.FindByGuid( x.Key ) is { IsValid: true, IsDestroyed: false } )
			.ToImmutableDictionary();
		if ( live.IsEmpty ) return SceneInstanceState.Empty;

		var sources = new HashSet<Guid>( live.Values.Select( x => x.Source ) );
		var pending = new Queue<Guid>( sources );
		while ( pending.TryDequeue( out var source ) )
		{
			foreach ( var nested in InstanceState.Definitions[source].Instances.Values )
			{
				if ( sources.Add( nested.Source ) ) pending.Enqueue( nested.Source );
			}
		}

		return new SceneInstanceState(
			InstanceState.Definitions.Where( x => sources.Contains( x.Key ) ).ToImmutableDictionary(), live );
	}

	/// <summary>Separates imported definitions from links belonging to another scene.</summary>
	internal static SceneInstanceState ForkInstanceSources( SceneInstanceState state )
	{
		var sources = state.Definitions.Keys.ToDictionary( x => x, _ => Guid.NewGuid() );
		SceneInstance Remap( SceneInstance instance ) => instance with { Source = sources[instance.Source] };
		return new(
			state.Definitions.ToImmutableDictionary( x => sources[x.Key], x => x.Value with
			{
				Instances = x.Value.Instances.ToImmutableDictionary( n => n.Key, n => Remap( n.Value ) )
			} ),
			state.Instances.ToImmutableDictionary( x => x.Key, x => Remap( x.Value ) ) );
	}

	/// <summary>Loads authoring links without replacing the scene's saved object contents.</summary>
	internal void DeserializeInstances( JsonNode node, bool additive = false )
	{
		var state = SceneInstanceState.Empty;
		try
		{
			state = node is null ? SceneInstanceState.Empty : Json.FromNode<SceneInstanceState>( node );
			if ( state?.Definitions is null || state.Instances is null )
				throw new JsonException( "Scene instance definitions and links are required." );

			var definitions = state.Definitions;
			foreach ( var source in state.Definitions.Keys )
			{
				try
				{
					ValidateInstanceDefinitions( state.Definitions, [source] );
				}
				catch ( InvalidOperationException e )
				{
					Log.Warning( $"Could not load scene instance definition {source}: {e.Message}" );
					definitions = definitions.Remove( source );
				}
			}
			state = new( definitions, state.Instances
				.Where( x => x.Value?.Objects is not null && definitions.ContainsKey( x.Value.Source ) )
				.ToImmutableDictionary() );
		}
		catch ( JsonException e )
		{
			Log.Warning( $"Could not load scene instance links: {e.Message}" );
			state = SceneInstanceState.Empty;
		}
		InstanceState = additive
			? new( InstanceState.Definitions.SetItems( state.Definitions ), InstanceState.Instances.SetItems( state.Instances ) )
			: state;
	}

	/// <summary>Rejects missing definitions and recursive instance nesting.</summary>
	private static void ValidateInstanceDefinitions( ImmutableDictionary<Guid, SceneInstanceDefinition> definitions, IEnumerable<Guid> sources = null )
	{
		var complete = new HashSet<Guid>();
		var visiting = new HashSet<Guid>();
		foreach ( var id in sources ?? definitions.Keys )
			Visit( id );

		void Visit( Guid id )
		{
			if ( complete.Contains( id ) ) return;
			if ( !visiting.Add( id ) )
				throw new SceneInstanceCycleException();
			if ( !definitions.TryGetValue( id, out var definition ) || definition?.Object is null || definition.Instances is null )
				throw new InvalidOperationException( $"Missing or invalid scene instance definition {id}." );
			foreach ( var nested in definition.Instances.Values )
			{
				if ( nested?.Objects is null )
					throw new InvalidOperationException( $"Invalid nested scene instance in {id}." );
				Visit( nested.Source );
			}
			visiting.Remove( id );
			complete.Add( id );
		}
	}

	private static void RemapInstanceIds( JsonObject json, IReadOnlyDictionary<Guid, Guid> mappings )
	{
		Json.WalkJsonTree( json, ( _, value ) =>
			value.TryGetValue<Guid>( out var id ) && mappings.TryGetValue( id, out var mapped )
				? JsonValue.Create( mapped ) : value );
	}

	private static IEnumerable<JsonObject> InstanceObjects( JsonObject root )
	{
		yield return root;
		foreach ( var child in root[GameObject.JsonKeys.Children]?.AsArray()?.ToArray() ?? [] )
		{
			if ( child is not JsonObject go ) continue;
			foreach ( var descendant in InstanceObjects( go ) )
				yield return descendant;
		}
	}

	/// <summary>Keeps newly added shared objects stable across refreshes and reloads.</summary>
	private static Guid DeriveInstanceId( Guid root, Guid source )
	{
		Span<byte> bytes = stackalloc byte[32];
		root.TryWriteBytes( bytes[..16] );
		source.TryWriteBytes( bytes[16..] );
		return new Guid( MD5.HashData( bytes ) );
	}
}
