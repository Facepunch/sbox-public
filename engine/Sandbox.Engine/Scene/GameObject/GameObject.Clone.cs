
using Facepunch.ActionGraphs;
using System.Text.Json.Nodes;

namespace Sandbox;

public partial class GameObject
{
	// Set only during the cloning process
	// We store this on the GameObject to avoid the need reverse lookup table during the clone process
	private GameObject _cloneOriginal = null;
	private bool _isCloningPrefab = false;
	private GameObject _cloneOriginalRoot = null;

	/// <summary>
	/// Create a unique copy of the passed in GameObject
	/// </summary>
	public GameObject Clone( in CloneConfig cloneConfig )
	{
		Assert.NotNull( Game.ActiveScene, "No Active Scene" );

		if ( !this.IsValid() )
		{
			throw new InvalidOperationException( "Attempting to clone invalid GameObject" );
		}

		using var cacheScope = ActionGraph.PushSerializationOptions( new(
			Cache: new ActionGraphCache(),
			WriteCacheReferences: true
		) );

		using var batchGroup = CallbackBatch.Isolated();

		// We also need to clone the dependecies that exist within the original hierarchy
		//
		// For example:
		//
		// OriginalA { OriginalComponentA {}, OriginalComponentB { property pointing to OriginalComponentA } }
		//
		// should result in:
		//
		// CloneA { CloneComponentA {}, CloneComponentB { property pointing to CloneComponentA } }
		//
		// To accomplish that we need to keep track which original object is cloned to which clone object
		// We use this information to rewire the refrences when we are deserializing Component or GameObject properties in Component.PostClone
		Dictionary<object, object> originalToClonedObject = new( Children.Count * 4 + Components.Count ); // Rough estimate of hierachy size

		// 2 Step process:

		// First:
		// Create all GameObejcts and Components.
		// This ensures we can resolve potential references correctly.
		// Create root Clone
		var clone = new GameObject( false );

		// TODO, this is here for legacy support yeet at some point
		JsonObject prefabVariablesOverride = null;
#pragma warning disable CS0612
		if ( cloneConfig.PrefabVariables is not null && cloneConfig.PrefabVariables.Count > 0 )
		{
			prefabVariablesOverride = Json.ToNode( cloneConfig.PrefabVariables ).AsObject();
		}
#pragma warning restore CS0612


		var cloneTransform = cloneConfig.Transform;
		// if we are cloning a prefab preserve the root transform
		if ( this is PrefabScene )
		{
			cloneTransform = cloneTransform.WithRotation( cloneConfig.Transform.Rotation * LocalRotation );
			cloneTransform = cloneTransform.WithScale( cloneConfig.Transform.Scale * LocalScale );
			cloneTransform = cloneTransform.WithPosition( cloneConfig.Transform.Position + LocalPosition );
		}
		else
		{
			// The reason why we only keep the scale of the original is historical.
			cloneTransform = cloneConfig.Transform.WithScale( cloneConfig.Transform.Scale * LocalScale );
		}

		// Initialize root clone and hierachy
		clone.InitClone( this, cloneTransform, enabled: false, originalToClonedObject, isCloningPrefab: this is PrefabScene, this );

		Dictionary<Guid, Guid> originalIdToCloneId = new( originalToClonedObject.Count );
		foreach ( var (original, cloned) in originalToClonedObject )
		{
			if ( original is GameObject go )
				originalIdToCloneId[go.Id] = (cloned as GameObject).Id;

			if ( original is Component comp )
				originalIdToCloneId[comp.Id] = (cloned as Component).Id;
		}


		// Set config overrides
		if ( cloneConfig.Parent is not null )
		{
			clone.Parent = cloneConfig.Parent;
		}

		if ( cloneConfig.Name is not null )
		{
			clone.Name = cloneConfig.Name;
		}
		else
		{
			clone.Name = Name;
			clone.MakeNameUnique();
		}

		// Not sure if we should do this here, we need to do it because it matches the old behaviour, where the clone is enabled before deserialization is completed.
		// See https://github.com/Facepunch/sbox/issues/1785
		clone.Enabled = cloneConfig.StartEnabled;

		// Second:
		// Copy all component properties from the original to the clone.
		clone.PostClone( originalToClonedObject, originalIdToCloneId );

		// Legacy support for restoring prefab vars
		if ( prefabVariablesOverride is not null && clone.IsPrefabInstanceRoot )
		{
			clone.DeserializePrefabVariables( prefabVariablesOverride );
		}

		return clone;
	}

	private void InitClone( GameObject original, Transform transform, bool enabled, Dictionary<object, object> originalToClonedObject, bool isCloningPrefab, GameObject cloneOriginalRoot )
	{
		originalToClonedObject[original] = this;
		_cloneOriginal = original;
		_isCloningPrefab = isCloningPrefab;
		_cloneOriginalRoot = cloneOriginalRoot;
		Flags = original.Flags;
		Flags |= GameObjectFlags.Deserializing;

		// If we're absolute we want to maintain the world transform relative to our parent, not just use the world transform directly
		if ( Flags.Contains( GameObjectFlags.Absolute ) && Parent != null && original.Parent != null )
		{
			// get the local transform relative to their parent
			var originalLocal = original.Parent.WorldTransform.ToLocal( transform );

			// convert it back to world relative to our parent
			WorldTransform = Parent.WorldTransform.ToWorld( originalLocal );
		}
		else
		{
			LocalTransform = transform;
		}

		Name = original.Name;
		Enabled = enabled;

		NetworkMode = original.NetworkMode;
		NetworkFlags = original.NetworkFlags;
		NetworkOrphaned = original.NetworkOrphaned;
		AlwaysTransmit = original.AlwaysTransmit;
		OwnerTransfer = original.OwnerTransfer;
		Tags.SetFrom( original.Tags );

		if ( original.IsPrefabInstanceRoot || original is PrefabScene )
		{
			var prefabSource = original.PrefabInstance?.PrefabSource;
			if ( original is PrefabScene prefabScene && prefabScene.Source is PrefabFile prefabFile )
			{
				prefabSource = prefabFile.ResourcePath;
			}

			var isNested = original.IsNestedPrefabInstanceRoot || (original.IsOutermostPrefabInstanceRoot && _isCloningPrefab);
			InitPrefabInstance( prefabSource, isNested );
		}

		if ( original.Components.Count > 0 )
		{
			foreach ( var originalComponent in original.Components.GetAll() )
			{
				if ( originalComponent is null ) continue;

				if ( originalComponent.Flags.Contains( ComponentFlags.NotCloned ) ) continue;

				if ( originalComponent is MissingComponent missing )
				{
					var clonedMissingComp = new MissingComponent( missing.GetJson() );
					Components.AddMissing( clonedMissingComp );
					clonedMissingComp.InitClone( originalComponent, originalToClonedObject );

					continue;
				}

				var clonedComp = Components.Create( originalComponent.GetType(), originalComponent.Enabled );
				clonedComp.InitClone( originalComponent, originalToClonedObject );
			}
		}

		if ( original.Children.Any() )
		{
			foreach ( var originalChild in original.Children )
			{
				if ( originalChild is null )
					continue;

				if ( originalChild.Flags.Contains( GameObjectFlags.NotSaved ) )
					continue;

				// Child gameobjects that are being destroyed don't want to be serialized
				if ( originalChild.IsDestroyed )
					continue;

				var clonedChild = new GameObject( this, false );

				clonedChild.InitClone( originalChild, originalChild.LocalTransform, originalChild.Enabled, originalToClonedObject, isCloningPrefab, cloneOriginalRoot );
			}
		}
	}

	/// <summary>
	/// Runs after this clone has been created by a cloned GameObject.
	/// </summary>
	/// <param name="originalToClonedObject">A mapping of original objects to their clones, used for all reference types.</param>
	/// <param name="originalIdToCloneId">A mapping of original GUIDs to cloned GUIDs, used for GameObject and Component references in JSON.</param>
	private void PostClone( Dictionary<object, object> originalToClonedObject, Dictionary<Guid, Guid> originalIdToCloneId )
	{
		// This can happen if setting a component property creates gameobjects.
		// But it really shouldn't, so we print a warning.
		// So far this only happened when we had a bug in CreateBoneObjects/CreateAttachements.
		if ( !_cloneOriginal.IsValid() )
		{
			Log.Warning( "Object created during cloning, which is not linked to an original." );
			return;
		}

		// When cloning prefabs or prefab instances, we need to ensure the prefab lookup references 
		// of the clone point to the correct prefab rather than the prefab instances.
		// There are two main scenarios to handle:

		// Case 1: Cloning a PrefabScene (direct prefab cloning)
		if ( _isCloningPrefab && _cloneOriginal is PrefabScene )
		{
			// When cloning a prefab directly, use the original-to-clone mapping as is
			Dictionary<Guid, Guid> newMapping = originalIdToCloneId;

			// For a new prefab clone, initialize with an empty patch
			var instancePatch = new Json.Patch();
			PrefabInstance.InitLookups( newMapping );
			PrefabInstance.InitPatch( instancePatch );
		}
		// Case 2: Cloning an instance that is a prefab root (but not part of a PrefabScene)
		else if ( _cloneOriginal.IsPrefabInstanceRoot )
		{
			// Create a new mapping based on the original's prefab instance mapping
			var originalMapping = _cloneOriginal.PrefabInstance.InstanceToPrefabLookup;
			var newMapping = new Dictionary<Guid, Guid>( originalMapping.Count );

			// Remap GUIDs to point to the newly cloned instances
			foreach ( var (originalInstanceGuid, originalPrefabGuid) in originalMapping )
			{
				if ( originalIdToCloneId.TryGetValue( originalInstanceGuid, out var clonedInstanceGuid ) )
				{
					newMapping[originalPrefabGuid] = clonedInstanceGuid;
				}
			}

			PrefabInstance.InitLookups( newMapping );

			// Copy the existing patch from the original instance
			if ( _cloneOriginal.IsOutermostPrefabInstanceRoot )
			{
				var instancePatch = _cloneOriginal.PrefabInstance.Patch;
				PrefabInstance.InitPatch( instancePatch );
			}
		}

		// when cloning part of an isntance we may need to convert some instances to a full prefab instance
		var isCloningPartOfPrefabInstance = _cloneOriginal.IsPrefabInstance && !_cloneOriginal.IsOutermostPrefabInstanceRoot && !_isCloningPrefab;
		if ( _cloneOriginalRoot == _cloneOriginal && isCloningPartOfPrefabInstance )
		{
			if ( IsNestedPrefabInstanceRoot )
			{
				PrefabInstance.ConvertNestedToFullPrefabInstance();
			}
			else
			{
				PrefabInstanceData.ConvertTopLevelNestedToFullPrefabInstances( this );
			}
		}

		if ( Components.Count > 0 )
		{
			Components.ForEach( "PostClone", true, c => c.PostClone( originalToClonedObject, originalIdToCloneId ) );
		}

		if ( Children.Any() )
		{
			// Need to do numeric iteration because the collection can change (e.g. PropComponent adds a several new components)
			ForEachChild( "PostClone", true, c =>
			{
				// should never happen
				if ( c.IsDestroyed )
					throw new InvalidOperationException( "Cloned GameObject was destroyed before cloning was completed" );
				c.PostClone( originalToClonedObject, originalIdToCloneId );
			} );
		}

		// Kill temp ref
		_cloneOriginal = null;
		// Reset flag
		_isCloningPrefab = false;
		Flags &= ~GameObjectFlags.Deserializing;

		Components.ForEach( "OnLoadInternal", true, c => c.OnLoadInternal() );
		Components.ForEach( "OnValidate", true, c => c.Validate() );
	}

	/// <summary>
	/// Clone a prefab from path
	/// </summary>
	public static GameObject Clone( string prefabPath, CloneConfig? config = default )
	{
		var prefabFile = ResourceLibrary.Get<PrefabFile>( prefabPath );
		return Clone( prefabFile, config );
	}

	/// <summary>
	/// Clone a prefab from path
	/// </summary>
	public static GameObject Clone( string prefabPath, Transform transform, GameObject parent = null, bool startEnabled = true, string name = null )
		=> Clone( prefabPath, new CloneConfig( transform, parent, startEnabled, name ) );

	/// <summary>
	/// Clone a prefab from path
	/// </summary>
	public static GameObject Clone( PrefabFile prefabFile, CloneConfig? config = default )
	{
		if ( prefabFile is null ) return null;

		var prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		return prefabScene.Clone( config ?? new CloneConfig( global::Transform.Zero ) );
	}

	/// <summary>
	/// Clone a prefab from path
	/// </summary>
	public static GameObject Clone( PrefabFile prefabFile, Transform transform, GameObject parent = null, bool startEnabled = true, string name = null )
		=> Clone( prefabFile, new CloneConfig( transform, parent, startEnabled, name ) );

	/// <summary>
	/// Create a unique copy of the GameObject
	/// </summary>
	public GameObject Clone( Transform transform, GameObject parent = null, bool startEnabled = true, string name = null )
		=> Clone( new CloneConfig( transform, parent, startEnabled, name ) );

	/// <summary>
	/// Create a unique copy of the GameObject
	/// </summary>
	public GameObject Clone() => Clone( global::Transform.Zero );

	/// <summary>
	/// Create a unique copy of the GameObject
	/// </summary>
	public GameObject Clone( Vector3 position ) => Clone( new Transform( position ) );

	/// <summary>
	/// Create a unique copy of the GameObject
	/// </summary>
	public GameObject Clone( Vector3 position, Rotation rotation ) => Clone( new Transform( position, rotation ) );

	/// <summary>
	/// Create a unique copy of the GameObject
	/// </summary>
	public GameObject Clone( Vector3 position, Rotation rotation, Vector3 scale ) => Clone( new Transform( position, rotation, scale ) );


	/// <summary>
	/// Create a unique copy of the GameObject
	/// </summary>
	public GameObject Clone( GameObject parent, Vector3 position, Rotation rotation, Vector3 scale ) => Clone( new Transform( position, rotation, scale ), parent );

}

/// <summary>
/// The low level input of a GameObject.Clone
/// </summary>
public struct CloneConfig
{
	public bool StartEnabled;
	public Transform Transform;
	public string Name;
	public GameObject Parent;
	[Obsolete]
	public Dictionary<string, object> PrefabVariables;

	public CloneConfig( Transform transform, GameObject parent = null, bool startEnabled = true, string name = null )
	{
		Transform = transform;
		Parent = parent;
		StartEnabled = startEnabled;
		Name = name;
	}
}
