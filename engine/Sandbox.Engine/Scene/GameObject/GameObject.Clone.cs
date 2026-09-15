using Facepunch.ActionGraphs;
using System.Text.Json.Nodes;

namespace Sandbox;

public partial class GameObject
{
	// Set only during the cloning process
	// We store this on the GameObject to avoid the need reverse lookup table during the clone process
	private GameObject _cloneOriginal = null;

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

		// Create the entire hierarchy before copying properties, so references can resolve to clones.
		var context = new CloneContext( new( Children.Count * 4 + Components.Count ), this );
		var clone = new GameObject( false );

		// TODO, this is here for legacy support yeet at some point
		JsonObject prefabVariablesOverride = null;
#pragma warning disable CS0612
		if ( cloneConfig.PrefabVariables is not null && cloneConfig.PrefabVariables.Count > 0 )
		{
			prefabVariablesOverride = Json.ToNode( cloneConfig.PrefabVariables ).AsObject();
		}
#pragma warning restore CS0612


		// All clones inherit scale; prefab roots also preserve their position and rotation.
		var cloneTransform = cloneConfig.Transform.WithScale( cloneConfig.Transform.Scale * LocalScale );
		if ( this is PrefabScene )
		{
			cloneTransform = cloneTransform.WithRotation( cloneConfig.Transform.Rotation * LocalRotation );
			cloneTransform = cloneTransform.WithPosition( cloneConfig.Transform.Position + LocalPosition );
		}
		// Initialize root clone and hierarchy
		clone.InitClone( this, cloneTransform, enabled: false, context );

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
			// Only make name unique in editor, in-game it isn't as important
			// and we want to avoid the overhead & string allocation when generating a new name.
			if ( Scene.IsEditor || (clone.Scene.IsValid() && clone.Scene.IsEditor) ) clone.MakeNameUnique();
		}

		// Not sure if we should do this here, we need to do it because it matches the old behaviour, where the clone is enabled before deserialization is completed.
		// See https://github.com/Facepunch/sbox/issues/1785
		clone.Enabled = cloneConfig.StartEnabled;

		// Restore prefab state, copy properties, then run load/validation callbacks.
		clone.PostClone( context );

		// Legacy support for restoring prefab vars
		if ( prefabVariablesOverride is not null && clone.IsPrefabInstanceRoot )
		{
			clone.DeserializePrefabVariables( prefabVariablesOverride );
		}

		return clone;
	}

	private void InitClone( GameObject original, Transform transform, bool enabled, CloneContext context )
	{
		context.OriginalToClone[original] = this;
		_cloneOriginal = original;
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
		Tags.CloneFrom( original.Tags );

		if ( original.IsPrefabInstanceRoot || original is PrefabScene )
		{
			var prefabSource = original.PrefabInstance?.PrefabSource;
			if ( original is PrefabScene prefabScene && prefabScene.Source is PrefabFile prefabFile )
			{
				prefabSource = prefabFile.ResourcePath;
			}

			var isNested = original.IsNestedPrefabInstanceRoot || (original.IsOutermostPrefabInstanceRoot && context.IsCloningPrefab);
			InitPrefabInstance( prefabSource, isNested );
		}

		var componentCount = original.Components.Count;
		for ( int i = 0; i < componentCount; i++ )
		{
			var originalComponent = original.Components[i];
			if ( originalComponent is null ) continue;

			if ( originalComponent.Flags.Contains( ComponentFlags.NotCloned ) ) continue;

			Component clonedComp;
			if ( originalComponent is MissingComponent missing )
			{
				var clonedMissingComp = new MissingComponent( missing.GetJson() );
				Components.AddMissing( clonedMissingComp );
				clonedComp = clonedMissingComp;
			}
			else
			{
				clonedComp = Components.Create( originalComponent.GetType(), originalComponent.Enabled );
			}
			clonedComp.InitClone( originalComponent, context );
		}

		var childCount = original.Children.Count;
		for ( int i = 0; i < childCount; i++ )
		{
			var originalChild = original.Children[i];
			if ( originalChild is null )
				continue;

			if ( originalChild.Flags.Contains( GameObjectFlags.NotSaved ) )
				continue;

			// Child gameobjects that are being destroyed don't want to be serialized
			if ( originalChild.IsDestroyed )
				continue;

			var clonedChild = new GameObject( this, false );

			clonedChild.InitClone( originalChild, originalChild.LocalTransform, originalChild.Enabled, context );
		}
	}

	/// <summary>
	/// Runs after this clone has been created by a cloned GameObject.
	/// </summary>
	private void PostClone( CloneContext context )
	{
		// This can happen if setting a component property creates gameobjects.
		// But it really shouldn't, so we print a warning.
		// So far this only happened when we had a bug in CreateBoneObjects/CreateAttachements.
		if ( !_cloneOriginal.IsValid() )
		{
			Log.Warning( "Object created during cloning, which is not linked to an original." );
			return;
		}

		PostClonePrefab( context );

		if ( Components.Count > 0 )
		{
			// Action graph delegates deserialized by the JSON fallback bind to this GameObject, push once for all components.
			using var targetScope = ActionGraph.PushTarget( InputDefinition.Target( typeof( GameObject ), this ) );
			Components.ForEach( "PostClone", true, c => c.PostClone( context ) );
		}

		if ( Children.Count > 0 )
		{
			// Need to do numeric iteration because the collection can change (e.g. PropComponent adds a several new components)
			ForEachChild( "PostClone", true, c =>
			{
				// should never happen
				if ( c.IsDestroyed )
					throw new InvalidOperationException( "Cloned GameObject was destroyed before cloning was completed" );
				c.PostClone( context );
			} );
		}

		// Kill temp ref
		_cloneOriginal = null;
		Flags &= ~GameObjectFlags.Deserializing;

		Components.ForEach( "OnLoadInternal", true, c => c.OnLoadInternal() );
		Components.ForEach( "OnValidate", true, c => c.Validate() );
	}

	/// <summary>
	/// Restore prefab mappings and patches before copying component properties.
	/// </summary>
	private void PostClonePrefab( CloneContext context )
	{
		// A prefab uses its own GUIDs; an instance must translate through its prefab mappings.
		if ( context.IsCloningPrefab && _cloneOriginal is PrefabScene )
		{
			PrefabInstance.InitLookups( context.OriginalIdToCloneId );
			PrefabInstance.InitPatch( new Json.Patch() );
		}
		// Case 2: Cloning an instance that is a prefab root (but not part of a PrefabScene)
		else if ( _cloneOriginal.IsPrefabInstanceRoot )
		{
			// Create a new mapping based on the original's prefab instance mapping
			var originalMapping = _cloneOriginal.PrefabInstance.InstanceToPrefabLookup;
			var newMapping = new Dictionary<Guid, Guid>( originalMapping.Count );
			var originalIdToCloneId = context.OriginalIdToCloneId;

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

		// when cloning part of an instance we may need to convert some instances to a full prefab instance
		var isCloningPartOfPrefabInstance = _cloneOriginal.IsPrefabInstance && !_cloneOriginal.IsOutermostPrefabInstanceRoot && !context.IsCloningPrefab;
		if ( context.Root == _cloneOriginal && isCloningPartOfPrefabInstance )
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
