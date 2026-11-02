using Editor.AnimGraph.Nodes;
using Editor.Mcp;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph;

public static partial class McpTools
{
	/// <summary>
	/// Discover AnimGraph node types and their authoring schema: properties, defaults,
	/// collection item types, static and default dynamic plugs, and graph kinds where each
	/// node is valid. Search by class name or display title; leave empty to list everything.
	/// Properties with a "names" source only accept a value listed by animgraph_names.
	/// </summary>
	/// <param name="search">Optional class-name or title substring.</param>
	/// <param name="graphKind">Optional graph kind filter: BlendTree, TransitionConduit, GlobalTransitions, EntryOverrides, LayerData, SelectorConditions, or VirtualParameterValueTree.</param>
	[McpTool.ReadOnly( "animgraph_node_types" )]
	public static object NodeTypes( string search = "", string graphKind = "" )
	{
		var filterKind = string.IsNullOrWhiteSpace( graphKind )
			? (GraphKind?)null
			: Enum.TryParse<GraphKind>( graphKind, true, out var parsed )
				? parsed
				: throw new Exception( $"Unknown graph kind '{graphKind}'" );

		var types = NodeTypesForAuthoring()
			.Select( type => (Type: type, Node: type.Create<Node>(), Info: DisplayInfo.ForType( type.TargetType )) )
			.Where( item => item.Node.CanClone && item.Node.CanRemove )
			.Where( item => string.IsNullOrWhiteSpace( search )
				|| item.Type.Name.Contains( search, StringComparison.OrdinalIgnoreCase )
				|| item.Info.Name.Contains( search, StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( item => item.Info.Group )
			.ThenBy( item => item.Info.Name )
			.Select( item =>
			{
				var kinds = Enum.GetValues<GraphKind>()
					.Where( kind => new NodeType( item.Type ).IsAvailableIn( new Data { Kind = kind } ) )
					.ToArray();

				if ( filterKind is { } required && !kinds.Contains( required ) )
					return null;

				return new
				{
					Type = item.Type.Name,
					item.Type.FullName,
					Title = item.Info.Name,
					item.Info.Description,
					item.Info.Group,
					item.Info.Icon,
					GraphKinds = kinds.Select( kind => kind.ToString() ).ToArray(),
					Properties = DescribeProperties( item.Type.TargetType, item.Node ),
					Inputs = item.Node.Inputs.Select( plug => new
					{
						Name = plug.Identifier,
						Title = plug.DisplayInfo.Name,
						Type = LinkType( plug.Type )
					} ).ToArray(),
					Outputs = item.Node.Outputs.Select( plug => new
					{
						Name = plug.Identifier,
						Title = plug.DisplayInfo.Name,
						Type = LinkType( plug.Type )
					} ).ToArray()
				};
			} )
			.Where( item => item is not null )
			.ToArray();

		return new { NodeTypes = types };
	}

	private static PropertySchema[] DescribeProperties( Type type, object instance = null, int depth = 0 )
	{
		return type.GetProperties( BindingFlags.Instance | BindingFlags.Public )
			.Where( property => property.CanWrite && property.GetCustomAttribute<JsonIgnoreAttribute>() is null )
			.Where( property => property.Name is not nameof( Node.Identifier ) and not nameof( Node.HandleOffsets ) )
			.Where( property => !typeof( IDBasedSelectorNode ).IsAssignableFrom( type )
				|| property.Name is not nameof( IDBasedSelectorNode.Options )
					and not nameof( IDBasedSelectorNode.ParameterIdentifier ) )
			.Where( property => !typeof( Data ).IsAssignableFrom( property.PropertyType ) )
			.Where( property => property.PropertyType != typeof( NodeInput ) )
			.Select( property =>
			{
				var isCollection = typeof( IList ).IsAssignableFrom( property.PropertyType );
				var itemType = isCollection ? CollectionElementType( property ) : null;
				var isPlugList = itemType == typeof( NodeInput );
				var display = DisplayInfo.ForMember( property );

				return new PropertySchema
				{
					Name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name,
					Title = display.Name,
					Type = PrettyType( property.PropertyType ),
					Description = isPlugList
						? "One input plug per item - add_item and remove_item change how many inputs this node takes, then connect them."
						: display.Description,
					Hidden = property.GetCustomAttribute<HideAttribute>() is not null,
					EditWith = isPlugList ? "plug_item_operations" : isCollection ? "item_operations" : "values",
					Names = NameSource( property ),
					Values = property.PropertyType.IsEnum ? Enum.GetNames( property.PropertyType ) : null,
					CollectionItemType = itemType is null ? null : PrettyType( itemType ),
					ItemProperties = itemType is not null && depth < 2 && !isPlugList && !IsSimpleType( itemType )
						? DescribeProperties( itemType, depth: depth + 1 )
						: null,
					Default = SimpleDefault( property, instance )
				};
			} )
			.ToArray();
	}

	/// <summary>
	/// Which list from animgraph_names a property's value has to come from, if any.
	/// </summary>
	private static string NameSource( PropertyInfo property )
	{
		if ( property.PropertyType != typeof( string ) )
			return null;

		if ( property.GetCustomAttribute<ResourceTypeAttribute>()?.AssetTypeExtension == "anim" )
			return "Clips";

		return property.GetCustomAttribute<EditorAttribute>()?.Value switch
		{
			"animgraph.bone" => "Bones",
			"animgraph.bonemask" => "BoneMasks",
			_ => null
		};
	}

	private static JsonNode SimpleDefault( PropertyInfo property, object instance )
	{
		if ( instance is null || !property.CanRead )
			return null;

		var type = property.PropertyType;
		if ( !IsSimpleType( type ) )
			return null;

		var value = property.GetValue( instance );
		return value is null ? null : JsonSerializer.SerializeToNode( value, type, JsonOptions );
	}

	private static bool IsSimpleType( Type type )
		=> type.IsPrimitive || type.IsEnum || type == typeof( string ) || type == typeof( Vector2 ) || type == typeof( Vector3 );

	private static string PrettyType( Type type )
	{
		if ( type.IsGenericType )
		{
			var name = type.Name[..type.Name.IndexOf( '`' )];
			return $"{name}<{string.Join( ", ", type.GetGenericArguments().Select( PrettyType ) )}>";
		}

		return type.Name;
	}

	private static string LinkType( Type type )
		=> type.Name.EndsWith( "Link", StringComparison.Ordinal ) ? type.Name[..^4] : type.Name;

	private sealed class PropertySchema
	{
		public string Name { get; set; }
		public string Title { get; set; }
		public string Type { get; set; }
		public string Description { get; set; }
		public bool Hidden { get; set; }
		public string EditWith { get; set; }

		/// <summary>The animgraph_names list this value has to come from, if any.</summary>
		public string Names { get; set; }

		public string[] Values { get; set; }
		public string CollectionItemType { get; set; }
		public PropertySchema[] ItemProperties { get; set; }
		public JsonNode Default { get; set; }
	}
}
