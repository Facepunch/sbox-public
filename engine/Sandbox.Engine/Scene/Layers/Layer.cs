using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// Blend modes for visual assets that support blending.
/// </summary>
public enum LayerBlendMode
{
	/// <summary>Normal blending - no special effect</summary>
	Normal,
	/// <summary>Multiply blend mode</summary>
	Multiply,
	/// <summary>Screen blend mode</summary>
	Screen,
	/// <summary>Overlay blend mode</summary>
	Overlay,
	/// <summary>Additive blend mode</summary>
	Additive
}

/// <summary>
/// Represents a layer in the scene that can contain GameObjects and Components.
/// Layers provide organizational structure and visibility/opacity control for scene assets.
/// </summary>
[Expose]
public sealed class Layer : IValid
{
	/// <summary>
	/// Unique identifier for this layer.
	/// </summary>
	public Guid Id { get; internal set; } = Guid.NewGuid();

	/// <summary>
	/// Human-readable name for the layer.
	/// </summary>
	public string Name { get; set; } = "New Layer";

	private bool _visible = true;

	/// <summary>
	/// Current visibility state of the layer.
	/// When false, all members of this layer are hidden.
	/// </summary>
	public bool Visible
	{
		get => _visible;
		set
		{
			if ( _visible == value ) return;
			_visible = value;
			OnVisibilityChanged?.Invoke( this );
			Manager?.OnLayerVisibilityChangedInternal( this );
		}
	}

	private bool _locked = false;

	/// <summary>
	/// When true, prevents selection and modification of layer members in the editor.
	/// </summary>
	public bool Locked
	{
		get => _locked;
		set
		{
			if ( _locked == value ) return;
			_locked = value;
			OnLockChanged?.Invoke( this );
		}
	}

	/// <summary>
	/// Visual identifier color for this layer in the editor UI.
	/// </summary>
	public Color Color { get; set; } = Color.White;

	private float _opacity = 1.0f;

	/// <summary>
	/// Opacity multiplier for supported asset types (0-1).
	/// Affects rendering opacity for visual assets, volume for audio, intensity for lights.
	/// </summary>
	public float Opacity
	{
		get => _opacity;
		set
		{
			value = value.Clamp( 0f, 1f );
			if ( Math.Abs( _opacity - value ) < 0.0001f ) return;
			_opacity = value;
			OnOpacityChanged?.Invoke( this );
			Manager?.OnLayerOpacityChangedInternal( this );
		}
	}

	/// <summary>
	/// Blend mode for visual assets that support blending.
	/// </summary>
	public LayerBlendMode BlendMode { get; set; } = LayerBlendMode.Normal;

	/// <summary>
	/// Z-order/render priority. Higher values render on top.
	/// </summary>
	public int SortOrder { get; set; } = 0;

	/// <summary>
	/// Parent layer ID for layer grouping/hierarchy.
	/// </summary>
	public Guid? ParentLayerId { get; set; }

	/// <summary>
	/// UI state for groups - whether the layer group is expanded in the editor.
	/// </summary>
	public bool IsExpanded { get; set; } = true;

	/// <summary>
	/// Whether this layer is a group that can contain other layers.
	/// </summary>
	public bool IsGroup { get; set; } = false;

	/// <summary>
	/// Optional tags for categorizing layers.
	/// </summary>
	public HashSet<string> Tags { get; set; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The LayerManager that owns this layer.
	/// </summary>
	internal LayerManager Manager { get; set; }

	/// <summary>
	/// Event fired when visibility changes.
	/// </summary>
	public event Action<Layer> OnVisibilityChanged;

	/// <summary>
	/// Event fired when lock state changes.
	/// </summary>
	public event Action<Layer> OnLockChanged;

	/// <summary>
	/// Event fired when opacity changes.
	/// </summary>
	public event Action<Layer> OnOpacityChanged;

	/// <summary>
	/// Returns true if this layer is valid and has a manager.
	/// </summary>
	public bool IsValid => Manager is not null;

	/// <summary>
	/// Gets the parent layer if one exists.
	/// </summary>
	public Layer Parent => ParentLayerId.HasValue ? Manager?.GetLayer( ParentLayerId.Value ) : null;

	/// <summary>
	/// Gets all child layers if this is a group.
	/// </summary>
	public IEnumerable<Layer> Children => Manager?.GetChildLayers( Id ) ?? Enumerable.Empty<Layer>();

	/// <summary>
	/// Gets all GameObjects that are members of this layer.
	/// </summary>
	public IReadOnlyList<GameObject> GetMembers()
	{
		return Manager?.GetLayerMembers( this ) ?? Array.Empty<GameObject>();
	}

	/// <summary>
	/// Gets all Components of type T that belong to GameObjects in this layer.
	/// </summary>
	public IReadOnlyList<T> GetMembers<T>() where T : Component
	{
		var members = GetMembers();
		var result = new List<T>();

		foreach ( var go in members )
		{
			if ( !go.IsValid() ) continue;
			var components = go.Components.GetAll<T>();
			result.AddRange( components );
		}

		return result;
	}

	/// <summary>
	/// Adds a GameObject to this layer.
	/// </summary>
	public void Add( GameObject obj )
	{
		if ( obj is null || !obj.IsValid() ) return;

		var member = obj.Components.GetOrCreate<LayerMember>();
		member.Layer = this;
	}

	/// <summary>
	/// Removes a GameObject from this layer.
	/// </summary>
	public void Remove( GameObject obj )
	{
		if ( obj is null || !obj.IsValid() ) return;

		var member = obj.Components.Get<LayerMember>();
		if ( member is not null && member.Layer == this )
		{
			member.Layer = null;
		}
	}

	/// <summary>
	/// Returns true if the GameObject is a member of this layer.
	/// </summary>
	public bool Contains( GameObject obj )
	{
		if ( obj is null || !obj.IsValid() ) return false;

		var member = obj.Components.Get<LayerMember>();
		return member?.Layer == this;
	}

	/// <summary>
	/// Gets the effective visibility considering parent layer visibility.
	/// </summary>
	public bool EffectiveVisible
	{
		get
		{
			if ( !Visible ) return false;
			var parent = Parent;
			return parent?.EffectiveVisible ?? true;
		}
	}

	/// <summary>
	/// Gets the effective opacity considering parent layer opacity.
	/// </summary>
	public float EffectiveOpacity
	{
		get
		{
			var parent = Parent;
			var parentOpacity = parent?.EffectiveOpacity ?? 1f;
			return Opacity * parentOpacity;
		}
	}

	/// <summary>
	/// Serializes the layer to JSON.
	/// </summary>
	public JsonObject Serialize()
	{
		var json = new JsonObject
		{
			["Id"] = Id.ToString(),
			["Name"] = Name,
			["Visible"] = Visible,
			["Locked"] = Locked,
			["Color"] = Color.ToHex( true ),
			["Opacity"] = Opacity,
			["BlendMode"] = BlendMode.ToString(),
			["SortOrder"] = SortOrder,
			["IsExpanded"] = IsExpanded,
			["IsGroup"] = IsGroup
		};

		if ( ParentLayerId.HasValue )
		{
			json["ParentLayerId"] = ParentLayerId.Value.ToString();
		}

		if ( Tags.Count > 0 )
		{
			json["Tags"] = new JsonArray( Tags.Select( t => JsonValue.Create( t ) ).ToArray() );
		}

		return json;
	}

	/// <summary>
	/// Deserializes a layer from JSON.
	/// </summary>
	public static Layer Deserialize( JsonObject json )
	{
		var layer = new Layer();

		if ( json.TryGetPropertyValue( "Id", out var idNode ) && Guid.TryParse( idNode?.GetValue<string>(), out var id ) )
			layer.Id = id;

		if ( json.TryGetPropertyValue( "Name", out var nameNode ) )
			layer.Name = nameNode?.GetValue<string>() ?? "New Layer";

		if ( json.TryGetPropertyValue( "Visible", out var visibleNode ) )
			layer._visible = visibleNode?.GetValue<bool>() ?? true;

		if ( json.TryGetPropertyValue( "Locked", out var lockedNode ) )
			layer._locked = lockedNode?.GetValue<bool>() ?? false;

		if ( json.TryGetPropertyValue( "Color", out var colorNode ) )
		{
			var colorStr = colorNode?.GetValue<string>();
			if ( !string.IsNullOrEmpty( colorStr ) )
				layer.Color = Color.Parse( colorStr ) ?? Color.White;
		}

		if ( json.TryGetPropertyValue( "Opacity", out var opacityNode ) )
			layer._opacity = opacityNode?.GetValue<float>() ?? 1f;

		if ( json.TryGetPropertyValue( "BlendMode", out var blendNode ) )
		{
			var blendStr = blendNode?.GetValue<string>();
			if ( Enum.TryParse<LayerBlendMode>( blendStr, out var blend ) )
				layer.BlendMode = blend;
		}

		if ( json.TryGetPropertyValue( "SortOrder", out var sortNode ) )
			layer.SortOrder = sortNode?.GetValue<int>() ?? 0;

		if ( json.TryGetPropertyValue( "ParentLayerId", out var parentNode ) )
		{
			var parentStr = parentNode?.GetValue<string>();
			if ( Guid.TryParse( parentStr, out var parentId ) )
				layer.ParentLayerId = parentId;
		}

		if ( json.TryGetPropertyValue( "IsExpanded", out var expandedNode ) )
			layer.IsExpanded = expandedNode?.GetValue<bool>() ?? true;

		if ( json.TryGetPropertyValue( "IsGroup", out var groupNode ) )
			layer.IsGroup = groupNode?.GetValue<bool>() ?? false;

		if ( json.TryGetPropertyValue( "Tags", out var tagsNode ) && tagsNode is JsonArray tagsArray )
		{
			foreach ( var tag in tagsArray )
			{
				var tagStr = tag?.GetValue<string>();
				if ( !string.IsNullOrEmpty( tagStr ) )
					layer.Tags.Add( tagStr );
			}
		}

		return layer;
	}

	public override string ToString() => $"Layer[{Name}]";
}
