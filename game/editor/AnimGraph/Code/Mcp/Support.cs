using Editor.AnimGraph.Nodes;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph;

public static partial class McpTools
{
	private sealed record GraphReference( string Id, string Owner, Data Graph );

	private static Session LoadSession( string path )
	{
		var asset = AssetSystem.FindByPath( path )
			?? throw new Exception( $"No AnimGraph asset at '{path}' - find it with asset_search or create it with animgraph_create" );

		return Session.Load( asset );
	}

	private static void EnsureWritableProjectAsset( Session session )
	{
		if ( string.IsNullOrWhiteSpace( session.SourceFile ) )
			throw new Exception( "The AnimGraph has no writable source file" );
	}

	/// <summary>
	/// The assets folder to create a graph in. Content addons like citizen are mounted from the
	/// game's addons folder without ever being loaded as a project, so fall back to that.
	/// </summary>
	private static string FindAssetsPath( string project )
	{
		if ( string.IsNullOrWhiteSpace( project ) )
			return Project.Current?.GetAssetsPath() ?? throw new Exception( "No project is currently open" );

		var loaded = EditorUtility.Projects.GetAll()
			.FirstOrDefault( x => string.Equals( x.Config?.Ident, project, StringComparison.OrdinalIgnoreCase ) );

		if ( loaded?.GetAssetsPath() is { } assets )
			return assets;

		var addon = $"/addons/{project}/Assets/";

		return FileSystem.Root.DirectoryExists( addon )
			? FileSystem.Root.GetFullPath( addon )
			: throw new Exception( $"No project or addon called '{project}'" );
	}

	private static string ResolveNewAssetPath( string path, string project )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new Exception( "Give a path relative to the project's assets folder" );

		var absolute = System.IO.Path.Combine( FindAssetsPath( project ), path.Replace( '\\', '/' ).TrimStart( '/' ) );
		return System.IO.Path.ChangeExtension( absolute, "ag" );
	}

	private static void CheckRevision( Session session, string expectedRevision )
	{
		if ( string.IsNullOrWhiteSpace( expectedRevision ) )
			throw new Exception( "expectedRevision is required - get it from animgraph_inspect or animgraph_create" );

		if ( !string.Equals( session.Revision, expectedRevision, StringComparison.OrdinalIgnoreCase ) )
			throw new Exception( $"The AnimGraph changed since it was inspected. Expected {expectedRevision}, current revision is {session.Revision}" );
	}

	private static CompileResult ToResult( Compiler.Result result )
	{
		return new CompileResult
		{
			ElapsedMilliseconds = result.ElapsedMilliseconds,
			RootNodeIndex = result.RootNodeIndex,
			Warnings = result.Warnings.Select( warning => new CompileWarning
			{
				Message = warning.Message,
				Node = warning.Node?.Identifier,
				NodeType = warning.Node?.GetType().Name,
				Severity = warning.Severity.ToString()
			} ).ToArray(),
			NodeIndices = result.NodeIndices.ToDictionary( pair => pair.Key.Identifier, pair => pair.Value )
		};
	}

	private static IReadOnlyList<GraphReference> Graphs( Data root )
	{
		var result = new List<GraphReference>();
		var visited = new HashSet<Data>( ReferenceEqualityComparer.Instance );

		void Add( Data graph, string id, string owner )
		{
			if ( graph is null || !visited.Add( graph ) )
				return;

			graph.SetRoot( root );
			result.Add( new GraphReference( id, owner, graph ) );

			foreach ( var node in graph.Nodes.OfType<Node>() )
			{
				var nodePath = $"{id}/node:{node.Identifier}";

				if ( node is StateMachineNode machine )
				{
					machine.SyncStates();

					for ( var i = 0; i < machine.States.Count; i++ )
					{
						var state = machine.States[i];
						Add( state.Graph, $"{nodePath}/state:{i}", $"{machine.DisplayInfo.Name} / {state.Name}" );
						Add( state.LayerGraph, $"{nodePath}/state:{i}/layer", $"{machine.DisplayInfo.Name} / {state.Name} Layer" );
					}

					for ( var i = 0; i < machine.Conduits.Count; i++ )
					{
						var conduit = machine.Conduits[i];
						Add( conduit.Graph, $"{nodePath}/conduit:{i}", $"{machine.DisplayInfo.Name} / {conduit.From} -> {conduit.To}" );
					}

					Add( machine.GlobalGraph, $"{nodePath}/global", $"{machine.DisplayInfo.Name} / Global Transitions" );
					Add( machine.EntryGraph, $"{nodePath}/entry", $"{machine.DisplayInfo.Name} / Entry Conditions" );
				}
				else if ( node is SelectorNode selector )
				{
					Add( selector.SecondaryGraph, $"{nodePath}/conditions", $"{selector.DisplayInfo.Name} / Conditions" );
				}
				else if ( node is Blend2DNode blend )
				{
					for ( var i = 0; i < blend.Points.Count; i++ )
					{
						var point = blend.Points[i];
						Add( point.Graph, $"{nodePath}/point:{i}", $"{blend.DisplayInfo.Name} / {blend.PointTitle( i )}" );
					}
				}
				else if ( node is Blend1DNode blend1D )
				{
					for ( var i = 0; i < blend1D.Points.Count; i++ )
					{
						var point = blend1D.Points[i];
						Add( point.Graph, $"{nodePath}/point:{i}", $"{blend1D.DisplayInfo.Name} / {blend1D.PointTitle( i )}" );
					}
				}
				else
				{
					foreach ( var (subGraph, index) in node.SubGraphs.Select( (graph, index) => (graph, index) ) )
						Add( subGraph, $"{nodePath}/graph:{index}", node.DisplayInfo.Name );
				}
			}
		}

		Add( root, "root", "Root" );

		foreach ( var parameter in root.VirtualParameters )
			Add( parameter.Graph, $"virtual:{parameter.Identifier}", $"Virtual Parameter / {parameter.Name}" );

		return result;
	}

	private static Data FindGraph( Data root, string locator, IReadOnlyDictionary<string, string> aliases = null )
	{
		locator = ExpandAliases( string.IsNullOrWhiteSpace( locator ) ? "root" : locator, aliases );
		var graphs = Graphs( root );
		return graphs.FirstOrDefault( graph => string.Equals( graph.Id, locator, StringComparison.OrdinalIgnoreCase ) )?.Graph
			?? throw new Exception( $"Unknown graph '{locator}'. Available graphs: {string.Join( ", ", graphs.Select( graph => graph.Id ) )}" );
	}

	private static string ExpandAliases( string value, IReadOnlyDictionary<string, string> aliases )
	{
		if ( string.IsNullOrWhiteSpace( value ) || aliases is null )
			return value;

		foreach ( var (alias, identifier) in aliases.OrderByDescending( pair => pair.Key.Length ) )
			value = value.Replace( $"@{alias}", identifier, StringComparison.OrdinalIgnoreCase );

		return value;
	}

	private static Node FindNode( Data graph, string identifier, IReadOnlyDictionary<string, string> aliases = null )
	{
		identifier = ExpandAliases( identifier, aliases );

		if ( string.IsNullOrWhiteSpace( identifier ) )
			throw new Exception( "A node id is required" );

		return graph.FindNode( identifier )
			?? throw new Exception( $"Graph '{Graphs( graph.Root ).First( candidate => candidate.Graph == graph ).Id}' has no node '{identifier}'" );
	}

	private static IParameter FindParameter( Data root, string identifier, IReadOnlyDictionary<string, string> aliases = null )
	{
		identifier = ExpandAliases( identifier, aliases );

		if ( string.IsNullOrWhiteSpace( identifier ) )
			throw new Exception( "A parameter id or name is required" );

		return root.AllParameters.FirstOrDefault( parameter =>
				string.Equals( parameter.Identifier, identifier, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( parameter.Name, identifier, StringComparison.OrdinalIgnoreCase ) )
			?? throw new Exception( $"No parameter matches '{identifier}'" );
	}

	private static IEnumerable<TypeDescription> NodeTypesForAuthoring()
		=> EditorTypeLibrary.GetTypes<Node>()
			.Where( type => !type.IsAbstract && (!type.HasAttribute<HideAttribute>()
				|| typeof( Editor.NodeEditor.ICommentNode ).IsAssignableFrom( type.TargetType )
				|| typeof( Editor.NodeEditor.IRerouteNode ).IsAssignableFrom( type.TargetType )) );

	private static TypeDescription FindNodeType( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new Exception( "A node type is required - use animgraph_node_types to discover them" );

		var types = NodeTypesForAuthoring()
			.Where( type =>
			{
				var node = type.Create<Node>();
				return node.CanClone && node.CanRemove;
			} )
			.ToArray();

		var exact = types.FirstOrDefault( type =>
			string.Equals( type.Name, name, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( type.FullName, name, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( DisplayInfo.ForType( type.TargetType ).Name, name, StringComparison.OrdinalIgnoreCase ) );

		if ( exact is not null )
			return exact;

		var matches = types.Where( type =>
			type.Name.Contains( name, StringComparison.OrdinalIgnoreCase )
			|| DisplayInfo.ForType( type.TargetType ).Name.Contains( name, StringComparison.OrdinalIgnoreCase ) ).ToArray();

		return matches.Length switch
		{
			1 => matches[0],
			0 => throw new Exception( $"No AnimGraph node type matches '{name}'" ),
			_ => throw new Exception( $"'{name}' matches several node types: {string.Join( ", ", matches.Select( type => type.Name ).Order() )}" )
		};
	}

	private static bool HasSerializableProperty( object target, string name )
	{
		if ( target is null || string.IsNullOrWhiteSpace( name ) )
			return false;

		return target.GetType().GetProperties( BindingFlags.Instance | BindingFlags.Public )
			.Any( candidate => candidate.CanWrite
				&& candidate.GetCustomAttribute<JsonIgnoreAttribute>() is null
				&& string.Equals( candidate.Name, name, StringComparison.OrdinalIgnoreCase ) );
	}

	private static PropertyInfo FindSerializableProperty( object target, string name, bool allowNodeCollection = false )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new Exception( "A property name is required" );

		var property = target.GetType().GetProperties( BindingFlags.Instance | BindingFlags.Public )
			.FirstOrDefault( candidate =>
				string.Equals( candidate.Name, name, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( candidate.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name, name, StringComparison.OrdinalIgnoreCase ) );

		if ( property is null )
			throw new Exception( $"{target.GetType().Name} has no property '{name}'" );

		if ( !property.CanWrite || property.GetCustomAttribute<JsonIgnoreAttribute>() is not null )
			throw new Exception( $"{target.GetType().Name}.{property.Name} is read-only" );

		if ( typeof( Data ).IsAssignableFrom( property.PropertyType ) )
			throw new Exception( $"{target.GetType().Name}.{property.Name} is managed by its owning graph" );

		if ( property.PropertyType == typeof( NodeInput ) )
			throw new Exception( $"{target.GetType().Name}.{property.Name} is a connection - use connect or disconnect" );

		if ( target is IDBasedSelectorNode )
		{
			if ( property.Name == nameof( IDBasedSelectorNode.Options ) )
				throw new Exception( "ID selector options are derived from ParameterName and cannot be edited directly" );

			if ( property.Name == nameof( IDBasedSelectorNode.ParameterIdentifier ) )
				throw new Exception( "Select an ID selector parameter with ParameterName" );
		}

		if ( target is Node && typeof( IList ).IsAssignableFrom( property.PropertyType ) && !allowNodeCollection )
			throw new Exception( $"{target.GetType().Name}.{property.Name} must be edited with item operations" );

		return property;
	}

	private static HashSet<string> ApplyValues( object target, JsonObject values, params string[] blocked )
	{
		var changed = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		if ( values is null )
			return changed;

		foreach ( var (name, value) in values )
		{
			if ( blocked.Contains( name, StringComparer.OrdinalIgnoreCase ) )
				throw new Exception( $"{target.GetType().Name}.{name} cannot be changed by this operation" );

			if ( target is Data graph && string.Equals( name, "Model", StringComparison.OrdinalIgnoreCase ) )
			{
				var modelPath = value?.GetValue<string>();
				graph.TargetModel = string.IsNullOrWhiteSpace( modelPath ) ? null : Model.Load( modelPath );
				changed.Add( "Model" );
				continue;
			}

			if ( target is Data skeletonGraph && string.Equals( name, "Skeleton", StringComparison.OrdinalIgnoreCase ) )
			{
				var skeletonPath = value?.GetValue<string>();
				skeletonGraph.TargetSkeleton = string.IsNullOrWhiteSpace( skeletonPath ) ? null : skeletonPath;
				changed.Add( "Skeleton" );
				continue;
			}

			if ( target is IDBasedSelectorNode idSelector
				&& string.Equals( name, nameof( IDBasedSelectorNode.ParameterName ), StringComparison.OrdinalIgnoreCase ) )
			{
				idSelector.ParameterIdentifier = "";
			}

			// Variation data lives on the node's data object, not the node itself
			var propertyTarget = target is VariationDataNode variationNode
				&& HasSerializableProperty( variationNode.SelectedVariationData, name )
					? variationNode.SelectedVariationData
					: target;

			var property = FindSerializableProperty( propertyTarget, name );
			var converted = value is null ? null : value.Deserialize( property.PropertyType, JsonOptions );
			property.SetValue( propertyTarget, converted );
			changed.Add( property.Name );
		}

		return changed;
	}

	private static IList GetCollection( Node node, string propertyName, out PropertyInfo property )
	{
		// Collections can live on the node's data for the variation being edited, not the node
		var target = node is VariationDataNode variationNode
			&& HasSerializableProperty( variationNode.SelectedVariationData, propertyName )
				? (object)variationNode.SelectedVariationData
				: node;

		property = FindSerializableProperty( target, propertyName, true );

		if ( property.GetValue( target ) is not IList list )
			throw new Exception( $"{target.GetType().Name}.{property.Name} is not an editable collection" );

		return list;
	}

	private static Type CollectionElementType( PropertyInfo property )
		=> property.PropertyType.IsArray
			? property.PropertyType.GetElementType()
			: property.PropertyType.GetGenericArguments().FirstOrDefault()
				?? throw new Exception( $"Cannot determine the item type for {property.DeclaringType?.Name}.{property.Name}" );

	private static object DeserializeItem( JsonNode value, Type type )
	{
		RejectManagedItemValues( value, type );

		// An input plug is always added empty, the connect operation is what fills it
		if ( type == typeof( NodeInput ) )
			return default( NodeInput );

		return value?.Deserialize( type, JsonOptions )
			?? throw new Exception( $"A {type.Name} value is required" );
	}

	private static void RejectManagedItemValues( JsonNode value, Type type )
	{
		if ( type == typeof( NodeInput ) )
		{
			var input = value?.Deserialize<NodeInput>( JsonOptions ) ?? default;
			if ( input.IsValid || !string.IsNullOrWhiteSpace( input.Identifier ) || !string.IsNullOrWhiteSpace( input.Output ) )
				throw new Exception( "Connections must be created with the connect operation" );

			return;
		}

		if ( value is not JsonObject document )
			return;

		foreach ( var property in type.GetProperties( BindingFlags.Instance | BindingFlags.Public ) )
		{
			var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
			if ( !document.Any( entry => string.Equals( entry.Key, name, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			if ( typeof( Data ).IsAssignableFrom( property.PropertyType ) )
				throw new Exception( $"{type.Name}.{property.Name} is managed by its owning graph" );

			if ( property.PropertyType == typeof( NodeInput ) )
				throw new Exception( $"{type.Name}.{property.Name} is a connection - use the connect operation" );
		}
	}

	private static void SynchronizeNode( Node node, string changedProperty = null )
	{
		if ( node is StateMachineNode machine )
			machine.SyncStates();

		if ( node is SelectorNode selector
			&& (changedProperty is null || string.Equals( changedProperty, nameof( SelectorNode.Options ), StringComparison.OrdinalIgnoreCase )) )
		{
			_ = selector.SecondaryGraph;
		}

		if ( node is Blend2DNode blend )
			blend.InitializePointGraphs();

		if ( node is Blend1DNode blend1D )
			blend1D.InitializePointGraphs();

		if ( node is IDBasedSelectorNode idSelector )
			idSelector.SynchronizeOptionsFromParameter();

		node.Update();
	}
}
