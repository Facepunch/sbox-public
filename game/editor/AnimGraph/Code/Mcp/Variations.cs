using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.AnimGraph;

public static partial class McpTools
{
	/// <summary>
	/// The serialized name of the data a variation data node holds for the default variation.
	/// Inspection reports resolved data instead, so this is filtered out of raw properties.
	/// </summary>
	private const string DefaultVariationDataProperty = "DefaultVariationData";

	/// <summary>
	/// The serialized name of the per variation overrides of a variation data node.
	/// </summary>
	private const string VariationOverridesProperty = "Overrides";

	/// <summary>
	/// Point the graph at the variation an operation targets, so nodes resolve its data.
	/// </summary>
	private static void SelectVariation( Data root, string variationID )
	{
		root.SelectedVariationID = string.IsNullOrWhiteSpace( variationID )
			? Variation.DefaultVariationID
			: FindVariation( root, variationID ).ID;
	}

	private static Variation FindVariation( Data root, string variationID )
	{
		if ( string.IsNullOrWhiteSpace( variationID ) )
			throw new Exception( "A variation is required" );

		var hierarchy = root.Variations;

		return hierarchy.TryGetCaseCorrectVariationID( variationID ) is { } correct
			? hierarchy.GetVariation( correct )
			: throw new Exception( $"Unknown variation '{variationID}'. Available variations: {string.Join( ", ", hierarchy.All.Select( x => x.ID ) )}" );
	}

	private static VariationInfo[] DescribeVariations( Data root )
	{
		var nodes = root.AllNodes.OfType<VariationDataNode>().ToArray();

		return root.Variations.All.Select( variation => new VariationInfo
		{
			Id = variation.ID,
			Parent = variation.ID == Variation.DefaultVariationID ? null : variation.ParentID,
			Model = variation.TargetModelPath,
			Skeleton = variation.TargetSkeletonPath,
			Overrides = nodes.Count( node => node.HasVariationOverride( variation.ID ) )
		} ).ToArray();
	}

	private static NodeVariationInfo DescribeNodeVariation( VariationDataNode node, Data root )
	{
		if ( node is null )
			return null;

		var variationID = root.SelectedVariationID;
		var data = node.GetResolvedVariationData( root.Variations, variationID );

		return new NodeVariationInfo
		{
			Source = root.Variations.ResolveDataSource( variationID, node.HasVariationOverride ),
			HasOverride = node.HasVariationOverride( variationID ),
			OverriddenBy = root.Variations.All
				.Where( variation => node.HasVariationOverride( variation.ID ) )
				.Select( variation => variation.ID )
				.ToArray(),
			Data = data is null ? null : PropertyValues( data )
		};
	}

	private static void AddVariation( Data root, EditOperation operation )
	{
		var variationID = Variation.SanitizeID( operation.Variation )
			?? throw new Exception( "A variation name is required" );

		if ( root.Variations.IsVariationNameTaken( variationID ) )
			throw new Exception( $"A variation named '{variationID}' already exists" );

		var parentID = string.IsNullOrWhiteSpace( operation.Parent )
			? Variation.DefaultVariationID
			: FindVariation( root, operation.Parent ).ID;

		root.Variations.CreateVariation( variationID, parentID );
		ApplyVariationValues( root, root.Variations.GetVariation( variationID ), operation.Values );
	}

	private static void SetVariation( Data root, EditOperation operation )
	{
		var variation = FindVariation( root, operation.Variation );

		if ( Value( operation.Values, "Id" )?.GetValue<string>() is { Length: > 0 } requestedName )
			RenameVariation( root, variation, requestedName );

		ApplyVariationValues( root, variation, operation.Values );
	}

	private static JsonNode Value( JsonObject values, string name )
		=> values?.FirstOrDefault( value => string.Equals( value.Key, name, StringComparison.OrdinalIgnoreCase ) ).Value;

	private static void RenameVariation( Data root, Variation variation, string requestedName )
	{
		if ( variation.ID == Variation.DefaultVariationID )
			throw new Exception( "The default variation cannot be renamed" );

		var newID = Variation.SanitizeID( requestedName )
			?? throw new Exception( "A variation name is required" );

		if ( newID == variation.ID )
			return;

		root.RenameVariation( variation.ID, newID );
		root.SelectedVariationID = newID;
	}

	private static void RemoveVariation( Data root, EditOperation operation )
	{
		var variation = FindVariation( root, operation.Variation );

		if ( variation.ID == Variation.DefaultVariationID )
			throw new Exception( "The default variation cannot be removed" );

		// Takes the children and the data nodes hold for them with it
		root.RemoveVariation( variation.ID );
	}

	private static void ApplyVariationValues( Data root, Variation variation, JsonObject values )
	{
		if ( values is null )
			return;

		foreach ( var (name, value) in values )
		{
			// Renaming is handled before anything else, so the rest lands on the right variation
			if ( string.Equals( name, "Id", StringComparison.OrdinalIgnoreCase ) )
				continue;

			if ( string.Equals( name, "Parent", StringComparison.OrdinalIgnoreCase )
				|| string.Equals( name, nameof( Variation.ParentID ), StringComparison.OrdinalIgnoreCase ) )
			{
				Reparent( root, variation, value?.GetValue<string>() );
				continue;
			}

			if ( string.Equals( name, "Model", StringComparison.OrdinalIgnoreCase )
				|| string.Equals( name, nameof( Variation.TargetModel ), StringComparison.OrdinalIgnoreCase ) )
			{
				var modelPath = value?.GetValue<string>();
				variation.TargetModel = string.IsNullOrWhiteSpace( modelPath ) ? null : Model.Load( modelPath );
				continue;
			}

			if ( string.Equals( name, "Skeleton", StringComparison.OrdinalIgnoreCase )
				|| string.Equals( name, nameof( Variation.TargetSkeletonPath ), StringComparison.OrdinalIgnoreCase ) )
			{
				var skeletonPath = value?.GetValue<string>();
				variation.TargetSkeletonPath = string.IsNullOrWhiteSpace( skeletonPath ) ? null : skeletonPath;
				continue;
			}

			var property = FindSerializableProperty( variation, name );
			property.SetValue( variation, value?.Deserialize( property.PropertyType, JsonOptions ) );
		}
	}

	private static void Reparent( Data root, Variation variation, string parentID )
	{
		if ( variation.ID == Variation.DefaultVariationID )
			throw new Exception( "The default variation is the root of the hierarchy and has no parent" );

		var parent = FindVariation( root, parentID );

		if ( parent.ID == variation.ID || root.Variations.GetDescendantVariations( variation.ID ).Contains( parent.ID ) )
			throw new Exception( $"'{parent.ID}' inherits from '{variation.ID}', so it cannot become its parent" );

		variation.ParentID = parent.ID;
	}

	private static void SetVariationOverride( Data root, EditOperation operation, IReadOnlyDictionary<string, string> aliases, bool hasOverride )
	{
		var graph = FindGraph( root, operation.Graph, aliases );
		var node = FindNode( graph, operation.Node, aliases ) as VariationDataNode
			?? throw new Exception( $"{operation.Node} holds no per variation data" );

		var variation = FindVariation( root, operation.Variation );

		if ( variation.ID == Variation.DefaultVariationID )
			throw new Exception( "The default variation owns the data every other variation inherits, so it cannot override it" );

		if ( hasOverride )
			node.CreateVariationOverride( variation.ID );
		else
			node.RemoveVariationOverride( variation.ID );
	}

	/// <summary>
	/// Editing a node writes the data of the variation being targeted, which only exists once that
	/// variation overrides the node - without this an edit would silently change what it inherits.
	/// </summary>
	private static void EnsureVariationIsWritable( Data root, Node node, IEnumerable<string> properties )
	{
		if ( node is not VariationDataNode variationNode || properties is null )
			return;

		var variationID = root.SelectedVariationID;

		if ( variationID == Variation.DefaultVariationID || variationNode.HasVariationOverride( variationID ) )
			return;

		var data = variationNode.SelectedVariationData;

		if ( !properties.Any( property => HasSerializableProperty( data, property ) ) )
			return;

		throw new Exception( $"'{variationID}' inherits this data from '{root.Variations.ResolveDataSource( variationID, variationNode.HasVariationOverride )}'"
			+ " - add_override for this node first, or edit the variation it inherits from" );
	}
}
