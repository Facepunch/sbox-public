using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

public partial class GameObject
{
	internal const int GameObjectVersion = 1;

	/// <summary>
	/// Helper variable for editor refreshes during deserialization.
	/// </summary>
	private bool _removeAfterDeserializationRefresh = false;

	public class SerializeOptions
	{
		/// <summary>
		/// If we're serializing for network, we won't include any networked objects
		/// </summary>
		public bool SceneForNetwork { get; set; }

		/// <summary>
		/// We're cloning this object
		/// </summary>
		[Obsolete( "Has no effect" )]
		public bool Cloning { get; set; }

		/// <summary>
		/// We're going to send a single network object
		/// </summary>
		public bool SingleNetworkObject { get; set; }

		/// <summary>
		/// Serialize the full hierarchy, prefab instances will be expanded to include all their children and components.
		/// All instances will be considered nested instances.
		/// The path to the prefab instance will be included in the JSON <see cref="JsonKeys.EditorPrefabInstanceNestedSource"/>
		/// Implies 
		/// </summary>
		internal bool SerializePrefabForDiff { get; set; }

		/// <summary>
		/// Serialize the prefab instance so it can be used to update the state of the prefab.
		/// Makes sure TopLevel nested prefabs are flagged, so they can get converted to a full prefab instance in the prefab.
		/// </summary>
		internal bool SerializeForPrefabInstanceToPrefabUpdate { get; set; }

		/// <summary>
		/// Don't serialize gameObject children.
		/// </summary>
		internal bool IgnoreChildren { get; set; }

		/// <summary>
		/// Don't serialize gameObject components.
		/// </summary>
		internal bool IgnoreComponents { get; set; }

		internal bool ShouldSave( GameObject gameObject )
		{
			var shouldIgnoreNotSavedFlag = SingleNetworkObject || SceneForNetwork;

			// Marked as do not save. This means to disk, really.
			if ( gameObject.Flags.Contains( GameObjectFlags.NotSaved ) && !shouldIgnoreNotSavedFlag ) return false;

			// We're saving for the network.
			if ( SceneForNetwork || SingleNetworkObject )
			{
				if ( gameObject.NetworkMode == NetworkMode.Never ) return false;
				if ( gameObject.Flags.Contains( GameObjectFlags.NotNetworked ) ) return false;
			}

			var isObjectNetworked = gameObject.Network.Active || gameObject.NetworkMode == NetworkMode.Object;

			//
			// If we're serializing the entire scene to send down the network, then don't send this
			// object if it's a network object, it'll get sent in the snapshots.
			//
			if ( SceneForNetwork && isObjectNetworked ) return false;

			return true;
		}
	}

	private static readonly SerializeOptions _defaultSerializeOptions = new();

	public struct DeserializeOptions
	{
		/// <summary>
		/// 
		/// When true, updates the existing GameObject hierarchy instead of creating a new one from scratch.
		/// This preserves C# object references and identity, used for undo/redo operations,
		/// and prefab instance patching.
		/// 
		/// During refreshing:
		/// - Existing GameObjects and Components are updated rather than recreated
		/// - Objects are matched by their GUIDs
		/// - Only missing objects are created
		/// - Existing objects not present in the JSON are removed
		/// - Component ordering is preserved as specified in the JSON
		/// </summary>
		internal bool IsRefreshing { get; set; }

		/// <summary>
		/// Should be used in Conjunction with <see cref="IsRefreshing"/>.
		/// Makes sure child networked objects are not removed during refresh.
		/// </summary>
		internal bool IsNetworkRefresh { get; set; }

		/// <summary>
		/// If true, the network refresh originated from the Host,
		/// so we should allow it to overwrite [Sync] properties.
		/// </summary>
		internal bool IsRefreshFromHost { get; set; }

		/// <summary>
		/// Allows overriding the transform when deserializing. Will apply only to the root object.
		/// </summary>
		public Transform? TransformOverride { get; set; }
	}

	private static readonly DeserializeOptions _defaultDeserializeOptions = new();

	/// <summary>
	/// Returns either a full JsonObject with all the GameObjects data,
	/// or if this GameObject is a prefab instance, it will return an object containing the patch/diff between instance and prefab.
	/// </summary>
	public virtual JsonObject Serialize( SerializeOptions options = null )
	{
		options ??= _defaultSerializeOptions;

		if ( !options.ShouldSave( this ) ) return null;

		if ( IsOutermostPrefabInstanceRoot && !options.SerializePrefabForDiff )
		{
			if ( options.SceneForNetwork || options.SingleNetworkObject )
			{
				return SerializePrefabInstanceForNetwork( options );
			}
			else
			{
				return SerializePrefabInstance();
			}
		}

		var json = SerializeStandard( options );

		return json;
	}

	/// <summary>
	/// Creates a JSON representation of this prefab instance including its overrides and GUID mappings.
	/// </summary>
	private JsonObject SerializePrefabInstance()
	{
		PrefabInstance.RefreshPatch();

		var json = new JsonObject();

		json[JsonKeys.Id] = Id;
		if ( GameObjectVersion != 0 ) json[JsonKeys.Version] = GameObjectVersion;
		json[JsonKeys.PrefabInstanceSource] = JsonValue.Create( PrefabInstance.PrefabSource );
		json[JsonKeys.PrefabInstancePatch] = Json.ToNode( PrefabInstance.Patch );
		PrefabInstance.ValidatePrefabLookup();
		json[JsonKeys.PrefabIdToInstanceId] = Json.ToNode( PrefabInstance.PrefabToInstanceLookup );

		return json;
	}

	/// <summary>
	/// Creates a JSON representation of this prefab instance including its overrides and GUID mappings.
	/// Only includes non networked objects.
	/// </summary>
	private JsonObject SerializePrefabInstanceForNetwork( SerializeOptions options )
	{
		var instanceData = SerializeStandard( options );
		PrefabInstance.RemapInstanceIdsToPrefabIds( ref instanceData );

		var prefabScene = (PrefabCacheScene)GetPrefab( PrefabInstance.PrefabSource );
		Assert.IsValid( prefabScene );

		var fullPrefabData = prefabScene.Serialize( new SerializeOptions
		{
			SerializePrefabForDiff = true
		} );

		var patch = Json.CalculateDifferences( fullPrefabData, instanceData, DiffObjectDefinitions );

		var json = new JsonObject();

		json[JsonKeys.Id] = Id;
		if ( GameObjectVersion != 0 ) json[JsonKeys.Version] = GameObjectVersion;
		json[JsonKeys.PrefabInstanceSource] = JsonValue.Create( PrefabInstance.PrefabSource );
		json[JsonKeys.PrefabInstancePatch] = Json.ToNode( patch );
		PrefabInstance.ValidatePrefabLookup();
		json[JsonKeys.PrefabIdToInstanceId] = Json.ToNode( PrefabInstance.PrefabToInstanceLookup );

		return json;
	}

	/// <summary>
	/// Returns a JsonObject containing all the GameObject's data.
	/// </summary>
	internal virtual JsonObject SerializeStandard( SerializeOptions options )
	{
		using var sceneScope = Scene.Push();

		// Will omit serializing the target of embedded Action Graphs
		using var targetScope = ActionGraph.PushTarget( InputDefinition.Target( typeof( GameObject ) ) );

		if ( !options.ShouldSave( this ) ) return null;

		var json = new JsonObject();

		json[JsonKeys.Id] = Id;
		if ( GameObjectVersion != 0 ) json[JsonKeys.Version] = GameObjectVersion;
		json[JsonKeys.Flags] = (long)Flags;
		json[JsonKeys.Name] = Name;

		SerializeTransform( json );

		json.Add( JsonKeys.Tags, string.Join( ",", Tags.TryGetAll( false ) ) );
		json.Add( JsonKeys.Enabled, Enabled );
		json.Add( JsonKeys.NetworkMode, (int)NetworkMode );
		json.Add( JsonKeys.NetworkInterpolation, NetworkInterpolation );
		json.Add( JsonKeys.NetworkOrphaned, (int)NetworkOrphaned );
		json.Add( JsonKeys.AlwaysTransmit, AlwaysTransmit );
		json.Add( JsonKeys.OwnerTransfer, (int)OwnerTransfer );

		if ( IsNestedPrefabInstanceRoot || (IsOutermostPrefabInstanceRoot && options.SerializePrefabForDiff) )
		{
			if ( options.SerializeForPrefabInstanceToPrefabUpdate && Parent is not null && Parent.IsOutermostPrefabInstanceRoot )
			{
				json[JsonKeys.EditorSkipPrefabBreakOnRefresh] = true;
			}
			else
			{
				json[JsonKeys.EditorPrefabInstanceNestedSource] = JsonValue.Create( PrefabInstance.PrefabSource );
			}
		}

		if ( !options.IgnoreComponents )
		{
			var components = new JsonArray();

			foreach ( var component in Components.GetAll() )
			{
				if ( component is null ) continue;

				if ( component is MissingComponent missing )
				{
					components.Add( missing.GetJson() );
					continue;
				}

				try
				{
					var result = component.Serialize( options );
					if ( result is null ) continue;

					components.Add( result );
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, $"Exception when serializing {component} - skipping!" );
				}
			}

			json.Add( JsonKeys.Components, components );
		}

		if ( !options.IgnoreChildren )
		{
			var children = new JsonArray();

			for ( int i = 0; i < Children.Count; i++ )
			{
				var child = Children[i];

				if ( child is null ) continue;

				// Child GameObjects that are being destroyed don't want to be serialized
				if ( child.IsDestroyed ) continue;

				// check both our current network status, and our wish network status
				// because we might be in the middle of spawning, and will become network
				// active after this.
				bool childIsNetworked = child.IsNetworkRoot || child.NetworkMode == NetworkMode.Object;

				// If this child is an independently networked object, and we're already serializing a
				// single network object we should not include it.
				if ( options.SingleNetworkObject && childIsNetworked ) continue;

				try
				{
					var result = child.Serialize( options );

					if ( result is not null )
					{
						children.Add( result );
					}
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, $"Exception when serializing GameObject" );
				}
			}

			json.Add( JsonKeys.Children, children );
		}

		return json;
	}

	public virtual void Deserialize( JsonObject node ) => Deserialize( node, _defaultDeserializeOptions );

	public virtual void Deserialize( JsonObject node, DeserializeOptions options )
	{
		ArgumentNullException.ThrowIfNull( node, nameof( node ) );

		using var sceneScope = Scene.Push();

		var serializedVersion = (int)(node[JsonKeys.Version] ?? 0);
		if ( serializedVersion < GameObjectVersion )
		{
			JsonUpgrader.Upgrade( serializedVersion, node, GetType() );
		}

		DeserializeFlags( node, options );
		Flags |= GameObjectFlags.Deserializing;

		if ( node[JsonKeys.EditorSkipPrefabBreakOnRefresh] is null )
		{
			_prefabInstanceData = null;
		}

		// Handle nested prefab instances
		// Only init with a path, we don't have any patches or lookups for nested instances.
		if ( node[JsonKeys.EditorPrefabInstanceNestedSource] is JsonValue __PrefabNestedInstance && __PrefabNestedInstance.TryGetValue( out string prefabSource ) )
		{
			if ( this is not PrefabScene )
			{
				InitPrefabInstance( prefabSource, true );

				var prefabFile = ResourceLibrary.Get<PrefabFile>( PrefabInstance.PrefabSource );
				if ( !IsPrefabLoaded( prefabFile ) )
				{
					PostDeserialize( options );
					return;
				}

				// Need to create those since they are not stored
				PrefabInstance.InitMappingsForNestedInstance( node[JsonKeys.Id].Deserialize<Guid>() );
			}
		}
		// Handle full prefab instances
		else if ( node[JsonKeys.PrefabInstanceSource] is JsonValue __Prefab && __Prefab.TryGetValue( out prefabSource ) )
		{
			InitPrefabInstance( prefabSource, false );

			var prefabFile = ResourceLibrary.Get<PrefabFile>( PrefabInstance.PrefabSource );
			if ( !IsPrefabLoaded( prefabFile ) )
			{
				PostDeserialize( options );
				return;
			}

			Json.Patch instancePatch = null;
			Dictionary<Guid, Guid> nodePrefabToInstanceId = null;
			if ( node[JsonKeys.PrefabInstancePatch] is JsonObject patchJson )
			{
				instancePatch = Json.FromNode<Json.Patch>( patchJson );
				nodePrefabToInstanceId = node[JsonKeys.PrefabIdToInstanceId]?.Deserialize<Dictionary<Guid, Guid>>() ?? new Dictionary<Guid, Guid>();
			}
			else
			{
				// This shouldn't be able to happen in a valid project, if it for some reason does we want to know about it.

				// The prefab is missing, catch this and demote to a warning
				if ( prefabFile.IsPromise )
				{
					Log.Warning( $"Unable to load prefab '{PrefabInstance.PrefabSource}'" );
				}
				else
				{
					Log.Error( $"Prefab instance '{PrefabInstance.PrefabSource}' missing overrides, upgrader did not run for some reason." );
				}

				PostDeserialize( options );
				return;
			}

			var prefabScene = (PrefabCacheScene)GetPrefab( prefabSource );
			Assert.IsValid( prefabScene );

			var fullPrefabData = prefabScene.FullPrefabInstanceJson;
			node = Json.ApplyPatch( fullPrefabData, instancePatch, DiffObjectDefinitions );
			PrefabInstance.InitLookups( nodePrefabToInstanceId );
			PrefabInstance.InitPatch( instancePatch );
			PrefabInstance.RemapPrefabIdsToInstanceIds( ref node );
		}

		// Stop right here if we are EditorOnly
		// If we are a PrefabRoot/PrefabCacheScene marked as editor only we still want to load as the Prefab might be referenced by instances
		if ( !Scene.IsEditor && Flags.Contains( GameObjectFlags.EditorOnly ) && this is not PrefabCacheScene )
		{
			// Immediately destroy this GameObject, we don't want it in the scene.
			DestroyImmediate();
			return;
		}

		Name = node.GetPropertyValue( "Name", Name );
		DeserializeTransform( node, options );

		_enabled = node.GetPropertyValue( "Enabled", false );

		using var batchGroup = CallbackBatch.Batch();

		DeserializeId( node );

		if ( node[JsonKeys.Tags].Deserialize<string>() is { } tags )
		{
			Tags.RemoveAll();
			Tags.Add( tags.Split( ',', StringSplitOptions.RemoveEmptyEntries ) );
		}

		if ( node.TryGetPropertyValue( JsonKeys.NetworkMode, out var propertyNode ) ) NetworkMode = (NetworkMode)(int)propertyNode;
		if ( node.TryGetPropertyValue( JsonKeys.NetworkOrphaned, out propertyNode ) ) NetworkOrphaned = (NetworkOrphaned)(int)propertyNode;
		if ( node.TryGetPropertyValue( JsonKeys.AlwaysTransmit, out propertyNode ) ) AlwaysTransmit = (bool)propertyNode;
		if ( node.TryGetPropertyValue( JsonKeys.OwnerTransfer, out propertyNode ) ) OwnerTransfer = (OwnerTransfer)(int)propertyNode;
		if ( node.TryGetPropertyValue( JsonKeys.NetworkInterpolation, out propertyNode ) ) NetworkInterpolation = (bool)propertyNode;

		if ( node[JsonKeys.Components] is JsonArray componentArray )
		{
			var existingComponents = options.IsRefreshing ? Components.GetAll().ToHashSet() : null;
			var processedComponents = options.IsRefreshing ? new HashSet<Component>( existingComponents.Count ) : null;

			for ( int componentIndex = 0; componentIndex < componentArray.Count; componentIndex++ )
			{
				var component = componentArray[componentIndex];

				if ( component is not JsonObject componentJson )
				{
					Log.Warning( $"Component entry is not an object!" );
					continue;
				}

				string componentTypeName = componentJson.GetPropertyValue( Component.JsonKeys.Type, "" );

				// This is pretty delicate here, it's not explicit what it's doing, so here it is.
				// Say we have a component named the same in our game addon - I want to choose that one. Because my assumption
				// is that they might have code with [RequireComponent] that is referencing that type.
				// This might also be useful to us when developing, where we can copy paste the component into our code
				// and have it still work and be able to edit hotloading.
				// This code chooses the type in a dynamic assembly (addon code) so they always have priority over the engine stuff.
				var componentType = Game.TypeLibrary.GetType<Component>( componentTypeName, true );

				// We didn't find this component type. So lets cut them some slack by searching
				// for it without the namespace - because maybe they refactored and didn't want
				// everything to break..
				if ( componentType is null )
				{
					var idx = componentTypeName.LastIndexOf( '.' );
					if ( idx > 0 && idx < componentTypeName.Length - 1 )
					{
						var componentClassName = componentTypeName[(idx + 1)..];
						componentType = Game.TypeLibrary.GetType<Component>( componentClassName, true );
					}
				}

				//
				// Okay definitely not found, lets give up
				//
				if ( componentType is null || componentType.TargetType.IsAbstract )
				{
					Log.Warning( $"TypeLibrary couldn't find Component type {componentTypeName}" );

					var missing = new MissingComponent( componentJson );
					Components.AddMissing( missing );
					continue;
				}

				Component c = null;

				if ( options.IsRefreshing )
				{
					var guid = componentJson[Component.JsonKeys.Id].Deserialize<Guid>();
					c = Scene.Directory.FindComponentByGuid( guid );
				}

				// Components should be created disabled, and then enabled in deserialize
				// because if not, disabled components will enable and then disable.
				try
				{
					c ??= Components.Create( componentType, false );
				}
				catch ( Exception e )
				{
					Log.Error( e );
				}

				if ( c is null )
				{
					// The component was null, maybe there was an error creating it. Add a missing component reference.
					var missing = new MissingComponent( componentJson );
					Components.AddMissing( missing );
					continue;
				}

				c.Deserialize( componentJson );

				if ( options.IsRefreshing )
				{
					processedComponents.Add( c );

					// change order of components needed
					if ( Components.IndexOf( c ) != componentIndex )
					{
						Components.MoveToIndex( c, componentIndex );
					}
				}
			}

			if ( options.IsRefreshing )
			{
				// For network refresh, filter out components that shouldn't be networked
				if ( options.IsNetworkRefresh )
				{
					existingComponents.RemoveWhere( c => c.Flags.Contains( ComponentFlags.NotNetworked ) );
				}

				// Common operation for both refresh types
				existingComponents.ExceptWith( processedComponents );

				// Common destruction for both refresh types
				foreach ( var existingComponent in existingComponents )
				{
					existingComponent.Destroy();
				}
			}
		}

		if ( node[JsonKeys.Children] is JsonArray childArray )
		{
			if ( options.IsRefreshing )
			{
				// Flag all our children for removal
				// flag will be cleared if the child is found in the JSON somewhere
				foreach ( var child in Children )
				{
					child._removeAfterDeserializationRefresh = true;
				}
			}

			for ( int childIndex = 0; childIndex < childArray.Count; childIndex++ )
			{
				var child = childArray[childIndex];

				if ( child is not JsonObject jso ) continue;

				// This GameObject is only for the editor. Don't load it!
				if ( Flags.Contains( GameObjectFlags.EditorOnly ) && !Scene.IsEditor ) continue;

				GameObject go = null;

				if ( options.IsRefreshing )
				{
					var guid = jso[JsonKeys.Id].Deserialize<Guid>();
					go = Scene.Directory.FindByGuid( guid );

					if ( go is not null )
					{
						// Existing object may have been moved here from another go make sure we update the parent
						go.Parent = this;
						go._removeAfterDeserializationRefresh = false;
					}
				}

				go ??= new GameObject( this, false );
				go.Deserialize( jso, options with { TransformOverride = default } );

				if ( options.IsRefreshing )
				{
					// change order of if GOs needed
					var childActualIndex = Children.IndexOf( go );
					if ( childActualIndex != childIndex )
					{
						// Swap the GameObject in the hierarchy to match the order in the JSON.
						Children[childActualIndex] = Children[childIndex];
						Children[childIndex] = go;
					}
				}
			}
		}

		Components.ForEach( "OnLoadInternal", true, c => c.OnLoadInternal() );
		Components.ForEach( "OnValidate", true, c => c.Validate() );

		// PostDeserialize recurses into children, so if our parent is deserializing
		// we don't need to queue a call to it ourselves

		if ( Parent is null || (Parent.Flags & GameObjectFlags.Deserializing) == 0 )
		{
			CallbackBatch.Add( CommonCallback.Deserialize, () => PostDeserialize( options ), this, "PostDeserialize" );
		}

		// Trigger OnEnabled after the GameObject has been deserialized fully, _enabled was set before, so OnAwake calls properly
		UpdateEnabledStatus();
	}

	private void DeserializeFlags( JsonObject node, DeserializeOptions options )
	{
		if ( !node.TryGetPropertyValue( JsonKeys.Flags, out var inFlagNode ) )
			return;

		var inFlags = (GameObjectFlags)(long)inFlagNode;

		if ( options.IsRefreshing )
		{
			Flags = inFlags;
			return;
		}

		// We only want to deserialize certain flags, the rest are runtime only.
		const GameObjectFlags FlagsToKeep =
						GameObjectFlags.ProceduralBone |
						GameObjectFlags.EditorOnly |
						GameObjectFlags.NotNetworked |
						GameObjectFlags.Absolute |
						GameObjectFlags.PhysicsBone |
						GameObjectFlags.Hidden;

		// Clear the flags we're about to deserialize
		Flags &= ~FlagsToKeep;

		// Copy set flags from source
		Flags |= (inFlags & FlagsToKeep);

	}

	private bool IsPrefabLoaded( PrefabFile prefabFile )
	{
		if ( prefabFile?.RootObject is null )
		{
			// Sol: the prefab is missing, register a promise like the REAL resource
			// json converter does, so we know it's wanted.
			GameResource.GetPromise( typeof( PrefabFile ), PrefabInstance.PrefabSource );
		}

		if ( prefabFile == null || prefabFile.IsPromise )
		{
			Log.Warning( $"Unable to load prefab '{PrefabInstance.PrefabSource}'" );
			return false;
		}

		return true;
	}

	/// <summary>
	/// Serializing the transform depends on a bunch of stuff, so split it into this method for clarity.
	/// </summary>
	private void SerializeTransform( JsonObject json )
	{
		//
		// If we're a physics bone then we need to serialize the local position.
		//
		if ( Flags.Contains( GameObjectFlags.PhysicsBone ) )
		{
			var localTx = Parent?.WorldTransform.ToLocal( WorldTransform ) ?? WorldTransform;

			json.Add( JsonKeys.Position, JsonValue.Create( localTx.Position ) );
			json.Add( JsonKeys.Rotation, JsonValue.Create( localTx.Rotation ) );
			json.Add( JsonKeys.Scale, JsonValue.Create( localTx.Scale ) );
			return;
		}

		//
		// If it's an animated bone, we don't bother with the transform position at all
		//
		var isAttachment = Flags.Contains( GameObjectFlags.Attachment );
		var isAnimated = Flags.Contains( GameObjectFlags.Bone ) && !Flags.Contains( GameObjectFlags.ProceduralBone );

		if ( isAttachment || isAnimated )
		{
			json.Add( JsonKeys.Position, JsonValue.Create( Vector3.Zero ) );
			json.Add( JsonKeys.Rotation, JsonValue.Create( Rotation.Identity ) );
			json.Add( JsonKeys.Scale, JsonValue.Create( Vector3.One ) );
			return;
		}

		//
		// The default is to just save the local transform
		//
		{
			var tx = LocalTransform;

			json.Add( JsonKeys.Position, JsonValue.Create( tx.Position ) );
			json.Add( JsonKeys.Rotation, JsonValue.Create( tx.Rotation ) );
			json.Add( JsonKeys.Scale, JsonValue.Create( tx.Scale ) );
		}
	}

	/// <summary>
	/// Again - this can be complicated, so this is extracted
	/// </summary>
	private void DeserializeTransform( JsonObject node, DeserializeOptions options )
	{
		//
		// They're doing something special. Maybe they're creating a "duplication" at a certain position.
		//
		if ( options.TransformOverride is Transform overrideTransform )
		{
			WorldTransform = overrideTransform;
			return;
		}

		//
		// If we're a physics bone then we need to serialize the local position.
		//
		if ( Flags.Contains( GameObjectFlags.PhysicsBone ) )
		{
			var tx = global::Transform.Zero;
			tx.Position = node[JsonKeys.Position]?.Deserialize<Vector3>() ?? Vector3.Zero;
			tx.Rotation = node[JsonKeys.Rotation]?.Deserialize<Rotation>() ?? Rotation.Identity;
			tx.Scale = node[JsonKeys.Scale]?.Deserialize<Vector3>() ?? Vector3.One;

			var worldTx = Parent?.WorldTransform.ToWorld( tx ) ?? tx;
			WorldTransform = worldTx;

			return;
		}

		// Only update transform if we're not refreshing or we aren't a bone. Bones use proxy transforms
		// and we don't want to set overrides here because then they won't animate correctly.
		if ( !(options.IsRefreshing && options.IsNetworkRefresh) || !Flags.Contains( GameObjectFlags.Bone ) )
		{
			var tx = global::Transform.Zero;
			tx.Position = node[JsonKeys.Position]?.Deserialize<Vector3>() ?? Vector3.Zero;
			tx.Rotation = node[JsonKeys.Rotation]?.Deserialize<Rotation>() ?? Rotation.Identity;
			tx.Scale = node[JsonKeys.Scale]?.Deserialize<Vector3>() ?? Vector3.One;
			LocalTransform = tx;
		}
	}

	internal void DeserializeId( JsonObject node )
	{
		if ( node.TryGetPropertyValue( JsonKeys.Id, out var propertyNode ) ) SetDeterministicId( (Guid)propertyNode );
	}

	/// <summary>
	/// Only needed for legacy support, when cloning.
	/// </summary>
	/// <param name="variables"></param>
	internal void DeserializePrefabVariables( JsonObject variables )
	{
		if ( variables is null || variables.Count == 0 ) return;

		var prefabFile = ResourceLibrary.Get<PrefabFile>( PrefabInstance.PrefabSource );
		if ( prefabFile is null ) return;

		var prefabScene = SceneUtility.GetPrefabScene( prefabFile );
		if ( prefabScene is null ) return;

		foreach ( (string name, JsonNode value) in variables )
		{
#pragma warning disable CS0612
			var variable = prefabScene.Variables.Where( x => x.Id == name ).FirstOrDefault();
#pragma warning restore CS0612
			if ( variable is null )
			{
				Log.Warning( $"Prefab Variable not in prefab: {name}" );
				continue;
			}

			foreach ( var target in variable.Targets )
			{
				if ( !PrefabInstance.PrefabToInstanceLookup.TryGetValue( target.Id, out Guid guid ) )
				{
					Log.Warning( $"Prefab variable target '{target.Id}' not found" );
					continue;
				}

				var component = Scene.Directory.FindComponentByGuid( guid );
				if ( component.IsValid() )
				{
					var t = Game.TypeLibrary.GetType( component.GetType() );

					if ( value is null )
						return;

					// TODO when we eventually get rid of DeserializePrefabVariables, make DeserializeProperty private again
					component.DeserializeProperty( t.Members.FirstOrDefault( x => x.Name == target.Property ), value );
				}
			}
		}
	}

	/// <summary>
	/// Push ActionGraph source location and cache if we're a prefab instance or map object.
	/// </summary>
	private ActionGraph.SerializationOptionsScope? PushDeserializeContext()
	{
		if ( IsPrefabInstanceRoot )
		{
			var prefabFile = ResourceLibrary.Get<PrefabFile>( PrefabInstanceSource );

			if ( prefabFile is null )
			{
				Log.Warning( $"Unable to find prefab source file: \"{PrefabInstanceSource}\"." );
				return null;
			}

			return ActionGraph.PushSerializationOptions( prefabFile.SerializationOptions with
			{
				ForceUpdateCached = false,
				GuidMap = PrefabInstance.InstanceToPrefabLookup
			} );
		}

		if ( IsMapInstanceRoot )
		{
			var mapSourceLoc = MapSourceLocation.Get( MapSource );

			return ActionGraph.PushSerializationOptions( mapSourceLoc.SerializationOptions with
			{
				ForceUpdateCached = false,
				GuidMap = PrefabInstance?.InstanceToPrefabLookup
			} );
		}

		return null;
	}

	internal void PostDeserialize( DeserializeOptions options )
	{
		using var prefabContext = PushDeserializeContext();

		Components.ForEach( "PostDeserialize", true, c => c.PostDeserialize( options ) );

		for ( int i = 0; i < Children.Count; i++ )
		{
			Children[i].PostDeserialize( options );
		}

		if ( options.IsRefreshing )
		{
			// Iterate all children check which are pending deletion and destroy them
			foreach ( var child in Children )
			{
				if ( options.IsNetworkRefresh )
				{
					// Only consider networked snapshot children for pruning during network refresh
					// Skip independently networked objects and objects marked as not networked
					if ( child.NetworkMode != NetworkMode.Snapshot || child.Flags.Contains( GameObjectFlags.NotNetworked ) )
					{
						continue;
					}
				}

				if ( child._removeAfterDeserializationRefresh )
				{
					child.DestroyImmediate();
				}
			}
			Components.ForEach( "OnRefresh", true, c => c.OnRefreshInternal() );
		}

		Flags &= ~GameObjectFlags.Deserializing;
	}

	enum NetworkReferenceType
	{
		Invalid = 0,
		GameObject = 1,
		Prefab = 2,
	}

	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		var refType = (NetworkReferenceType)bs.Read<byte>();

		switch ( refType )
		{
			case NetworkReferenceType.Invalid:
				return default;
			case NetworkReferenceType.GameObject:
				if ( !Game.ActiveScene.IsValid() ) return default;
				var id = bs.Read<Guid>();
				return Game.ActiveScene.Directory.FindByGuid( id );
			case NetworkReferenceType.Prefab:
				var resourceId = bs.Read<int>();
				var prefabFile = ResourceLibrary.Get<PrefabFile>( resourceId );
				return SceneUtility.GetPrefabScene( prefabFile );
			default:
				return default;
		}
	}

	static void BytePack.ISerializer.BytePackWrite( object value, ref ByteStream bs )
	{
		if ( value is not GameObject go )
		{
			bs.Write( (byte)NetworkReferenceType.Invalid );
			return;
		}

		if ( go is PrefabScene prefabScene )
		{
			bs.Write( (byte)NetworkReferenceType.Prefab );
			bs.Write( prefabScene.Source.ResourceId );
			return;
		}

		bs.Write( (byte)NetworkReferenceType.GameObject );
		bs.Write( go.Id );
	}

	public static object JsonRead( ref Utf8JsonReader reader, Type targetType )
	{
		if ( reader.TokenType == JsonTokenType.StartObject )
		{
			var goRef = JsonSerializer.Deserialize<GameObjectReference>( ref reader );

			return goRef.Resolve( Game.ActiveScene, warn: true );
		}

		//
		// Legacy way, guid or prefab
		//
		if ( reader.TokenType == JsonTokenType.String )
		{
			if ( reader.TryGetGuid( out Guid guid ) )
			{
				var go = Game.ActiveScene.Directory.FindByGuid( guid );

				if ( go is null )
				{
					Log.Warning( $"Couldn't find GameObject {guid}" );
				}

				return go;
			}

			var stringValue = reader.GetString();

			// Added 12 dec 2023
			stringValue = stringValue.Replace( ".object", ".prefab", StringComparison.OrdinalIgnoreCase );

			if ( ResourceLibrary.TryGet( stringValue, out PrefabFile prefabFile ) )
			{
				return SceneUtility.GetPrefabScene( prefabFile );
			}

			throw new Exception( $"Prefab not found '{prefabFile}'" );
		}

		reader.Skip();
		return null;
	}

	public static void JsonWrite( object value, Utf8JsonWriter writer )
	{
		if ( value is not GameObject go )
			throw new NotImplementedException();

		if ( !go.IsValid )
		{
			writer.WriteNullValue();
			return;
		}

		JsonSerializer.Serialize( writer, GameObjectReference.FromInstance( go ), Json.options );
	}

	/// <summary>
	/// Json Keys used for serialization and deserialization of GameObjects.
	/// Kept here so they are easier to change, and we are less susceptible to typos.
	/// </summary>
	internal static class JsonKeys
	{
		internal const string PrefabInstanceSource = "__Prefab";
		internal const string PrefabInstancePatch = "__PrefabInstancePatch";
		internal const string PrefabIdToInstanceId = "__PrefabIdToInstanceId";
		internal const string PrefabInstanceVariables = "__PrefabVariables"; // Legacy

		internal const string Id = "__guid";
		internal const string Children = "Children";
		internal const string Components = "Components";
		internal const string Flags = "Flags";
		internal const string Name = "Name";
		internal const string Position = "Position";
		internal const string Rotation = "Rotation";
		internal const string Scale = "Scale";
		internal const string Enabled = "Enabled";
		internal const string Tags = "Tags";
		internal const string Version = "__version";
		internal const string NetworkMode = "NetworkMode";
		internal const string NetworkInterpolation = "NetworkInterpolation";
		internal const string NetworkOrphaned = "NetworkOrphaned";
		internal const string AlwaysTransmit = "NetworkTransmit";
		internal const string OwnerTransfer = "OwnerTransfer";

		// Editor only keys used to influence serialization logic when performing editor actions
		internal const string EditorPrefabInstanceNestedSource = "__EditorPrefabNestedInstance";
		internal const string EditorSkipPrefabBreakOnRefresh = "__EditorSkipPrefabBreakOnRefresh";
	}

}
