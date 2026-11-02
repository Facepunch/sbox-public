using Editor.AnimGraph.Nodes;
using Editor.Mcp;
using Editor.NodeEditor;
using System.Collections;
using System.Text.Json.Nodes;

namespace Editor.AnimGraph;

public static partial class McpTools
{
	/// <summary>
	/// Apply an ordered transaction of AnimGraph edits without opening the editor. Nothing is
	/// written unless every operation succeeds. Use graph locators, node ids, property names,
	/// collection schemas and plug names from animgraph_inspect and animgraph_node_types.
	///
	/// add_node: Graph, Type, optional Alias, Values.
	/// set_node/remove_node: Graph, Node, optional Values.
	/// move_nodes: Graph, Nodes, TargetGraph. Preserves ids and internal connections; disconnect external connections first.
	/// group_nodes: Graph, Nodes, Node (pose output), optional FromOutput, Alias and Values (Name, Position). Preserves boundary connections.
	/// ungroup_node: Graph and Node. Restores the group's contents and boundary connections.
	/// layout_graph: Graph and optional Recursive. Arranges nodes without changing connections or state order.
	/// connect: Graph, FromNode, FromOutput, ToNode, ToInput.
	/// disconnect: Graph, ToNode, ToInput.
	/// set_graph: Graph and Values (Model is a model path).
	/// add/set/remove_parameter: Parameter or Alias, ParameterType, Virtual, Values.
	/// add/set/remove/move_item: Graph, Node, Property, Index, TargetIndex, Value or Values.
	/// add_variation: Variation, optional Parent and Values (Model, Preview*).
	/// set_variation: Variation and Values (Id renames it, Parent reparents it, Model).
	/// remove_variation: Variation, which also removes its children and their overrides.
	/// add/remove_override: Graph, Node, Variation.
	///
	/// Set Variation on any node or item operation to edit that variation's data - the node
	/// needs an override for it first, otherwise the edit would change what it inherits.
	///
	/// Later operations can reference an add_node, add_parameter or group_nodes alias as @alias, including
	/// inside graph locators such as root/node:@machine/state:0.
	/// </summary>
	/// <param name="path">The .ag asset path.</param>
	/// <param name="expectedRevision">Revision from animgraph_inspect or animgraph_create.</param>
	/// <param name="operations">Ordered edit operations applied as one transaction.</param>
	[McpTool( "animgraph_edit" )]
	public static EditResult Edit( string path, string expectedRevision, EditOperation[] operations )
	{
		if ( operations is null || operations.Length == 0 )
			throw new Exception( "Give at least one edit operation" );

		var session = LoadSession( path );
		EnsureWritableProjectAsset( session );
		CheckRevision( session, expectedRevision );

		var aliases = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		for ( var i = 0; i < operations.Length; i++ )
		{
			var operation = operations[i] ?? throw new Exception( $"Operation {i} is null" );

			try
			{
				ApplyOperation( session.Graph, operation, aliases );
			}
			catch ( Exception exception )
			{
				throw new Exception( $"Operation {i} ({operation.Operation}) failed: {exception.Message}", exception );
			}
		}

		var result = session.Save( expectedRevision: expectedRevision );

		return new EditResult
		{
			Path = session.Asset.Path,
			SourceFile = session.SourceFile,
			Revision = session.Revision,
			Variation = session.Graph?.SelectedVariationID,
			AssetCompiled = session.AssetCompileSucceeded,
			Compile = ToResult( result ),
			OperationsApplied = operations.Length,
			Aliases = aliases
		};
	}

	private static void ApplyOperation(
		Data root,
		EditOperation operation,
		Dictionary<string, string> aliases )
	{
		var name = operation.Operation?.Trim().ToLowerInvariant()
			?? throw new Exception( "Operation name is required" );

		// Node data is per variation, so every operation states which one it edits
		SelectVariation( root, name is "add_variation" or "set_variation" or "remove_variation" ? null : operation.Variation );

		switch ( name )
		{
			case "add_node":
				AddNode( root, operation, aliases );
				break;
			case "set_node":
				SetNode( root, operation, aliases );
				break;
			case "remove_node":
				RemoveNode( root, operation, aliases );
				break;
			case "move_nodes":
				MoveNodes( root, operation, aliases );
				break;
			case "group_nodes":
				GroupNodes( root, operation, aliases );
				break;
			case "ungroup_node":
				UngroupNode( root, operation, aliases );
				break;
			case "layout_graph":
				GraphLayout.Arrange( FindGraph( root, operation.Graph, aliases ), operation.Recursive );
				break;
			case "connect":
				Connect( root, operation, aliases );
				break;
			case "disconnect":
				Disconnect( root, operation, aliases );
				break;
			case "set_graph":
				ApplyValues(
					FindGraph( root, operation.Graph, aliases ),
					operation.Values,
					nameof( Data.Version ),
					nameof( Data.IsSubGraph ),
					nameof( Data.Kind ) );
				break;
			case "add_parameter":
				AddParameter( root, operation, aliases );
				break;
			case "set_parameter":
				SetParameter( root, operation, aliases );
				break;
			case "remove_parameter":
				RemoveParameter( root, operation, aliases );
				break;
			case "add_item":
			case "set_item":
			case "remove_item":
			case "move_item":
				EditCollection( root, operation, aliases, name );
				break;
			case "add_variation":
				AddVariation( root, operation );
				break;
			case "set_variation":
				SetVariation( root, operation );
				break;
			case "remove_variation":
				RemoveVariation( root, operation );
				break;
			case "add_override":
				SetVariationOverride( root, operation, aliases, true );
				break;
			case "remove_override":
				SetVariationOverride( root, operation, aliases, false );
				break;
			default:
				throw new Exception( $"Unknown operation '{operation.Operation}'" );
		}
	}

	private static void AddNode( Data root, EditOperation operation, Dictionary<string, string> aliases )
	{
		var graph = FindGraph( root, operation.Graph, aliases );
		var type = FindNodeType( operation.Type );
		var nodeType = new NodeType( type );

		if ( !nodeType.IsAvailableIn( graph ) )
			throw new Exception( $"{type.Name} is not available in {graph.Kind} graphs" );

		var node = type.Create<Node>();

		if ( !node.CanClone || !node.CanRemove )
			throw new Exception( $"{type.Name} is managed by its owning graph and cannot be created directly" );

		if ( node is VariationDataNode && root.SelectedVariationID != Variation.DefaultVariationID )
		{
			throw new Exception( $"A new {type.Name} starts with the data every variation inherits"
				+ " - add it on the default variation, then add_override to give this one its own" );
		}

		ApplyValues( node, operation.Values, nameof( Node.Identifier ), nameof( Node.Graph ) );
		graph.AddNode( node );
		ValidateIDSelectorParameter( node, operation.Values );
		SynchronizeNode( node );

		if ( !string.IsNullOrWhiteSpace( operation.Alias ) )
		{
			if ( !aliases.TryAdd( operation.Alias, node.Identifier ) )
				throw new Exception( $"Alias '{operation.Alias}' is already in use" );
		}
	}

	private static void SetNode( Data root, EditOperation operation, IReadOnlyDictionary<string, string> aliases )
	{
		var graph = FindGraph( root, operation.Graph, aliases );
		var node = FindNode( graph, operation.Node, aliases );

		if ( node is GlobalTransitionNode && operation.Values?.Any( value =>
			value.Key.Equals( nameof( GlobalTransitionNode.StateName ), StringComparison.OrdinalIgnoreCase ) ) is true )
			throw new Exception( "Global transition targets are managed by the state list" );

		if ( !node.CanClone && node is not GlobalTransitionNode
			&& operation.Values?.All( value => value.Key.Equals( nameof( Node.Position ), StringComparison.OrdinalIgnoreCase ) ) is not true )
			throw new Exception( $"{node.GetType().Name} is managed by its owning graph; only Position can be edited directly" );

		if ( node is StateMachineNode && operation.Values?.Any( value =>
			value.Key.Equals( nameof( StateMachineNode.States ), StringComparison.OrdinalIgnoreCase )
			|| value.Key.Equals( nameof( StateMachineNode.Conduits ), StringComparison.OrdinalIgnoreCase )
			|| value.Key.Equals( nameof( StateMachineNode.GlobalGraph ), StringComparison.OrdinalIgnoreCase )
			|| value.Key.Equals( nameof( StateMachineNode.EntryGraph ), StringComparison.OrdinalIgnoreCase ) ) is true )
		{
			throw new Exception( "State machine structure is managed through state and conduit item operations" );
		}

		if ( node is SelectorNode && operation.Values?.Any( value =>
			value.Key.Equals( nameof( SelectorNode.Options ), StringComparison.OrdinalIgnoreCase ) ) is true )
		{
			throw new Exception( "Edit selector Options with item operations so conditions stay paired" );
		}

		EnsureVariationIsWritable( root, node, operation.Values?.Select( value => value.Key ) );

		var changed = ApplyValues( node, operation.Values, nameof( Node.Identifier ), nameof( Node.Graph ) );
		ValidateIDSelectorParameter( node, operation.Values );
		SynchronizeNode( node, changed.Contains( nameof( SelectorNode.Options ) ) ? nameof( SelectorNode.Options ) : null );
	}

	private static void ValidateIDSelectorParameter( Node node, JsonObject values )
	{
		if ( node is not IDBasedSelectorNode selector
			|| values?.Any( value => value.Key.Equals( nameof( IDBasedSelectorNode.ParameterName ), StringComparison.OrdinalIgnoreCase ) ) is not true )
		{
			return;
		}

		if ( string.IsNullOrWhiteSpace( selector.ParameterName ) )
		{
			selector.ClearParameter();
			return;
		}

		var available = selector.GetAvailableParameters();
		var parameter = available
			.FirstOrDefault( candidate => string.Equals( candidate.Name, selector.ParameterName, StringComparison.OrdinalIgnoreCase ) );
		if ( parameter is not null )
		{
			selector.SelectParameter( parameter );
			return;
		}

		var names = available.Select( candidate => candidate.Name ).ToArray();
		throw new Exception( names.Length == 0
			? "ID Based Selector requires a control ID parameter"
			: $"Unknown ID selector parameter '{selector.ParameterName}'. Available parameters: {string.Join( ", ", names )}" );
	}

	private static void RemoveNode( Data root, EditOperation operation, IReadOnlyDictionary<string, string> aliases )
	{
		var graph = FindGraph( root, operation.Graph, aliases );
		var node = FindNode( graph, operation.Node, aliases );

		if ( !node.CanRemove )
			throw new Exception( $"{node.GetType().Name} is managed by its owning graph and cannot be removed" );

		graph.RemoveNode( node );
	}

	private static void Connect( Data root, EditOperation operation, IReadOnlyDictionary<string, string> aliases )
	{
		var graph = FindGraph( root, operation.Graph, aliases );
		var source = FindNode( graph, operation.FromNode, aliases );
		var target = FindNode( graph, operation.ToNode, aliases );
		var output = source.Outputs.FirstOrDefault( plug => string.Equals( plug.Identifier, operation.FromOutput, StringComparison.OrdinalIgnoreCase ) )
			?? throw new Exception( $"{source.GetType().Name} has no output '{operation.FromOutput}'" );
		var inputName = ExpandAliases( operation.ToInput, aliases );
		var input = target.Inputs.FirstOrDefault( plug => string.Equals( plug.Identifier, inputName, StringComparison.OrdinalIgnoreCase ) )
			?? throw new Exception( $"{target.GetType().Name} has no input '{operation.ToInput}'" );

		if ( output.Type != input.Type )
			throw new Exception( $"Cannot connect {LinkType( output.Type )} to {LinkType( input.Type )}" );

		input.ConnectedOutput = output;
	}

	private static void Disconnect( Data root, EditOperation operation, IReadOnlyDictionary<string, string> aliases )
	{
		var graph = FindGraph( root, operation.Graph, aliases );
		var target = FindNode( graph, operation.ToNode, aliases );
		var inputName = ExpandAliases( operation.ToInput, aliases );
		var input = target.Inputs.FirstOrDefault( plug => string.Equals( plug.Identifier, inputName, StringComparison.OrdinalIgnoreCase ) )
			?? throw new Exception( $"{target.GetType().Name} has no input '{operation.ToInput}'" );
		input.ConnectedOutput = null;
	}

	private static void AddParameter( Data root, EditOperation operation, Dictionary<string, string> aliases )
	{
		if ( !Enum.TryParse<AnimParameterType>( operation.ParameterType, true, out var type ) )
			throw new Exception( $"Unknown parameter type '{operation.ParameterType}'" );

		var name = operation.Values?["Name"]?.GetValue<string>();

		if ( string.IsNullOrWhiteSpace( name ) )
			name = operation.Parameter;

		name = string.IsNullOrWhiteSpace( name ) ? root.UniqueParameterName( type.ToString() ) : name.Trim();
		EnsureUniqueParameterName( root, name );

		IParameter parameter;

		if ( operation.Virtual )
		{
			var virtualParameter = type switch
			{
				AnimParameterType.Bool => (VirtualParameter)new BoolVirtualParameter(),
				AnimParameterType.ID => new IDVirtualParameter(),
				AnimParameterType.Float => new FloatVirtualParameter(),
				AnimParameterType.Vector => new VectorVirtualParameter(),
				AnimParameterType.Target => new TargetVirtualParameter(),
				AnimParameterType.BoneMask => new BoneMaskVirtualParameter(),
				_ => throw new ArgumentOutOfRangeException()
			};

			ApplyValues( virtualParameter, operation.Values, nameof( VirtualParameter.Identifier ), nameof( VirtualParameter.Graph ) );
			virtualParameter.Name = name;
			root.AddVirtualParameter( virtualParameter );
			parameter = virtualParameter;
		}
		else
		{
			var control = new Parameter { Name = name, Type = type };
			ApplyValues( control, operation.Values, nameof( Parameter.Identifier ), nameof( Parameter.Graph ), nameof( Parameter.Type ) );
			root.Parameters.Add( control );
			parameter = control;
		}

		if ( !string.IsNullOrWhiteSpace( operation.Alias ) )
		{
			if ( !aliases.TryAdd( operation.Alias, parameter.Identifier ) )
				throw new Exception( $"Alias '{operation.Alias}' is already in use" );
		}
	}

	private static void SetParameter( Data root, EditOperation operation, IReadOnlyDictionary<string, string> aliases )
	{
		var parameter = FindParameter( root, operation.Parameter, aliases );
		var oldName = parameter.Name;
		var requestedName = operation.Values?["Name"]?.GetValue<string>();

		if ( !string.IsNullOrWhiteSpace( requestedName )
			&& !string.Equals( requestedName, oldName, StringComparison.OrdinalIgnoreCase ) )
		{
			EnsureUniqueParameterName( root, requestedName, parameter );
		}

		var changed = ApplyValues( parameter, operation.Values, nameof( IParameter.Identifier ), nameof( IParameter.Type ), nameof( VirtualParameter.Graph ) );
		var resetSelectorOptions = changed.Contains( nameof( Parameter.EnumType ) )
			|| changed.Contains( nameof( Parameter.EnumTypeName ) );

		root.UpdateParameterReferences( parameter, oldName, resetSelectorOptions );
	}

	private static void RemoveParameter( Data root, EditOperation operation, IReadOnlyDictionary<string, string> aliases )
	{
		var parameter = FindParameter( root, operation.Parameter, aliases );
		var references = root.AllNodes.OfType<ParameterNode>()
			.Where( node => node.ParameterIdentifier == parameter.Identifier
				|| string.Equals( node.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase ) )
			.ToArray();

		foreach ( var reference in references )
		{
			reference.ParameterIdentifier = "";
			reference.ParameterName = "";
			reference.Update();
		}

		if ( parameter is VirtualParameter virtualParameter )
			root.VirtualParameters.Remove( virtualParameter );
		else
			root.Parameters.Remove( (Parameter)parameter );

		foreach ( var selector in root.AllNodes.OfType<IDBasedSelectorNode>() )
			selector.ClearParameterIfMissing();
	}

	private static void EnsureUniqueParameterName( Data root, string name, IParameter except = null )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new Exception( "Parameter names cannot be empty" );

		if ( root.AllParameters.Any( parameter => parameter != except
			&& string.Equals( parameter.Name, name, StringComparison.OrdinalIgnoreCase ) ) )
		{
			throw new Exception( $"A parameter named '{name}' already exists" );
		}
	}

	private static void EditCollection(
		Data root,
		EditOperation operation,
		IReadOnlyDictionary<string, string> aliases,
		string operationName )
	{
		var graph = FindGraph( root, operation.Graph, aliases );
		var node = FindNode( graph, operation.Node, aliases );

		if ( !node.CanClone )
			throw new Exception( $"{node.GetType().Name} is managed by its owning graph and cannot be edited directly" );

		EnsureVariationIsWritable( root, node, [operation.Property] );

		var list = GetCollection( node, operation.Property, out var property );
		var elementType = CollectionElementType( property );
		var selectorConditions = node is SelectorNode selector
			&& string.Equals( property.Name, nameof( SelectorNode.Options ), StringComparison.OrdinalIgnoreCase )
				? selector.SecondaryGraph.Nodes.OfType<SelectorConditionNode>().FirstOrDefault()
				: null;

		switch ( operationName )
		{
			case "add_item":
			{
				var item = DeserializeItem( operation.Value, elementType );
				ValidateCollectionItem( node, property.Name, item, null );
				var index = operation.Index < 0 ? list.Count : operation.Index;

				if ( index < 0 || index > list.Count )
					throw new Exception( $"Index {index} is outside 0..{list.Count}" );

				list.Insert( index, item );
				selectorConditions?.Conditions.Insert( index, default );
				break;
			}
			case "set_item":
			{
				EnsureIndex( list, operation.Index );
				var oldItem = list[operation.Index];
				var oldStateName = (oldItem as StateMachineNode.State)?.Name;

				if ( operation.Values is not null )
				{
					if ( elementType == typeof( NodeInput ) )
						throw new Exception( "Connections must be changed with connect or disconnect" );

					ApplyValues( oldItem, operation.Values );
					ValidateCollectionItem( node, property.Name, oldItem, operation.Index );
					list[operation.Index] = oldItem;
				}
				else
				{
					var item = DeserializeItem( operation.Value, elementType );
					ValidateCollectionItem( node, property.Name, item, operation.Index );
					list[operation.Index] = item;
				}

				if ( node is StateMachineNode machine
					&& list[operation.Index] is StateMachineNode.State state
					&& oldStateName != state.Name )
				{
					machine.RenameState( oldStateName, state.Name );
				}

				break;
			}
			case "remove_item":
			{
				EnsureIndex( list, operation.Index );
				var removed = list[operation.Index];

				if ( node is StateMachineNode machine && removed is StateMachineNode.State state )
				{
					machine.Conduits.RemoveAll( conduit => conduit.From == state.Name || conduit.To == state.Name );
					if ( machine.DefaultState == state.Name )
						machine.DefaultState = "";
				}

				list.RemoveAt( operation.Index );
				if ( selectorConditions is not null && operation.Index < selectorConditions.Conditions.Count )
					selectorConditions.Conditions.RemoveAt( operation.Index );
				break;
			}
			case "move_item":
			{
				EnsureIndex( list, operation.Index );
				if ( operation.TargetIndex < 0 || operation.TargetIndex >= list.Count )
					throw new Exception( $"Target index {operation.TargetIndex} is outside 0..{list.Count - 1}" );

				var condition = selectorConditions is null ? default : selectorConditions.Conditions[operation.Index];
				var item = list[operation.Index];
				list.RemoveAt( operation.Index );
				list.Insert( operation.TargetIndex, item );

				if ( selectorConditions is not null )
				{
					selectorConditions.Conditions.RemoveAt( operation.Index );
					selectorConditions.Conditions.Insert( operation.TargetIndex, condition );
				}

				break;
			}
		}

		SynchronizeNode( node, property.Name );
	}

	private static void EnsureIndex( IList list, int index )
	{
		if ( index < 0 || index >= list.Count )
			throw new Exception( $"Index {index} is outside 0..{list.Count - 1}" );
	}

	private static void ValidateCollectionItem( Node node, string property, object item, int? replacingIndex )
	{
		if ( node is not StateMachineNode machine )
			return;

		if ( string.Equals( property, nameof( StateMachineNode.States ), StringComparison.OrdinalIgnoreCase )
			&& item is StateMachineNode.State state )
		{
			if ( string.IsNullOrWhiteSpace( state.Name ) )
				throw new Exception( "State names cannot be empty" );

			if ( machine.States.Where( (_, index) => index != replacingIndex )
				.Any( candidate => string.Equals( candidate.Name, state.Name, StringComparison.OrdinalIgnoreCase ) ) )
			{
				throw new Exception( $"A state named '{state.Name}' already exists" );
			}
		}

		if ( string.Equals( property, nameof( StateMachineNode.Conduits ), StringComparison.OrdinalIgnoreCase )
			&& item is StateMachineNode.Conduit conduit )
		{
			if ( machine.States.All( state => state.Name != conduit.From ) )
				throw new Exception( $"Unknown source state '{conduit.From}'" );

			if ( machine.States.All( state => state.Name != conduit.To ) )
				throw new Exception( $"Unknown target state '{conduit.To}'" );
		}
	}
}
