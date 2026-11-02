using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph;

/// <summary>
/// A node whose data can be overridden per graph variation. It holds one set of data for the
/// default variation plus an override per variation that needs different data.
/// Ported from Esoterica's <c>EE::Animation::VariationDataToolsNode</c>.
/// </summary>
public abstract class VariationDataNode : Node
{
	internal event Action OverridesChanged;

	protected void NotifyOverridesChanged() => OverridesChanged?.Invoke();

	/// <summary>
	/// The name of the property holding the data of the variation being edited.
	/// </summary>
	internal const string SelectedDataProperty = "SelectedData";

	/// <summary>
	/// The per variation data of a <see cref="VariationDataNode"/>.
	/// </summary>
	public abstract class VariationData
	{
		public enum VisualState
		{
			None,
			HasUnsetData
		}

		/// <summary>
		/// The node this data belongs to - lets control widgets find the graph they're editing.
		/// </summary>
		[JsonIgnore, Hide, Browsable( false )]
		public VariationDataNode Owner { get; internal set; }

		/// <summary>
		/// Get the visual state that we should display for this data.
		/// </summary>
		public virtual VisualState GetVisualState() => VisualState.None;
	}

	/// <summary>
	/// Get the default data.
	/// </summary>
	public abstract VariationData GetDefaultVariationData();

	/// <summary>
	/// This will return the final resolved variation data for this slot.
	/// </summary>
	public abstract VariationData GetResolvedVariationData( VariationHierarchy variationHierarchy, string variationID );

	public abstract bool HasVariationOverride( string variationID );
	public abstract void CreateVariationOverride( string variationID );
	public abstract void RenameVariationOverride( string oldVariationID, string newVariationID );
	public abstract void RemoveVariationOverride( string variationID );

	/// <summary>
	/// The data for the variation currently being edited - what the node shows in the editor.
	/// </summary>
	[JsonIgnore, Hide, Browsable( false )]
	public VariationData SelectedVariationData => Root is { } root
		? GetResolvedVariationData( root.Variations, root.SelectedVariationID )
		: GetDefaultVariationData();
}

/// <summary>
/// The data a single variation overrides on a <see cref="VariationDataNode"/>.
/// Ported from Esoterica's <c>VariationDataToolsNode::OverrideValue</c>.
/// </summary>
public sealed class VariationOverride<T> where T : VariationDataNode.VariationData, new()
{
	public string VariationID { get; set; }
	public T VariationData { get; set; } = new();
}

/// <inheritdoc cref="VariationDataNode"/>
public abstract class VariationDataNode<T> : VariationDataNode where T : VariationDataNode.VariationData, new()
{
	[Hide, Browsable( false )]
	public T DefaultVariationData { get; set; } = new();

	[Hide, Browsable( false )]
	public List<VariationOverride<T>> Overrides { get; set; } = [];

	/// <summary>
	/// The data for the variation currently being edited, of this node's data type.
	/// </summary>
	[Property, InlineEditor( Label = false ), JsonIgnore]
	public T SelectedData => (T)SelectedVariationData;

	public override VariationData GetDefaultVariationData()
	{
		DefaultVariationData.Owner = this;
		return DefaultVariationData;
	}

	public override VariationData GetResolvedVariationData( VariationHierarchy variationHierarchy, string variationID )
	{
		var variationData = ResolveVariationData( variationHierarchy, variationID );
		variationData.Owner = this;
		return variationData;
	}

	private T ResolveVariationData( VariationHierarchy variationHierarchy, string variationID )
	{
		var variationData = DefaultVariationData;

		if ( variationHierarchy is null || string.IsNullOrEmpty( variationID ) || variationID == Variation.DefaultVariationID )
			return variationData;

		bool TryGetVariationData( string id )
		{
			if ( id == Variation.DefaultVariationID )
			{
				variationData = DefaultVariationData;
				return true;
			}

			foreach ( var variation in Overrides )
			{
				if ( variation.VariationID != id )
					continue;

				variationData = variation.VariationData;
				return true;
			}

			return false;
		}

		// Try get the data for this variation
		if ( TryGetVariationData( variationID ) )
			return variationData;

		// Go up the hierarchy and return the first if an override exists.
		// Bounded by the variation count so a malformed hierarchy can't spin.
		var parentVariationID = variationHierarchy.GetParentVariationID( variationID );

		for ( var i = variationHierarchy.All.Count; i > 0 && !string.IsNullOrEmpty( parentVariationID ); i-- )
		{
			if ( TryGetVariationData( parentVariationID ) )
				break;

			parentVariationID = variationHierarchy.GetParentVariationID( parentVariationID );
		}

		return variationData;
	}

	public override bool HasVariationOverride( string variationID )
	{
		// This query makes no sense on the default variation
		if ( variationID == Variation.DefaultVariationID )
			return false;

		return Overrides.Any( variation => variation.VariationID == variationID );
	}

	public override void CreateVariationOverride( string variationID )
	{
		if ( string.IsNullOrEmpty( variationID ) || variationID == Variation.DefaultVariationID )
			return;

		if ( HasVariationOverride( variationID ) )
			return;

		// Start from the data it inherits, so overriding doesn't blank out what the node was playing
		var inherited = ResolveVariationData( Root?.Variations, variationID );

		Overrides.Add( new VariationOverride<T>
		{
			VariationID = variationID,
			VariationData = Clone( inherited ),
		} );
		NotifyOverridesChanged();
	}

	/// <summary>
	/// Variation data is plain serializable data, so a round trip is a deep copy - the override
	/// must not share lists with the data it was copied from.
	/// </summary>
	private static T Clone( T source )
	{
		if ( source is null )
			return new();

		var json = JsonSerializer.Serialize( source, Data.SerializerOptions );
		return JsonSerializer.Deserialize<T>( json, Data.SerializerOptions ) ?? new();
	}

	public override void RenameVariationOverride( string oldVariationID, string newVariationID )
	{
		if ( string.IsNullOrEmpty( oldVariationID ) || string.IsNullOrEmpty( newVariationID ) )
			return;

		if ( oldVariationID == Variation.DefaultVariationID || newVariationID == Variation.DefaultVariationID )
			return;

		var changed = false;
		foreach ( var overrideValue in Overrides )
		{
			if ( overrideValue.VariationID == oldVariationID )
			{
				overrideValue.VariationID = newVariationID;
				changed = true;
			}
		}

		if ( changed )
			NotifyOverridesChanged();
	}

	public override void RemoveVariationOverride( string variationID )
	{
		if ( string.IsNullOrEmpty( variationID ) || variationID == Variation.DefaultVariationID )
			return;

		if ( Overrides.RemoveAll( overrideValue => overrideValue.VariationID == variationID ) > 0 )
			NotifyOverridesChanged();
	}

	protected IEnumerable<T> AllVariationData => Overrides
		.Select( overrideValue => overrideValue.VariationData )
		.Where( data => data is not null )
		.Prepend( DefaultVariationData );
}
