using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// Represents a snapshot of layer states that can be saved and restored.
/// Used for layer presets and undo/redo operations.
/// </summary>
[Expose]
public sealed class LayerSnapshot
{
	/// <summary>
	/// State data for a single layer.
	/// </summary>
	public struct LayerState
	{
		public Guid LayerId;
		public bool Visible;
		public float Opacity;
		public bool Locked;

		public LayerState( Layer layer )
		{
			LayerId = layer.Id;
			Visible = layer.Visible;
			Opacity = layer.Opacity;
			Locked = layer.Locked;
		}

		public void Apply( Layer layer )
		{
			if ( layer is null || layer.Id != LayerId ) return;

			layer.Visible = Visible;
			layer.Opacity = Opacity;
			layer.Locked = Locked;
		}

		public JsonObject Serialize()
		{
			return new JsonObject
			{
				["LayerId"] = LayerId.ToString(),
				["Visible"] = Visible,
				["Opacity"] = Opacity,
				["Locked"] = Locked
			};
		}

		public static LayerState Deserialize( JsonObject json )
		{
			var state = new LayerState();

			if ( json.TryGetPropertyValue( "LayerId", out var idNode ) && Guid.TryParse( idNode?.GetValue<string>(), out var id ) )
				state.LayerId = id;

			if ( json.TryGetPropertyValue( "Visible", out var visibleNode ) )
				state.Visible = visibleNode?.GetValue<bool>() ?? true;

			if ( json.TryGetPropertyValue( "Opacity", out var opacityNode ) )
				state.Opacity = opacityNode?.GetValue<float>() ?? 1f;

			if ( json.TryGetPropertyValue( "Locked", out var lockedNode ) )
				state.Locked = lockedNode?.GetValue<bool>() ?? false;

			return state;
		}
	}

	/// <summary>
	/// The captured layer states.
	/// </summary>
	public List<LayerState> States { get; } = new();

	/// <summary>
	/// When this snapshot was created.
	/// </summary>
	public DateTime CreatedAt { get; set; } = DateTime.Now;

	/// <summary>
	/// Optional description for this snapshot.
	/// </summary>
	public string Description { get; set; }

	public LayerSnapshot() { }

	/// <summary>
	/// Creates a snapshot from the given layers.
	/// </summary>
	public LayerSnapshot( IEnumerable<Layer> layers )
	{
		foreach ( var layer in layers )
		{
			States.Add( new LayerState( layer ) );
		}
	}

	/// <summary>
	/// Applies this snapshot's state to the given layers.
	/// </summary>
	public void Apply( IEnumerable<Layer> layers )
	{
		var layerDict = layers.ToDictionary( l => l.Id );

		foreach ( var state in States )
		{
			if ( layerDict.TryGetValue( state.LayerId, out var layer ) )
			{
				state.Apply( layer );
			}
		}
	}

	/// <summary>
	/// Serializes the snapshot to JSON.
	/// </summary>
	public JsonObject Serialize()
	{
		var json = new JsonObject
		{
			["CreatedAt"] = CreatedAt.ToString( "o" ),
			["Description"] = Description
		};

		var statesArray = new JsonArray();
		foreach ( var state in States )
		{
			statesArray.Add( state.Serialize() );
		}
		json["States"] = statesArray;

		return json;
	}

	/// <summary>
	/// Deserializes a snapshot from JSON.
	/// </summary>
	public static LayerSnapshot Deserialize( JsonObject json )
	{
		var snapshot = new LayerSnapshot();

		if ( json.TryGetPropertyValue( "CreatedAt", out var createdNode ) )
		{
			var createdStr = createdNode?.GetValue<string>();
			if ( DateTime.TryParse( createdStr, out var created ) )
				snapshot.CreatedAt = created;
		}

		if ( json.TryGetPropertyValue( "Description", out var descNode ) )
			snapshot.Description = descNode?.GetValue<string>();

		if ( json.TryGetPropertyValue( "States", out var statesNode ) && statesNode is JsonArray statesArray )
		{
			foreach ( var stateNode in statesArray )
			{
				if ( stateNode is JsonObject stateJson )
				{
					snapshot.States.Add( LayerState.Deserialize( stateJson ) );
				}
			}
		}

		return snapshot;
	}

	/// <summary>
	/// Creates a copy of this snapshot.
	/// </summary>
	public LayerSnapshot Clone()
	{
		var clone = new LayerSnapshot
		{
			CreatedAt = CreatedAt,
			Description = Description
		};

		clone.States.AddRange( States );
		return clone;
	}
}
