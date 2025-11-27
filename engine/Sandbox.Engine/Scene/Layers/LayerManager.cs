using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// Manages all layers in a scene. Provides layer CRUD operations,
/// membership tracking, and layer state management.
/// </summary>
[Expose]
public sealed class LayerManager : GameObjectSystem<LayerManager>
{
	/// <summary>
	/// The default layer that objects are assigned to when no layer is specified.
	/// </summary>
	public Layer DefaultLayer { get; private set; }

	/// <summary>
	/// All layers in the scene, ordered by SortOrder.
	/// </summary>
	public IReadOnlyList<Layer> All => _layers.OrderBy( l => l.SortOrder ).ToList();

	/// <summary>
	/// All visible layers.
	/// </summary>
	public IReadOnlyList<Layer> Visible => _layers.Where( l => l.Visible ).OrderBy( l => l.SortOrder ).ToList();

	/// <summary>
	/// Event fired when a layer's visibility changes.
	/// </summary>
	public event Action<Layer> OnLayerVisibilityChanged;

	/// <summary>
	/// Event fired when a layer's opacity changes.
	/// </summary>
	public event Action<Layer> OnLayerOpacityChanged;

	/// <summary>
	/// Event fired when a layer is created.
	/// </summary>
	public event Action<Layer> OnLayerCreated;

	/// <summary>
	/// Event fired when a layer is deleted.
	/// </summary>
	public event Action<Layer> OnLayerDeleted;

	/// <summary>
	/// Event fired when a member changes layers.
	/// </summary>
	public event Action<LayerMember, Layer, Layer> OnMemberLayerChanged;

	/// <summary>
	/// Event fired when layers are modified (for serialization tracking).
	/// </summary>
	public event Action OnLayersModified;

	private readonly List<Layer> _layers = new();
	private readonly Dictionary<Guid, Layer> _layerLookup = new();
	private readonly HashSet<LayerMember> _members = new();
	private readonly Dictionary<Guid, HashSet<LayerMember>> _membersByLayer = new();

	private bool _batchUpdateActive = false;
	private bool _batchDirty = false;

	/// <summary>
	/// Named presets for layer visibility states.
	/// </summary>
	private readonly Dictionary<string, LayerSnapshot> _presets = new();

	public LayerManager( Scene scene ) : base( scene )
	{
		// Create default layer
		DefaultLayer = new Layer
		{
			Id = Guid.Empty,
			Name = "Default",
			Color = Color.Gray,
			Manager = this
		};

		_layers.Add( DefaultLayer );
		_layerLookup[DefaultLayer.Id] = DefaultLayer;
		_membersByLayer[DefaultLayer.Id] = new HashSet<LayerMember>();

		// Hook into scene lifecycle
		Listen( Stage.SceneLoaded, 0, OnSceneLoaded, "LayerManager.OnSceneLoaded" );
		Listen( Stage.FinishUpdate, 0, EnsureSceneLayerData, "LayerManager.EnsureSceneLayerData" );
	}

	private bool _sceneLayerDataEnsured = false;

	private void EnsureSceneLayerData()
	{
		// Only check once
		if ( _sceneLayerDataEnsured ) return;
		_sceneLayerDataEnsured = true;

		// Ensure SceneLayerData component exists on the scene for serialization
		if ( Scene is not null && Scene.IsEditor )
		{
			SceneLayerData.GetOrCreate( Scene );
		}
	}

	private void OnSceneLoaded()
	{
		// Load layer data from SceneLayerData component if it exists
		var layerData = SceneLayerData.Get( Scene );
		if ( layerData is not null && !string.IsNullOrEmpty( layerData.LayerDataJson ) )
		{
			// Data will be loaded through the component's OnValidate
		}

		// Re-apply layer states after scene load
		RefreshAllMembers();
	}

	/// <summary>
	/// Gets the LayerManager for the given scene, or creates one if it doesn't exist.
	/// </summary>
	public static LayerManager Get( Scene scene )
	{
		return scene?.GetSystem<LayerManager>();
	}

	/// <summary>
	/// Creates a new layer with the given name.
	/// </summary>
	public Layer CreateLayer( string name = null )
	{
		var layer = new Layer
		{
			Name = name ?? GenerateLayerName(),
			Manager = this,
			SortOrder = _layers.Count
		};

		_layers.Add( layer );
		_layerLookup[layer.Id] = layer;
		_membersByLayer[layer.Id] = new HashSet<LayerMember>();

		OnLayerCreated?.Invoke( layer );
		MarkModified();

		return layer;
	}

	/// <summary>
	/// Creates a new layer group.
	/// </summary>
	public Layer CreateGroup( string name = null )
	{
		var layer = CreateLayer( name ?? "New Group" );
		layer.IsGroup = true;
		return layer;
	}

	/// <summary>
	/// Deletes a layer. Members are moved to the default layer.
	/// </summary>
	public bool DeleteLayer( Layer layer )
	{
		if ( layer is null || layer == DefaultLayer ) return false;
		if ( !_layerLookup.ContainsKey( layer.Id ) ) return false;

		// Move all members to default layer
		if ( _membersByLayer.TryGetValue( layer.Id, out var members ) )
		{
			foreach ( var member in members.ToList() )
			{
				member.Layer = DefaultLayer;
			}
		}

		// Remove child layers if this is a group
		foreach ( var child in GetChildLayers( layer.Id ).ToList() )
		{
			child.ParentLayerId = null;
		}

		_layers.Remove( layer );
		_layerLookup.Remove( layer.Id );
		_membersByLayer.Remove( layer.Id );

		layer.Manager = null;

		OnLayerDeleted?.Invoke( layer );
		MarkModified();

		return true;
	}

	/// <summary>
	/// Gets a layer by ID.
	/// </summary>
	public Layer GetLayer( Guid id )
	{
		return _layerLookup.TryGetValue( id, out var layer ) ? layer : null;
	}

	/// <summary>
	/// Gets a layer by name.
	/// </summary>
	public Layer GetLayer( string name )
	{
		return _layers.FirstOrDefault( l => l.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// Gets all layers with the specified tag.
	/// </summary>
	public IReadOnlyList<Layer> GetByTag( string tag )
	{
		return _layers.Where( l => l.Tags.Contains( tag ) ).ToList();
	}

	/// <summary>
	/// Gets all child layers of a parent layer.
	/// </summary>
	public IEnumerable<Layer> GetChildLayers( Guid parentId )
	{
		return _layers.Where( l => l.ParentLayerId == parentId );
	}

	/// <summary>
	/// Gets all root-level layers (no parent).
	/// </summary>
	public IEnumerable<Layer> GetRootLayers()
	{
		return _layers.Where( l => !l.ParentLayerId.HasValue ).OrderBy( l => l.SortOrder );
	}

	/// <summary>
	/// Gets all members of a layer.
	/// </summary>
	public IReadOnlyList<GameObject> GetLayerMembers( Layer layer )
	{
		if ( layer is null ) return Array.Empty<GameObject>();

		if ( !_membersByLayer.TryGetValue( layer.Id, out var members ) )
			return Array.Empty<GameObject>();

		return members
			.Where( m => m.IsValid && m.GameObject.IsValid() )
			.Select( m => m.GameObject )
			.ToList();
	}

	/// <summary>
	/// Registers a layer member with the manager.
	/// </summary>
	internal void RegisterMember( LayerMember member )
	{
		if ( member is null ) return;

		_members.Add( member );

		var layerId = member.LayerId;
		if ( layerId == Guid.Empty )
			layerId = DefaultLayer.Id;

		if ( !_membersByLayer.TryGetValue( layerId, out var members ) )
		{
			members = new HashSet<LayerMember>();
			_membersByLayer[layerId] = members;
		}

		members.Add( member );
	}

	/// <summary>
	/// Unregisters a layer member from the manager.
	/// </summary>
	internal void UnregisterMember( LayerMember member )
	{
		if ( member is null ) return;

		_members.Remove( member );

		foreach ( var kvp in _membersByLayer )
		{
			kvp.Value.Remove( member );
		}
	}

	/// <summary>
	/// Called when a member changes layers.
	/// </summary>
	internal void OnMemberLayerChangedInternal( LayerMember member, Layer oldLayer, Layer newLayer )
	{
		if ( oldLayer is not null && _membersByLayer.TryGetValue( oldLayer.Id, out var oldMembers ) )
		{
			oldMembers.Remove( member );
		}

		var newLayerId = newLayer?.Id ?? DefaultLayer.Id;
		if ( !_membersByLayer.TryGetValue( newLayerId, out var newMembers ) )
		{
			newMembers = new HashSet<LayerMember>();
			_membersByLayer[newLayerId] = newMembers;
		}
		newMembers.Add( member );

		OnMemberLayerChanged?.Invoke( member, oldLayer, newLayer );
	}

	/// <summary>
	/// Called when a layer's visibility changes.
	/// </summary>
	internal void OnLayerVisibilityChangedInternal( Layer layer )
	{
		if ( _batchUpdateActive )
		{
			_batchDirty = true;
			return;
		}

		// Notify all members in this layer and child layers
		NotifyLayerVisibilityChanged( layer );

		OnLayerVisibilityChanged?.Invoke( layer );
		MarkModified();
	}

	/// <summary>
	/// Called when a layer's opacity changes.
	/// </summary>
	internal void OnLayerOpacityChangedInternal( Layer layer )
	{
		if ( _batchUpdateActive )
		{
			_batchDirty = true;
			return;
		}

		// Notify all members in this layer and child layers
		NotifyLayerOpacityChanged( layer );

		OnLayerOpacityChanged?.Invoke( layer );
		MarkModified();
	}

	private void NotifyLayerVisibilityChanged( Layer layer )
	{
		if ( _membersByLayer.TryGetValue( layer.Id, out var members ) )
		{
			foreach ( var member in members )
			{
				member.OnLayerVisibilityChanged();
			}
		}

		// Also notify child layers
		foreach ( var child in GetChildLayers( layer.Id ) )
		{
			NotifyLayerVisibilityChanged( child );
		}
	}

	private void NotifyLayerOpacityChanged( Layer layer )
	{
		if ( _membersByLayer.TryGetValue( layer.Id, out var members ) )
		{
			foreach ( var member in members )
			{
				member.OnLayerOpacityChanged();
			}
		}

		// Also notify child layers
		foreach ( var child in GetChildLayers( layer.Id ) )
		{
			NotifyLayerOpacityChanged( child );
		}
	}

	/// <summary>
	/// Refreshes all layer members to apply current layer states.
	/// </summary>
	public void RefreshAllMembers()
	{
		foreach ( var member in _members )
		{
			if ( member.IsValid )
			{
				member.ApplyLayerState();
			}
		}
	}

	/// <summary>
	/// Begins a batch update. Layer state changes are deferred until EndBatchUpdate is called.
	/// </summary>
	public void BeginBatchUpdate()
	{
		_batchUpdateActive = true;
		_batchDirty = false;
	}

	/// <summary>
	/// Ends a batch update and applies all pending layer state changes.
	/// </summary>
	public void EndBatchUpdate()
	{
		_batchUpdateActive = false;

		if ( _batchDirty )
		{
			_batchDirty = false;
			RefreshAllMembers();
		}
	}

	#region Visibility Control

	/// <summary>
	/// Shows a layer by name.
	/// </summary>
	public void Show( string layerName )
	{
		var layer = GetLayer( layerName );
		if ( layer is not null )
			layer.Visible = true;
	}

	/// <summary>
	/// Hides a layer by name.
	/// </summary>
	public void Hide( string layerName )
	{
		var layer = GetLayer( layerName );
		if ( layer is not null )
			layer.Visible = false;
	}

	/// <summary>
	/// Toggles a layer's visibility by name.
	/// </summary>
	public void Toggle( string layerName )
	{
		var layer = GetLayer( layerName );
		if ( layer is not null )
			layer.Visible = !layer.Visible;
	}

	/// <summary>
	/// Sets a layer's visibility by name.
	/// </summary>
	public void SetVisible( string layerName, bool visible )
	{
		var layer = GetLayer( layerName );
		if ( layer is not null )
			layer.Visible = visible;
	}

	/// <summary>
	/// Shows only the specified layer, hiding all others.
	/// </summary>
	public void Solo( string layerName )
	{
		BeginBatchUpdate();
		try
		{
			foreach ( var layer in _layers )
			{
				layer.Visible = layer.Name.Equals( layerName, StringComparison.OrdinalIgnoreCase );
			}
		}
		finally
		{
			EndBatchUpdate();
		}
	}

	/// <summary>
	/// Shows all layers.
	/// </summary>
	public void ShowAll()
	{
		BeginBatchUpdate();
		try
		{
			foreach ( var layer in _layers )
			{
				layer.Visible = true;
			}
		}
		finally
		{
			EndBatchUpdate();
		}
	}

	/// <summary>
	/// Hides all layers.
	/// </summary>
	public void HideAll()
	{
		BeginBatchUpdate();
		try
		{
			foreach ( var layer in _layers )
			{
				layer.Visible = false;
			}
		}
		finally
		{
			EndBatchUpdate();
		}
	}

	/// <summary>
	/// Shows only the specified layers, hiding all others.
	/// </summary>
	public void ShowOnly( params string[] layerNames )
	{
		var nameSet = new HashSet<string>( layerNames, StringComparer.OrdinalIgnoreCase );

		BeginBatchUpdate();
		try
		{
			foreach ( var layer in _layers )
			{
				layer.Visible = nameSet.Contains( layer.Name );
			}
		}
		finally
		{
			EndBatchUpdate();
		}
	}

	/// <summary>
	/// Sets visibility for layers matching a predicate.
	/// </summary>
	public void SetVisibleWhere( Func<Layer, bool> predicate, bool visible )
	{
		BeginBatchUpdate();
		try
		{
			foreach ( var layer in _layers.Where( predicate ) )
			{
				layer.Visible = visible;
			}
		}
		finally
		{
			EndBatchUpdate();
		}
	}

	#endregion

	#region Presets/Snapshots

	/// <summary>
	/// Saves the current layer visibility/opacity state as a named preset.
	/// </summary>
	public void SavePreset( string presetName )
	{
		_presets[presetName] = CaptureSnapshot();
		MarkModified();
	}

	/// <summary>
	/// Loads a previously saved preset.
	/// </summary>
	public bool LoadPreset( string presetName )
	{
		if ( !_presets.TryGetValue( presetName, out var snapshot ) )
			return false;

		RestoreSnapshot( snapshot );
		return true;
	}

	/// <summary>
	/// Gets all available preset names.
	/// </summary>
	public IEnumerable<string> GetPresetNames() => _presets.Keys;

	/// <summary>
	/// Deletes a preset by name.
	/// </summary>
	public bool DeletePreset( string presetName )
	{
		var result = _presets.Remove( presetName );
		if ( result ) MarkModified();
		return result;
	}

	/// <summary>
	/// Captures the current layer visibility/opacity state as a snapshot.
	/// </summary>
	public LayerSnapshot CaptureSnapshot()
	{
		return new LayerSnapshot( _layers );
	}

	/// <summary>
	/// Restores layer visibility/opacity state from a snapshot.
	/// </summary>
	public void RestoreSnapshot( LayerSnapshot snapshot )
	{
		if ( snapshot is null ) return;

		BeginBatchUpdate();
		try
		{
			snapshot.Apply( _layers );
		}
		finally
		{
			EndBatchUpdate();
		}
	}

	#endregion

	#region Serialization

	/// <summary>
	/// Serializes all layers to JSON.
	/// </summary>
	public JsonObject Serialize()
	{
		var json = new JsonObject();

		var layersArray = new JsonArray();
		foreach ( var layer in _layers.Where( l => l != DefaultLayer ) )
		{
			layersArray.Add( layer.Serialize() );
		}
		json["Layers"] = layersArray;

		var presetsObj = new JsonObject();
		foreach ( var kvp in _presets )
		{
			presetsObj[kvp.Key] = kvp.Value.Serialize();
		}
		json["Presets"] = presetsObj;

		return json;
	}

	/// <summary>
	/// Deserializes layers from JSON.
	/// </summary>
	public void Deserialize( JsonObject json )
	{
		// Clear existing layers (except default)
		foreach ( var layer in _layers.Where( l => l != DefaultLayer ).ToList() )
		{
			_layers.Remove( layer );
			_layerLookup.Remove( layer.Id );
			_membersByLayer.Remove( layer.Id );
		}

		// Load layers
		if ( json.TryGetPropertyValue( "Layers", out var layersNode ) && layersNode is JsonArray layersArray )
		{
			foreach ( var layerNode in layersArray )
			{
				if ( layerNode is JsonObject layerJson )
				{
					var layer = Layer.Deserialize( layerJson );
					layer.Manager = this;
					_layers.Add( layer );
					_layerLookup[layer.Id] = layer;
					_membersByLayer[layer.Id] = new HashSet<LayerMember>();
				}
			}
		}

		// Load presets
		_presets.Clear();
		if ( json.TryGetPropertyValue( "Presets", out var presetsNode ) && presetsNode is JsonObject presetsObj )
		{
			foreach ( var kvp in presetsObj )
			{
				if ( kvp.Value is JsonObject presetJson )
				{
					_presets[kvp.Key] = LayerSnapshot.Deserialize( presetJson );
				}
			}
		}

		// Refresh all members to apply loaded layer states
		RefreshAllMembers();
	}

	#endregion

	private string GenerateLayerName()
	{
		int counter = 1;
		string name;
		do
		{
			name = $"Layer {counter}";
			counter++;
		} while ( _layers.Any( l => l.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) );

		return name;
	}

	private void MarkModified()
	{
		OnLayersModified?.Invoke();
	}

	public override void Dispose()
	{
		base.Dispose();

		foreach ( var layer in _layers )
		{
			layer.Manager = null;
		}

		_layers.Clear();
		_layerLookup.Clear();
		_membersByLayer.Clear();
		_members.Clear();
		_presets.Clear();
	}
}
