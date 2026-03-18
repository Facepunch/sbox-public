namespace Sandbox;

public partial class GameObject
{
	/// <summary>
	/// We are cloned from a prefab. Stop that.
	/// </summary>
	public void BreakFromPrefab()
	{
		if ( !IsOutermostPrefabInstanceRoot )
			return;

		PrefabInstanceData.ConvertTopLevelNestedToFullPrefabInstances( this );

		ClearPrefabInstance();
	}

	public void UpdateFromPrefab()
	{
		if ( !IsPrefabInstance )
			return;

		if ( !IsPrefabInstanceRoot )
		{
			Clear();
			return;
		}

		PrefabInstance.UpdateGameObjectFromPrefab( this );
	}

	/// <summary>
	/// Get the GameObject of a prefab from file path
	/// </summary>
	public static GameObject GetPrefab( string prefabFilePath )
	{
		var prefabFile = ResourceLibrary.Get<PrefabFile>( prefabFilePath );
		if ( prefabFile is null ) return default;

		return SceneUtility.GetPrefabScene( prefabFile );
	}

	public string PrefabInstanceSource
	{
		get
		{
			return PrefabInstance?.PrefabSource;
		}
	}

	/// <summary>
	/// This GameObject is part of a prefab instance.
	/// </summary>
	public bool IsPrefabInstance
	{
		get
		{
			if ( IsPrefabInstanceRoot ) return true;
			if ( Parent is null ) return false;

			return Parent.IsPrefabInstance;
		}
	}

	/// <summary>
	/// This GameObject is the root of a prefab instance.
	/// Returns true for regular instance roots and nested prefab instance roots.
	/// </summary>
	public bool IsPrefabInstanceRoot => _prefabInstanceData is not null && !IsPrefabCacheSceneRoot;

	/// <summary>
	/// Get the root of the prefab instance this GameObject is part of.
	/// </summary>
	internal GameObject PrefabInstanceRoot
	{
		get
		{
			return IsPrefabInstanceRoot ? this : Parent?.PrefabInstanceRoot;
		}
	}

	/// <summary>
	/// Get the outermost prefab instance root of this GameObject.
	/// This is the root of the prefab instance this GameObject is part of, or the root of the prefab instance this GameObject is nested in.
	/// </summary>
	internal GameObject OutermostPrefabInstanceRoot
	{
		get
		{
			Assert.True( IsPrefabInstance );

			if ( Parent == null ) return this;

			// Case 1: GameObject is inside a prefab but isn't the root
			if ( IsPrefabInstance && !IsPrefabInstanceRoot ) return Parent.OutermostPrefabInstanceRoot;

			// Case 2: GameObject is a prefab root but is nested inside another prefab
			if ( IsPrefabInstanceRoot && Parent.IsPrefabInstance && IsNestedPrefabInstanceRoot ) return Parent.OutermostPrefabInstanceRoot;

			// Otherwise, we've found the outermost prefab instance root
			return this;
		}
	}

	/// <summary>
	/// This GameObject is the root of a prefab instance and is not nested inside another prefab instance.
	/// </summary>
	internal bool IsOutermostPrefabInstanceRoot => IsPrefabInstanceRoot && OutermostPrefabInstanceRoot == this;

	/// <summary>
	/// This GameObject is the root of a nested prefab instance.
	/// </summary>
	internal bool IsNestedPrefabInstanceRoot => IsPrefabInstanceRoot && PrefabInstance.IsNested;

	/// <summary>
	/// Is this PrefabRoot the root of a prefab cache scene?
	/// </summary>
	internal bool IsPrefabCacheSceneRoot => this is PrefabCacheScene;

	/// <summary>
	/// The filename of the map this object is defined in.
	/// </summary>
	private string MapSource { get; set; }

	// This should never have been public (maybe we did it for easy access from the editor?),
	// It is super easy to mess stuff up when using this incorrectly, even worse now with the new prefab system.
	[Obsolete( "Stop using this, you will most likely mess something up." )]
	public void SetPrefabSource( string prefabSource )
	{
		InitPrefabInstance( prefabSource, false );
	}

	/// <summary>
	/// Initializes the instance data.
	/// </summary>
	internal void InitPrefabInstance( string prefabSource, bool isNested )
	{
		if ( string.IsNullOrEmpty( prefabSource ) )
		{
			_prefabInstanceData = null;
			return;
		}

		// Added 12th Dec 2023
		prefabSource = prefabSource.Replace( ".object", ".prefab", StringComparison.OrdinalIgnoreCase );

		_prefabInstanceData = new PrefabInstanceData( prefabSource, this, isNested );
	}

	internal void ClearPrefabInstance()
	{
		_prefabInstanceData = null;
	}

	internal void SetMapSource( string mapSource )
	{
		MapSource = mapSource;
	}

	internal bool IsMapInstanceRoot => MapSource is not null;

	/// <summary>
	/// Access point for all prefab instance related data.
	/// Can be accessed on both instance root and children contained within the instance.
	/// For outermost prefab instances this will contain a patch and guid mappings.
	/// For nested prefab instances this will just contain the prefab source.
	/// </summary>
	internal PrefabInstanceData PrefabInstance
	{
		get
		{
			return IsPrefabInstanceRoot ? _prefabInstanceData : Parent?.PrefabInstance;
		}
	}
	PrefabInstanceData _prefabInstanceData = null;

	/// <summary>
	/// Defines objects within a scene hierarchy we want to track for prefab diffing and patching.
	/// </summary>
	public static HashSet<Json.TrackedObjectDefinition> DiffObjectDefinitions =
	[
		Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
			type: "GameObject",
			requiredFields: [JsonKeys.Id, JsonKeys.Children, JsonKeys.Components, JsonKeys.Flags],
			idProperty: JsonKeys.Id,
			parentType: "GameObject",
			allowedAsRoot: true,
			ignoredProperties: [ JsonKeys.EditorPrefabInstanceNestedSource, JsonKeys.EditorSkipPrefabBreakOnRefresh ]
		),
		// Prefab Instances are GameObjects as well but have a different structure (children and components array can be omitted)
		Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
			type: "GameObject",
			requiredFields: [JsonKeys.Id, JsonKeys.PrefabInstanceSource],
			idProperty: JsonKeys.Id,
			parentType: "GameObject",
			allowedAsRoot: false,
			atomic: true
		),
		Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
			type: "Component",
			requiredFields: [Component.JsonKeys.Id, Component.JsonKeys.Type, Component.JsonKeys.Enabled],
			idProperty: Component.JsonKeys.Id,
			parentType: "GameObject",
			allowedAsRoot: true
		),
	];
}
