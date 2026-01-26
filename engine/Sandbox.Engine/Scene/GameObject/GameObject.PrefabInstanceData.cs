using Sandbox;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using static Sandbox.GameObject;

internal class PrefabInstanceData
{
	/// <summary>
	/// Gets the cached patch representing differences between this instance and its source prefab.
	/// No guarantee this is up to date, except when in editor, calculating a patch is fairly expensive so we only do it when serializing or editing.
	/// </summary>
	public Json.Patch Patch => _patch;
	private Json.Patch _patch = new();

	private GameObject _instanceRoot;

	private Dictionary<Guid, Guid> _instanceGuidToPrefabGuid = new();
	private Dictionary<Guid, Guid> _prefabGuidToInstanceGuid = new();

	/// <summary>
	/// Translate from this instances guids to prefab guids
	/// </summary>
	public ReadOnlyDictionary<Guid, Guid> InstanceToPrefabLookup => _instanceGuidToPrefabGuid.AsReadOnly();
	/// <summary>
	/// Translate from prefab guids to this instances guids
	/// </summary>
	public ReadOnlyDictionary<Guid, Guid> PrefabToInstanceLookup => _prefabGuidToInstanceGuid.AsReadOnly();

	/// <summary>
	/// A prefab instance that is nested only contains the prefab source and lookup data, no patch.
	/// </summary>
	public bool IsNested => _isNested;
	private bool _isNested = false;

	/// <summary>
	/// The filename of the prefab this object is defined in.
	/// </summary>
	public string PrefabSource { get; private set; }

	public PrefabInstanceData( string prefabSource, GameObject prefabInstanceRoot, bool isNested )
	{
		_instanceRoot = prefabInstanceRoot;
		PrefabSource = prefabSource;
		_isNested = isNested;
	}

	/// <summary>
	/// Initialize lookups for this prefab instance.
	/// </summary>
	/// <param name="prefabToInstance">Mapping from prefab GUIDs to instance GUIDs</param>
	public void InitLookups( Dictionary<Guid, Guid> prefabToInstance )
	{
		var validatedLookup = ValidatePrefabToInstanceIdLookup( prefabToInstance, PrefabSource );
		UpdateLookups( validatedLookup );
	}

	public void InitMappingsForNestedInstance( Guid id )
	{
		Assert.True( IsNested, "This method should only be called on a nested prefab instance." );


		var prefabGameObject = _instanceRoot.OutermostPrefabInstanceRoot.PrefabInstance.FindPrefabGameObjectForInstanceId( id );
		var instanceToPrefabMapping = new Dictionary<Guid, Guid>( _instanceRoot.OutermostPrefabInstanceRoot.PrefabInstance._instanceGuidToPrefabGuid );

		// build a mapping all the way back to the original prefab
		while ( prefabGameObject is not PrefabCacheScene )
		{
			// extend mapping
			foreach ( var (instanceId, prefabId) in instanceToPrefabMapping )
			{
				if ( prefabGameObject.OutermostPrefabInstanceRoot.PrefabInstance._instanceGuidToPrefabGuid.TryGetValue( prefabId, out var outerPrefabId ) )
				{
					instanceToPrefabMapping[instanceId] = outerPrefabId;
				}
				else
				{
					instanceToPrefabMapping.Remove( instanceId );
				}
			}

			// step up the hierarchy
			prefabGameObject = prefabGameObject.OutermostPrefabInstanceRoot.PrefabInstance.FindPrefabGameObjectForInstanceId( prefabGameObject.Id );
		}

		instanceToPrefabMapping = AddNewObjectsToInstanceToPrefabLookup( _instanceRoot, instanceToPrefabMapping );
		// invert mapping:
		var prefabToInstanceMapping = new Dictionary<Guid, Guid>( instanceToPrefabMapping.Count );
		foreach ( var (instanceId, prefabId) in instanceToPrefabMapping )
		{
			prefabToInstanceMapping[prefabId] = instanceId;
		}

		InitLookups( prefabToInstanceMapping );
	}

	/// <summary>
	/// Initialize patch data for this prefab instance.
	/// </summary>
	/// <param name="patch">Existing patch to use</param>
	public void InitPatch( Json.Patch patch )
	{
		_patch = patch;
	}

	private static readonly SerializeOptions _serializeOptions = new();

	/// <summary>
	/// Updates the cached patch that represents differences between this instance and its source prefab.
	/// </summary>
	public void RefreshPatch()
	{

		var instanceData = _instanceRoot.SerializeStandard( _serializeOptions );

		RemapInstanceIdsToPrefabIds( ref instanceData );

		var prefabScene = (PrefabCacheScene)GetPrefab( PrefabSource );
		Assert.IsValid( prefabScene );

		var fullPrefabData = prefabScene.FullPrefabInstanceJson;

		var patch = Json.CalculateDifferences( fullPrefabData, instanceData, DiffObjectDefinitions );

		_patch = patch;
	}

	/// <summary>
	/// Clear Patch for this instance, can be used to revert back to the original state.
	/// </summary>
	public void ClearPatch( bool keepBasicGoOverridesOnRoot )
	{
		var newPatch = new Json.Patch();
		if ( keepBasicGoOverridesOnRoot )
		{
			var rootPrefabId = _instanceGuidToPrefabGuid[_instanceRoot.Id];
			newPatch.PropertyOverrides.AddRange( _patch.PropertyOverrides.Where( x => Guid.Parse( x.Target.IdValue ) == rootPrefabId && _ignoredProperties.Contains( x.Property ) ) );
		}

		_patch = newPatch;
	}

	/// <summary>
	/// Returns true if this GameObject has been added to a prefab instance.
	/// </summary>
	public bool IsAddedGameObject( GameObject obj )
	{
		if ( _patch.AddedObjects.Exists( x => Guid.Parse( x.Id.IdValue ) == obj.Id ) )
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Returns true if this Component has been added to a prefab instance.
	/// </summary>
	public bool IsAddedComponent( Component obj )
	{
		if ( _patch.AddedObjects.Exists( x => Guid.Parse( x.Id.IdValue ) == obj.Id ) )
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Returns true if the property of the owner was overridden by the prefab instance.
	/// </summary>
	public bool IsPropertyOverridden( object owner, string propertyName, bool ignoreBasicGoOverrides = false )
	{
		// always ignore version
		if ( propertyName == JsonKeys.Version ) return false;

		var targetId = FindPrefabIdForInstanceObject( owner );

		// If it doesn't exist in prefab it's an added object, so return false
		if ( targetId == Guid.Empty ) return false;

		if ( owner is GameTransform gt )
		{
			propertyName = RemapTransformPropertyName( propertyName );
		}

		if ( ignoreBasicGoOverrides && ShouldIgnoreBasicOverride( targetId, propertyName ) ) return false;

		return _patch.PropertyOverrides.Exists( x => Guid.Parse( x.Target.IdValue ) == targetId && x.Property == propertyName );
	}

	/// <summary>
	/// Returns true if the prefab instance was modified.
	/// 1. If any property was changed on any descendant
	/// 2. If any GameObject/Component was added
	/// 3. If any GameObject/Component was moved
	/// 4. If any GameObject/Component was removed
	/// </summary>
	public bool IsModified()
	{
		var hasPropertyOverride = _patch.PropertyOverrides.Exists( x =>
		{
			var propertyName = x.Property;
			if ( ShouldIgnoreBasicOverride( Guid.Parse( x.Target.IdValue ), propertyName ) )
			{
				return false;
			}

			return true;
		} );

		return hasPropertyOverride || _patch.AddedObjects.Count > 0 || _patch.MovedObjects.Count > 0 || _patch.RemovedObjects.Count > 0;
	}

	/// <summary>
	/// Some properties are always overridden by a prefab instance, we may want to ignore them so they don't show as modified in the UI.
	/// </summary>
	private static readonly HashSet<string> _ignoredProperties =
	[
		JsonKeys.Position,
			JsonKeys.Rotation,
			JsonKeys.Scale,
			JsonKeys.Name,
			JsonKeys.Flags,
			JsonKeys.Enabled
	];

	/// <summary>
	/// Determines if a basic GameObject override should be ignored.
	/// </summary>
	private bool ShouldIgnoreBasicOverride( Guid propertyPrefabTargetId, string propertyName )
	{
		// We only want to ignore these basic overrides for overrides targeting the root
		if ( !_instanceRoot.IsOutermostPrefabInstanceRoot ) return false;

		propertyName = RemapTransformPropertyName( propertyName );
		return _ignoredProperties.Contains( propertyName );
	}

	/// <summary>
	/// Returns true if:
	/// 1. Any property of the GameObject or descendant has been modified
	/// 2. If any GameObject/Component was added/removed to the GameObject
	/// </summary>
	public bool IsGameObjectModified( GameObject obj, bool ignoreBasicGoOverrides = false )
	{
		var targetId = FindPrefabIdForInstanceObject( obj );
		if ( targetId == Guid.Empty ) return false;

		// Check for property overrides (either on this GameObject or its components)
		bool hasPropertyOverride = _patch.PropertyOverrides.Exists( x =>
		{
			if ( IsPropertyOverridden( obj, x.Property, ignoreBasicGoOverrides ) ) return true;

			var targetInstanceGuid = _prefabGuidToInstanceGuid.GetValueOrDefault( Guid.Parse( x.Target.IdValue ) );
			var component = obj.Scene.Directory.FindComponentByGuid( targetInstanceGuid );
			return component != null && component.GameObject == obj;
		} );
		if ( hasPropertyOverride ) return true;

		// Check for added children or components
		bool hasAddedObject = _patch.AddedObjects.Exists( x => Guid.Parse( x.Parent.IdValue ) == targetId );
		if ( hasAddedObject ) return true;

		// Check for removed children or components
		var prefabScene = GameObject.GetPrefab( PrefabSource );
		bool hasRemovedObject = _patch.RemovedObjects.Exists( x =>
		{
			var removedId = Guid.Parse( x.Id.IdValue );

			var prefabGameObj = prefabScene.Scene.Directory.FindByGuid( removedId );
			var isGameObjectRemoved = prefabGameObj != null && prefabGameObj.Parent?.Id == targetId;
			if ( isGameObjectRemoved )
				return true;

			var prefabComponent = prefabScene.Scene.Directory.FindComponentByGuid( removedId );
			var isComponentRemoved = prefabComponent != null && prefabComponent.GameObject?.Id == targetId;
			return isComponentRemoved;
		} );
		return hasRemovedObject;
	}

	/// <summary>
	/// Returns true if any property of the component was changed on the instance.
	/// </summary>
	public bool IsComponentModified( Component comp )
	{
		var targetId = FindPrefabIdForInstanceObject( comp );
		// If target doesn't exists this means this is an added object with no equivalent in the prefab
		// therefore there can't be any property overrides.
		if ( targetId == Guid.Empty ) return false;

		if ( _patch.PropertyOverrides.Exists( x => IsPropertyOverridden( comp, x.Property ) ) )
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Reverts the properties value back to the prefab's value.
	/// </summary>
	public void RevertPropertyChange( object owner, string propertyName )
	{
		var targetId = FindPrefabIdForInstanceObject( owner );
		if ( targetId == Guid.Empty ) return;

		if ( owner is GameTransform gt )
		{
			propertyName = RemapTransformPropertyName( propertyName );
		}
		Patch.PropertyOverrides.RemoveAll( x => Guid.Parse( x.Target.IdValue ) == targetId && x.Property == propertyName );

		if ( owner is GameObject go )
		{
			UpdateGameObjectFromPrefab( go );
		}
		else if ( owner is Component comp )
		{
			UpdateComponentFromPrefab( comp );
		}
		else if ( owner is GameTransform gt1 )
		{
			UpdateGameObjectFromPrefab( gt1.GameObject );
		}
	}

	/// <summary>
	/// Helper function used when Discarding/Reverting.
	/// </summary>
	private void UpdateComponentFromPrefab( Component comp )
	{
		var prefabComponent = FindPrefabComponentForInstanceId( comp.Id );

		var prefabComponentJson = prefabComponent.Serialize();

		var updatedInstanceData = Json.ApplyPatch( prefabComponentJson.AsObject(), Patch, DiffObjectDefinitions );

		RemapPrefabIdsToInstanceIds( ref updatedInstanceData );

		comp.DeserializeImmediately( updatedInstanceData.AsObject() );
	}

	/// <summary>
	/// Reverts all the components properties back to the prefab's value.
	/// </summary>
	public void RevertComponentChanges( Component comp )
	{
		var targetId = FindPrefabIdForInstanceObject( comp );

		Patch.PropertyOverrides.RemoveAll( x => Guid.Parse( x.Target.IdValue ) == targetId );

		UpdateComponentFromPrefab( comp );
	}

	/// <summary>
	/// Helper function used when Discarding/Reverting.
	/// </summary>
	public void UpdateGameObjectFromPrefab( GameObject go, bool revertChanges = false )
	{
		var prefabGameObject = FindPrefabGameObjectForInstanceId( go.Id );
		var prefabGameObjectJson = prefabGameObject.Serialize( new SerializeOptions { SerializePrefabForDiff = true } );
		var validatedLookup = ValidatePrefabToInstanceIdLookup( _prefabGuidToInstanceGuid, PrefabSource );
		UpdateLookups( validatedLookup );

		// Reapply our patch
		var updatedInstanceData = revertChanges ? prefabGameObjectJson : Json.ApplyPatch( prefabGameObjectJson, Patch, DiffObjectDefinitions );
		RemapPrefabIdsToInstanceIds( ref updatedInstanceData );
		if ( go.IsOutermostPrefabInstanceRoot )
		{
			updatedInstanceData[JsonKeys.EditorSkipPrefabBreakOnRefresh] = true;
		}
		go.Deserialize( updatedInstanceData, new DeserializeOptions { IsRefreshing = true } );
		go.OutermostPrefabInstanceRoot.PrefabInstance.RefreshPatch();
	}

	/// <summary>
	/// Reverts all the changes made to the GameObject and descendants (Additions, Removals, PropertyOverrides) back to the prefab's value.
	/// </summary>
	public void RevertGameObjectChanges( GameObject go )
	{
		Assert.True( go.IsPrefabInstance );

		UpdateGameObjectFromPrefab( go, true );
	}

	/// <summary>
	/// Applies the changes made to the property back to the prefab.
	/// </summary>
	public void ApplyPropertyChangeToPrefab( object owner, string propertyName )
	{
		var targetId = FindPrefabIdForInstanceObject( owner );
		var goId = owner is Component comp ? comp.GameObject.Id : FindInstanceId( owner );

		if ( owner is GameTransform gt )
		{
			propertyName = RemapTransformPropertyName( propertyName );
		}

		if ( targetId == Guid.Empty ) return;

		// Mini patch to apply single property change back to prefab
		var applyChangesPatch = new Json.Patch();
		var instancePropOverride = Patch.PropertyOverrides
			.Where( x => Guid.Parse( x.Target.IdValue ) == targetId && x.Property == propertyName )
			.FirstOrDefault();

		Assert.True( instancePropOverride != default );

		RemapInstanceIdsToPrefabIds( ref instancePropOverride.Value );

		applyChangesPatch.PropertyOverrides.Add( instancePropOverride );

		// To apply a patch and to cover both go and component overrides, we need to serialize the full go
		var prefabGameObject = FindPrefabGameObjectForInstanceId( goId );

		var prefabGameObjectJson = prefabGameObject.Serialize( new SerializeOptions { SerializePrefabForDiff = true } );

		var updatedPrefabGameObjectJson = Json.ApplyPatch( prefabGameObjectJson, applyChangesPatch, DiffObjectDefinitions );

		prefabGameObject.Deserialize( updatedPrefabGameObjectJson, new GameObject.DeserializeOptions { IsRefreshing = true } );

		Assert.True( prefabGameObject.Scene is PrefabCacheScene );

		var prefabScene = (PrefabCacheScene)prefabGameObject.Scene;
		prefabScene.ToPrefabFile();

		RefreshPatch();
	}

	/// <summary>
	/// Applies the changes made to the component back to the prefab.
	/// </summary>
	public void ApplyComponentChangesToPrefab( Component comp )
	{
		var instanceObjectJson = comp.Serialize();

		RemapGuids( instanceObjectJson, _instanceGuidToPrefabGuid );

		var prefabComponent = FindPrefabComponentForInstanceId( comp.Id );

		prefabComponent.DeserializeImmediately( instanceObjectJson.AsObject() );

		var prefabFile = ResourceLibrary.Get<PrefabFile>( PrefabSource );

		Assert.True( prefabComponent.Scene is PrefabCacheScene );

		var prefabScene = (PrefabCacheScene)prefabComponent.Scene;
		prefabScene.ToPrefabFile();

		RefreshPatch();
	}


	/// <summary>
	/// Prepares instance JSON data for application to a prefab by updating GUID mappings 
	/// and translating instance GUIDs to their prefab equivalents.
	/// </summary>
	private JsonObject PrepareInstanceJsonForPrefabUpdate( GameObject instanceGameObject )
	{
		var instanceGameObjectJson = instanceGameObject.SerializeStandard( new SerializeOptions { SerializeForPrefabInstanceToPrefabUpdate = true } );

		PrepareLookupsForPrefabUpdate( instanceGameObject );

		// Remap all GUIDs in the JSON to their prefab equivalents
		RemapGuids( instanceGameObjectJson, _instanceGuidToPrefabGuid, remapAddedPrefabInstances: true );

		return instanceGameObjectJson;
	}

	private void PrepareLookupsForPrefabUpdate( GameObject instanceGameObject )
	{
		// Update the instance-to-prefab mapping with any new objects
		_instanceGuidToPrefabGuid = AddNewObjectsToInstanceToPrefabLookup( instanceGameObject, _instanceGuidToPrefabGuid );

		// Ensure prefab-to-instance mapping is up to date
		foreach ( var (instanceGuid, prefabGuid) in _instanceGuidToPrefabGuid )
		{
			if ( !_prefabGuidToInstanceGuid.ContainsKey( prefabGuid ) )
			{
				_prefabGuidToInstanceGuid[prefabGuid] = instanceGuid;
			}
		}

	}

	/// <summary>
	/// Applies the changes made to the GameObject and descendants back to the prefab.
	/// </summary>
	public void ApplyGameObjectChangesToPrefab( GameObject go )
	{
		var prefabGameObject = FindPrefabGameObjectForInstanceId( go.Id );

		var instanceGameObjectJson = PrepareInstanceJsonForPrefabUpdate( go );

		prefabGameObject.Deserialize( instanceGameObjectJson, new DeserializeOptions { IsRefreshing = true } );

		if ( prefabGameObject.IsPrefabInstance )
		{
			prefabGameObject.OutermostPrefabInstanceRoot.PrefabInstance.RefreshPatch();
		}

		Assert.True( prefabGameObject.Scene is PrefabCacheScene );

		var prefabScene = (PrefabCacheScene)prefabGameObject.Scene;
		prefabScene.ToPrefabFile();

		PrefabInstanceData.ConvertAllPrefabInstancesToNested( go );

		RefreshPatch();
	}

	/// <summary>
	/// Adds an added GameObject or PrefabInstance from the instance to the prefab.
	/// </summary>
	public void AddGameObjectToPrefab( GameObject go )
	{
		Assert.True( IsAddedGameObject( go ), "GameObject must be an added object in the prefab instance." );
		Assert.True( go.Parent != null, "GameObject must have a parent." );

		JsonObject instanceGameObjectJson;

		// If the go we are trying to add to the prefab is an added prefab instance
		// we need to serialize it differently
		if ( go.IsOutermostPrefabInstanceRoot )
		{
			instanceGameObjectJson = go.Serialize();

			PrepareLookupsForPrefabUpdate( go.Parent );

			// Remap all GUIDs in the JSON to their prefab equivalents
			RemapGuids( instanceGameObjectJson, _instanceGuidToPrefabGuid, remapAddedPrefabInstances: true );
		}
		else
		{
			instanceGameObjectJson = PrepareInstanceJsonForPrefabUpdate( go );
		}

		var prefabParentObject = FindPrefabGameObjectForInstanceId( go.Parent.Id );

		var prefabNewGo = prefabParentObject.Scene.CreateObject( go.Enabled );
		prefabNewGo.Deserialize( instanceGameObjectJson );

		var nextSibling = go.GetNextSibling( false );

		// Find the next sibling that is not an added object,
		// we can't use added objects as sibling reference as they don't exist in the prefab
		while ( nextSibling != null && IsAddedGameObject( nextSibling ) )
		{
			nextSibling = nextSibling.GetNextSibling( false );
		}
		if ( nextSibling != null )
		{
			var prefabNextSibling = FindPrefabGameObjectForInstanceId( nextSibling.Id );
			if ( prefabNextSibling != null )
			{
				prefabNextSibling.AddSibling( prefabNewGo, true );
			}
			else
			{
				// Shouldn't be possible to get here, we catch it anyway and make sure nto to corrupt anyhting by reverting the action
				Log.Error( "Something went wrong when applying to prefab, please report what you did." );
				prefabNewGo.DestroyImmediate();
				return;
			}
		}
		else
		{
			prefabNewGo.SetParent( prefabParentObject );
		}

		Assert.True( prefabParentObject.Scene is PrefabCacheScene );

		var prefabScene = (PrefabCacheScene)prefabParentObject.Scene;
		prefabScene.ToPrefabFile();

		PrefabInstanceData.ConvertAllPrefabInstancesToNested( go );

		RefreshPatch();
	}

	/// <summary>
	/// Overrides the prefab definition with the current instance state, making the instance match the prefab exactly.
	/// </summary>
	public void OverridePrefabWithInstance()
	{
		Assert.True( _instanceRoot.IsOutermostPrefabInstanceRoot );

		var prefabGameObject = FindPrefabGameObjectForInstanceId( _instanceRoot.Id );

		var goTransform = _instanceRoot.LocalTransform;

		_instanceRoot.LocalPosition = prefabGameObject.LocalPosition;
		var instanceGameObjectJson = PrepareInstanceJsonForPrefabUpdate( _instanceRoot );
		_instanceRoot.LocalTransform = goTransform;

		prefabGameObject.Deserialize( instanceGameObjectJson, new DeserializeOptions { IsRefreshing = true } );

		if ( prefabGameObject.IsPrefabInstance )
		{
			prefabGameObject.OutermostPrefabInstanceRoot.PrefabInstance.RefreshPatch();
		}

		Assert.True( prefabGameObject.Scene is PrefabCacheScene );

		var prefabScene = (PrefabCacheScene)prefabGameObject.Scene;
		prefabScene.ToPrefabFile();

		// Previously added PrefabInstances are now nested, so convert them
		PrefabInstanceData.ConvertAllPrefabInstancesToNested( _instanceRoot );

		// Patch should be empty now
		ClearPatch( true );

		// Fetch latest data from prefab to ensure everything is up to date
		UpdateGameObjectFromPrefab( _instanceRoot );
	}

	public void RemoveHierarchyFromLookup( GameObject go )
	{
		var gos = GetRequiredInstanceGuids( go );
		var newLookup = new Dictionary<Guid, Guid>( _prefabGuidToInstanceGuid.Count );
		foreach ( var (prefabGuid, instanceGuid) in _prefabGuidToInstanceGuid )
		{
			// Only keep the mapping if the instanceGuid is not in the gos set
			if ( gos.Contains( instanceGuid ) )
			{
				continue;
			}
			newLookup[prefabGuid] = instanceGuid;
		}
		UpdateLookups( newLookup );
	}

	/// <summary>
	/// Creates the bidirectional GUID lookups from the provided mapping.
	/// </summary>
	private void UpdateLookups( Dictionary<Guid, Guid> prefabToInstance )
	{
		_instanceGuidToPrefabGuid.Clear();
		_prefabGuidToInstanceGuid.Clear();
		foreach ( var (prefabGuid, instanceGuid) in prefabToInstance )
		{
			_instanceGuidToPrefabGuid[instanceGuid] = prefabGuid;
			_prefabGuidToInstanceGuid[prefabGuid] = instanceGuid;
		}
	}

	private Component FindPrefabComponentForInstanceId( Guid instanceId )
	{
		var prefabGuid = _instanceGuidToPrefabGuid.GetValueOrDefault( instanceId );
		if ( prefabGuid == Guid.Empty ) return null;

		var prefabScene = GameObject.GetPrefab( PrefabSource );

		return prefabScene.Scene.Directory.FindComponentByGuid( prefabGuid );
	}

	private GameObject FindPrefabGameObjectForInstanceId( Guid instanceId )
	{
		var prefabGuid = _instanceGuidToPrefabGuid.GetValueOrDefault( instanceId );
		if ( prefabGuid == Guid.Empty ) return null;

		var prefabScene = GameObject.GetPrefab( PrefabSource );

		return prefabScene.Scene.Directory.FindByGuid( prefabGuid );
	}

	private Guid FindPrefabIdForInstanceObject( object owner )
	{
		return _instanceGuidToPrefabGuid.GetValueOrDefault( FindInstanceId( owner ), Guid.Empty );
	}

	private Guid FindInstanceId( object owner )
	{
		return owner switch
		{
			GameObject go => go.Id,
			Component comp => comp.Id,
			GameTransform gt => gt.GameObject.Id,
			_ => Guid.Empty
		};
	}

	/// <summary>
	/// We don't serialize the GameTransform, we always serialize Position, Rotation and Scale.
	/// So we always need to use those keys, when handling GameTransform properties.
	/// </summary>
	private string RemapTransformPropertyName( string propertyName )
	{
		if ( propertyName == "LocalPosition" ) return JsonKeys.Position;
		if ( propertyName == "LocalRotation" ) return JsonKeys.Rotation;
		if ( propertyName == "LocalScale" ) return JsonKeys.Scale;

		return propertyName;
	}

	// Helper functions

	/// <summary>
	/// Remap all guids in the json to the new guids in the remap table.
	/// </summary>
	/// <param name="node">Node that will be modified.</param>
	/// <param name="remapTable">The table to use for guid translation.</param>
	/// <param name="remapAddedPrefabInstances">If true, will also remap objects that have their own mapping (added prefab instances).</param>
	private static void RemapGuids( JsonNode node, Dictionary<Guid, Guid> remapTable, bool remapAddedPrefabInstances = false )
	{
		if ( node is JsonObject jsonObject )
		{
			// Important, don't remap objects with their own mapping
			if ( !remapAddedPrefabInstances && node.AsObject().ContainsKey( JsonKeys.PrefabIdToInstanceId ) )
			{
				return;
			}

			foreach ( var (k, v) in jsonObject )
			{
				RemapGuids( v, remapTable, remapAddedPrefabInstances );
			}

			return;
		}

		if ( node is JsonArray array )
		{
			for ( int i = 0; i < array.Count; i++ )
			{
				RemapGuids( array[i], remapTable, remapAddedPrefabInstances );
			}
			return;
		}

		if ( node is JsonValue value )
		{
			if ( !value.TryGetValue<Guid>( out var guid ) ) return;
			if ( !remapTable.TryGetValue( guid, out var updatedGuid ) ) return;

			value.ReplaceWith( updatedGuid );
		}
	}

	public void RemapPrefabIdsToInstanceIds( ref JsonNode node )
	{
		RemapGuids( node, _prefabGuidToInstanceGuid );
	}

	public void RemapPrefabIdsToInstanceIds( ref JsonObject node )
	{
		RemapGuids( node, _prefabGuidToInstanceGuid );
	}

	public void RemapInstanceIdsToPrefabIds( ref JsonNode node )
	{
		RemapGuids( node, _instanceGuidToPrefabGuid );
	}

	public void RemapInstanceIdsToPrefabIds( ref JsonObject node )
	{
		RemapGuids( node, _instanceGuidToPrefabGuid );
	}

	public void ValidatePrefabLookup()
	{
		var validatedLookup = ValidatePrefabToInstanceIdLookup( _prefabGuidToInstanceGuid, PrefabSource );
		UpdateLookups( validatedLookup );
	}

	/// <summary>
	/// Ensures the prefab-to-instance GUID mapping is valid by adding missing entries and removing obsolete ones.
	/// </summary>
	/// <param name="oldPrefabToInstanceLookup">The existing GUID mapping to validate</param>
	/// <param name="prefabSource">The source path of the prefab file</param>
	/// <returns>A validated mapping containing only relevant GUIDs with appropriate instance IDs</returns>
	private Dictionary<Guid, Guid> ValidatePrefabToInstanceIdLookup( Dictionary<Guid, Guid> oldPrefabToInstanceLookup, string prefabSource )
	{
		var newLookup = new Dictionary<Guid, Guid>( oldPrefabToInstanceLookup );
		var prefabFile = ResourceLibrary.Get<PrefabFile>( prefabSource );

		if ( !prefabFile.IsValid() )
		{
			Log.Warning( $"Failed to serialize a prefab instance {_instanceRoot}. Prefab file is not valid, did you delete the Prefab {prefabSource}?" );
			return newLookup;
		}

		var prefabScene = (PrefabCacheScene)SceneUtility.GetPrefabScene( prefabFile );

		if ( !prefabScene.IsValid() )
		{
			Log.Warning( $"Failed to serialize a prefab instance {_instanceRoot}. Prefab scene is not valid, did you delete the Prefab {prefabSource}?" );
			return newLookup;
		}

		// Collect all required GUIDs from the prefab scene
		var requiredGuids = GetRequiredPrefabGuids( prefabScene );
		Assert.True( requiredGuids.Contains( prefabScene.Id ) );

		// Remove obsolete entries
		foreach ( var (prefabGuid, _) in oldPrefabToInstanceLookup )
		{
			if ( !requiredGuids.Contains( prefabGuid ) )
			{
				newLookup.Remove( prefabGuid );
			}
		}

		// Add missing mappings
		foreach ( var requiredObjId in requiredGuids )
		{
			if ( !newLookup.ContainsKey( requiredObjId ) )
			{
				newLookup.Add( requiredObjId, Guid.NewGuid() );
			}
		}

		return newLookup;
	}

	/// <summary>
	/// Gets all required GUIDs from a prefab scene.
	/// </summary>
	private static HashSet<Guid> GetRequiredPrefabGuids( PrefabCacheScene prefabScene )
	{
		// Find all GameObjects
		var requiredGameObjectGuids = prefabScene.Directory.AllGameObjects
			.Where( gameObject => gameObject.IsValid() && !gameObject.Flags.Contains( GameObjectFlags.NotSaved ) )
			.Select( gameObject => gameObject.Id );

		// Find all Components
		var requiredComponentGuids = prefabScene.Directory.AllComponents
			.Where( component => component.IsValid() && !component.Flags.Contains( ComponentFlags.NotSaved ) )
			.Select( component => component.Id );

		return requiredGameObjectGuids
			.Concat( requiredComponentGuids )
			.Append( prefabScene.Id )
			.ToHashSet();
	}

	/// <summary>
	/// Adds new GUID mappings to the instance lookup based on the required GUIDs from the given prefab instance root.
	/// </summary>
	private static Dictionary<Guid, Guid> AddNewObjectsToInstanceToPrefabLookup( GameObject instanceRoot, Dictionary<Guid, Guid> oldInstanceToPrefabLookup )
	{
		var newLookup = new Dictionary<Guid, Guid>( oldInstanceToPrefabLookup );

		// Collect all required GUIDs from the prefab scene
		var requiredGuids = GetRequiredInstanceGuids( instanceRoot );
		Assert.True( requiredGuids.Contains( instanceRoot.Id ) );

		// Add missing mappings
		foreach ( var requiredObjId in requiredGuids )
		{
			if ( !newLookup.ContainsKey( requiredObjId ) )
			{
				newLookup.Add( requiredObjId, Guid.NewGuid() );
			}
		}

		return newLookup;
	}

	/// <summary>
	/// Gets all GUIDs contained inside a go hierarchy
	/// </summary>
	private static HashSet<Guid> GetRequiredInstanceGuids( GameObject go )
	{
		// Find all GameObjects
		var requiredGameObjectGuids = go.GetAllObjects( false )
			.Where( gameObject => !gameObject.Flags.Contains( GameObjectFlags.NotSaved ) )
			.Select( gameObject => gameObject.Id );

		// Find all Components
		var requiredComponentGuids = go.Components.GetAll( FindMode.InSelf | FindMode.InDescendants )
			.Where( component => !component.Flags.Contains( ComponentFlags.NotSaved ) )
			.Select( component => component.Id );

		return requiredGameObjectGuids
			.Concat( requiredComponentGuids )
			.Append( go.Id )
			.ToHashSet();
	}

	public void ConvertNestedToFullPrefabInstance()
	{
		_isNested = false;
		RefreshPatch();
	}

	public static void ConvertTopLevelNestedToFullPrefabInstances( GameObject go )
	{
		foreach ( var child in go.Children )
		{
			if ( child.IsNestedPrefabInstanceRoot )
			{
				child.PrefabInstance.ConvertNestedToFullPrefabInstance();
			}
			else
			{
				ConvertTopLevelNestedToFullPrefabInstances( child );
			}
		}
	}

	public static void BreakAllPrefabInstanceInHierarchy( GameObject go )
	{
		if ( go.IsPrefabInstanceRoot )
		{
			go.ClearPrefabInstance();
		}
		foreach ( var child in go.Children )
		{
			BreakAllPrefabInstanceInHierarchy( child );
		}
	}

	public static void ConvertAllPrefabInstancesToNested( GameObject go )
	{
		if ( go.IsOutermostPrefabInstanceRoot )
		{
			go.PrefabInstance._isNested = true;
		}
		else
		{
			foreach ( var child in go.Children )
			{
				ConvertAllPrefabInstancesToNested( child );
			}
		}
	}
}
