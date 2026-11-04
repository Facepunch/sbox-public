using Editor.AnimGraph.Nodes;
using Editor.NodeEditor;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Editor.AnimGraph;

/// <summary>
/// What a graph produces, and so which nodes may be created in it.
/// </summary>
public enum GraphKind
{
	/// <summary>Produces a pose - the root graph and state graphs.</summary>
	BlendTree,

	/// <summary>A conduit's transitions and their conditions.</summary>
	TransitionConduit,

	/// <summary>A state machine's global transitions, one per state.</summary>
	GlobalTransitions,

	/// <summary>Entry state override conditions, one pin per state.</summary>
	EntryOverrides,

	/// <summary>A state's layer weight / root motion weight / bone mask.</summary>
	LayerData,

	/// <summary>A selector's per-option conditions.</summary>
	SelectorConditions,

	/// <summary>A virtual parameter's private typed value graph.</summary>
	VirtualParameterValueTree,
}

/// <summary>
/// A model bone merged onto the preview model in the editor - weapons, clothing etc.
/// </summary>
public class PreviewAttachment
{
	private Model _model;
	private string _modelPath;
	private ulong _bodyGroups = ulong.MaxValue;

	[KeyProperty]
	public Model Model
	{
		get
		{
			if ( _model is null && !string.IsNullOrWhiteSpace( _modelPath ) )
				_model = Sandbox.Model.Load( _modelPath );

			return _model;
		}
		set
		{
			if ( _model == value )
				return;

			_model = value;
			_modelPath = value?.Name;
			_bodyGroups = ulong.MaxValue;
		}
	}

	internal string ModelPath
	{
		get => _model?.Name ?? _modelPath;
		set
		{
			_model = null;
			_modelPath = value;
			_bodyGroups = ulong.MaxValue;
		}
	}

	[Model.BodyGroupMask, ShowIf( nameof( HasBodyGroups ), true )]
	public ulong BodyGroups
	{
		get => _bodyGroups == ulong.MaxValue ? Model?.Parts.DefaultMask ?? ulong.MaxValue : _bodyGroups;
		set => _bodyGroups = value;
	}

	/// <summary>
	/// Raw mask - <see cref="ulong.MaxValue"/> means "whatever the model defaults to", so it isn't saved.
	/// </summary>
	internal ulong RawBodyGroups => _bodyGroups;

	[Model.MaterialGroup, ShowIf( nameof( HasMaterialGroups ), true )]
	public string MaterialGroup { get; set; }

	/// <summary>
	/// A copy of this attachment, so variations don't share the same instance.
	/// </summary>
	internal PreviewAttachment Clone() => new()
	{
		ModelPath = ModelPath,
		BodyGroups = _bodyGroups,
		MaterialGroup = MaterialGroup,
	};

	[Hide, JsonIgnore]
	public bool HasBodyGroups => Model?.Parts.All.Sum( x => x.Choices.Count ) > 1;

	[Hide, JsonIgnore]
	public bool HasMaterialGroups => Model?.MaterialGroupCount > 0;
}

/// <summary>
/// The AnimGraph data model. Implements IGraph for the node editor framework
/// and handles JSON serialization to .ag files. Also used for nested graphs
/// owned by nodes (state pose graphs, transition condition graphs).
/// </summary>
[JsonConverter( typeof( DataConverter ) )]
public class Data : IGraph
{
	private const string DefaultTargetModel = "models/citizen/citizen.vmdl";
	private readonly VariationHierarchy _variations = CreateVariationHierarchy();
	private string _selectedVariationID = Variation.DefaultVariationID;

	private static VariationHierarchy CreateVariationHierarchy()
	{
		var hierarchy = new VariationHierarchy();
		hierarchy.GetVariation( Variation.DefaultVariationID ).TargetModelPath = DefaultTargetModel;
		return hierarchy;
	}

	[Hide, Browsable( false )]
	public int Version { get; set; } = 1;

	/// <summary>
	/// Nested graphs don't carry a model or parameters - those live on the root graph.
	/// </summary>
	[Hide, Browsable( false ), JsonIgnore]
	public bool IsSubGraph { get; internal init; }

	/// <summary>
	/// What this graph is for - decides which nodes are creatable in it.
	/// </summary>
	[Hide, Browsable( false ), JsonIgnore]
	public GraphKind Kind { get; set; } = GraphKind.BlendTree;

	/// <summary>
	/// Value graphs produce values instead of poses - no pose nodes allowed.
	/// </summary>
	[Hide, Browsable( false ), JsonIgnore]
	public bool IsValueGraph => Kind is not GraphKind.BlendTree;

	/// <summary>
	/// The variations of this graph. There's always a default variation, which is the one the
	/// graph's own target model and node data belong to. Nested graphs share the root's hierarchy.
	/// </summary>
	[Hide, Browsable( false ), JsonIgnore]
	public VariationHierarchy Variations => Root._variations;

	/// <summary>
	/// The default variation - the root of the variation hierarchy.
	/// </summary>
	[Hide, Browsable( false ), JsonIgnore]
	public Variation DefaultVariation => Variations.GetVariation( Variation.DefaultVariationID );

	/// <summary>
	/// The variation currently being edited. Decides which variation's data the nodes show.
	/// </summary>
	[Hide, Browsable( false ), JsonIgnore]
	public string SelectedVariationID
	{
		get => Root._selectedVariationID;
		set
		{
			// Take the variation's own casing, so a stale or hand typed ID still lands on it
			var variationID = Variations.TryGetCaseCorrectVariationID( value ) ?? Variation.DefaultVariationID;
			if ( Root._selectedVariationID == variationID )
				return;

			Root._selectedVariationID = variationID;

			Root.SelectedVariationChanged?.Invoke();
		}
	}

	internal event Action SelectedVariationChanged;

	/// <summary>
	/// The variation currently being edited, the default one when the selection is stale.
	/// </summary>
	[Hide, Browsable( false ), JsonIgnore]
	public Variation SelectedVariation => Variations.GetVariation( SelectedVariationID ) ?? DefaultVariation;

	/// <summary>
	/// Rename a variation, keeping its children, node overrides and current selection attached.
	/// </summary>
	public void RenameVariation( string oldVariationID, string newVariationID )
	{
		if ( oldVariationID == Variation.DefaultVariationID )
			throw new InvalidOperationException( "The default variation cannot be renamed" );

		if ( !Variations.IsValidVariation( oldVariationID ) )
			throw new InvalidOperationException( $"Unknown variation '{oldVariationID}'" );

		if ( string.IsNullOrWhiteSpace( newVariationID ) )
			throw new InvalidOperationException( "A variation name is required" );

		if ( oldVariationID == newVariationID )
			return;

		if ( Variations.TryGetCaseCorrectVariationID( newVariationID ) is { } taken && taken != oldVariationID )
			throw new InvalidOperationException( $"A variation named '{taken}' already exists" );

		var renamingSelected = SelectedVariationID == oldVariationID;
		Variations.RenameVariation( oldVariationID, newVariationID );

		foreach ( var node in Root.AllNodes.OfType<VariationDataNode>() )
			node.RenameVariationOverride( oldVariationID, newVariationID );

		if ( renamingSelected )
			SelectedVariationID = newVariationID;
	}

	/// <summary>
	/// Destroy a variation and its children, along with the data nodes hold for them - an override
	/// for a variation that no longer exists would come back if its name were ever reused.
	/// </summary>
	public void RemoveVariation( string variationID )
	{
		if ( variationID == Variation.DefaultVariationID || !Variations.IsValidVariation( variationID ) )
			return;

		string[] removed = [variationID, .. Variations.GetDescendantVariations( variationID )];

		Variations.DestroyVariation( variationID );

		foreach ( var node in Root.AllNodes.OfType<VariationDataNode>() )
		{
			foreach ( var id in removed )
				node.RemoveVariationOverride( id );
		}

		if ( removed.Contains( SelectedVariationID ) )
			SelectedVariationID = Variation.DefaultVariationID;
	}

	/// <summary>
	/// The target model this animation graph is built for, for the variation being edited.
	/// This is what the graph's poses are shown on.
	/// </summary>
	[Property, Title( "Target Model" )]
	public Model TargetModel
	{
		get => SelectedVariation.TargetModel;
		set => SelectedVariation.TargetModel = value;
	}

	/// <summary>
	/// The skeleton this animation graph animates, for the variation being edited.
	/// Every clip it plays has to be compiled against this skeleton.
	/// </summary>
	[Property, Title( "Target Skeleton" ), ResourceType( "skel" )]
	public string TargetSkeleton
	{
		get => SelectedVariation.ResolveTargetSkeletonPath();
		set => SelectedVariation.TargetSkeletonPath = value;
	}

	/// <summary>
	/// Body groups shown on the preview model in the editor, for the variation being edited.
	/// Defaults to the model's own body groups, same as a ModelRenderer would.
	/// </summary>
	[Property, Group( "Preview" ), Model.BodyGroupMask( ModelParameter = nameof( TargetModel ) ), ShowIf( nameof( HasBodyGroups ), true )]
	public ulong PreviewBodyGroups
	{
		get => SelectedVariation.PreviewBodyGroups;
		set => SelectedVariation.PreviewBodyGroups = value;
	}

	/// <summary>
	/// Material group (skin) shown on the preview model in the editor, for the variation being edited.
	/// </summary>
	[Property, Group( "Preview" ), Model.MaterialGroup( ModelParameter = nameof( TargetModel ) ), ShowIf( nameof( HasMaterialGroups ), true )]
	public string PreviewMaterialGroup
	{
		get => SelectedVariation.PreviewMaterialGroup;
		set => SelectedVariation.PreviewMaterialGroup = value;
	}

	/// <summary>
	/// Models bone merged onto the preview model in the editor, for the variation being edited -
	/// weapons, clothing etc.
	/// </summary>
	[Property, Group( "Preview" ), Title( "Bone Merged Models" )]
	public List<PreviewAttachment> PreviewAttachments
	{
		get => SelectedVariation.PreviewAttachments;
		set => SelectedVariation.PreviewAttachments = value ?? new();
	}

	/// <summary>
	/// Bone the preview camera focuses on in the editor, for the variation being edited.
	/// </summary>
	[Hide, Browsable( false )]
	public string PreviewFocusBone
	{
		get => SelectedVariation.PreviewFocusBone;
		set => SelectedVariation.PreviewFocusBone = value;
	}

	[Hide, JsonIgnore]
	public bool HasBodyGroups => TargetModel?.Parts.All.Sum( x => x.Choices.Count ) > 1;

	[Hide, JsonIgnore]
	public bool HasMaterialGroups => TargetModel?.MaterialGroupCount > 0;

	/// <summary>
	/// Control parameters - graph inputs that game code sets at runtime.
	/// </summary>
	[Hide, Browsable( false )]
	public List<Parameter> Parameters { get; } = new();

	/// <summary>
	/// Virtual parameters - named values calculated by private value graphs.
	/// </summary>
	[Hide, Browsable( false )]
	public List<VirtualParameter> VirtualParameters { get; } = new();

	[Hide, Browsable( false ), JsonIgnore]
	public IEnumerable<IParameter> AllParameters => Parameters.Cast<IParameter>().Concat( VirtualParameters );

	public void AddVirtualParameter( VirtualParameter parameter, int index = -1 )
	{
		parameter.Graph.SetRoot( Root );

		if ( index < 0 )
			VirtualParameters.Add( parameter );
		else
			VirtualParameters.Insert( index, parameter );
	}

	/// <summary>
	/// Rename a parameter and update references throughout the document, including nested graphs.
	/// </summary>
	public void RenameParameter( IParameter parameter, string name )
	{
		var oldName = parameter.Name;
		parameter.Name = name;
		UpdateParameterReferences( parameter, oldName );
	}

	/// <summary>
	/// Apply reference updates after editing parameter properties. Serialized property editors
	/// have already assigned the new name, so they supply the previous one.
	/// </summary>
	internal void UpdateParameterReferences( IParameter parameter, string oldName, bool resetSelectorOptions = false )
	{
		var disconnected = 0;

		foreach ( var node in Root.AllNodes )
		{
			if ( node is ParameterNode reference && parameter.Name != oldName
				&& (reference.ParameterIdentifier == parameter.Identifier
					|| string.Equals( reference.ParameterName, oldName, StringComparison.OrdinalIgnoreCase )) )
			{
				reference.ParameterIdentifier = parameter.Identifier;
				reference.ParameterName = parameter.Name;
				reference.Update();
			}

			if ( node is not IDBasedSelectorNode selector
				|| (selector.ParameterIdentifier != parameter.Identifier
					&& !string.Equals( selector.ParameterName, oldName, StringComparison.OrdinalIgnoreCase )) )
				continue;

			disconnected += selector.Options.Count( option => option.Pose.IsValid
				&& (resetSelectorOptions
					|| parameter.Type != AnimParameterType.ID
					|| parameter is Parameter control && !control.Options.Contains( option.Id, StringComparer.Ordinal )) );

			if ( parameter.Type != AnimParameterType.ID )
			{
				selector.ClearParameter();
				continue;
			}

			var renamed = selector.ParameterIdentifier != parameter.Identifier || selector.ParameterName != parameter.Name;
			selector.ParameterIdentifier = parameter.Identifier;
			selector.ParameterName = parameter.Name;

			if ( resetSelectorOptions )
				selector.ResetOptionsFromParameter();
			else if ( selector.SynchronizeOptionsFromParameter() )
				renamed = true;

			if ( renamed )
				selector.Update();
		}

		if ( disconnected > 0 )
			Log.Warning( $"AnimGraph: removing ID options disconnected {disconnected} selector pose{(disconnected == 1 ? "" : "s")}" );
	}

	public bool ReflectParameters( Data other, bool reflectVirtualParameters, List<string> outputLog )
	{
		var changed = false;

		foreach ( var parameter in other.Parameters.Cast<IParameter>()
			.Concat( reflectVirtualParameters ? other.VirtualParameters : [] ) )
		{
			var control = Parameters.FirstOrDefault( x => string.Equals( x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase ) );
			if ( control is not null )
			{
				if ( control.Name != parameter.Name )
				{
					outputLog.Add( $"Parameter Exists with Incorrect Case: {parameter.Name}" );
					continue;
				}

				if ( control.Type != parameter.Type )
				{
					outputLog.Add( $"Parameter Exists with Incorrect Type: {parameter.Name}" );
					continue;
				}

				outputLog.Add( $"Parameter Exists, Reflecting preview values: {parameter.Name}" );

				if ( parameter is Parameter otherControl )
					changed |= control.ReflectPreviewValues( otherControl );

				continue;
			}

			var virtualParameter = VirtualParameters.FirstOrDefault( x => string.Equals( x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase ) );
			if ( virtualParameter is not null )
			{
				if ( virtualParameter.Name != parameter.Name || virtualParameter.Type != parameter.Type )
				{
					outputLog.Add( $"Parameter Exists with Incorrect Type: {parameter.Name}" );
					continue;
				}

				outputLog.Add( $"Parameter Exists, Reflecting preview values: {parameter.Name}" );
				continue;
			}

			var created = new Parameter
			{
				Name = parameter.Name,
				Group = parameter.Group,
				Type = parameter.Type
			};

			if ( parameter is Parameter sourceControl )
				created.ReflectPreviewValues( sourceControl );

			Parameters.Add( created );
			outputLog.Add( $"Parameter Added: {parameter.Name}" );
			changed = true;
		}

		return changed;
	}

	private readonly Dictionary<string, Node> _nodes = new();

	[Hide, Browsable( false )]
	public IEnumerable<INode> Nodes => _nodes.Values;

	/// <summary>
	/// The graph that owns the model and parameters - this graph, or the root
	/// graph for nested graphs. Stamped as traversals descend from the root.
	/// </summary>
	[Hide, Browsable( false ), JsonIgnore]
	public Data Root => _root ?? this;

	private Data _root;

	internal Node Owner { get; private set; }

	internal void SetRoot( Data root ) => _root = root;

	/// <summary>
	/// All nodes in this graph and every nested graph, recursively.
	/// </summary>
	[Hide, Browsable( false ), JsonIgnore]
	public IEnumerable<Node> AllNodes
	{
		get
		{
			foreach ( var node in _nodes.Values )
			{
				yield return node;

				foreach ( var sub in node.SubGraphs )
				{
					sub._root = Root;
					sub.Owner = node;

					foreach ( var inner in sub.AllNodes )
						yield return inner;
				}
			}

			if ( !IsSubGraph )
			{
				foreach ( var parameter in VirtualParameters )
				{
					parameter.Graph.SetRoot( this );

					foreach ( var node in parameter.Graph.AllNodes )
						yield return node;
				}
			}
		}
	}

	/// <summary>
	/// Creates a nested graph seeded with its output node.
	/// </summary>
	public static Data CreateSubGraph<TOutput>( GraphKind kind = GraphKind.BlendTree ) where TOutput : Node, new()
	{
		var graph = new Data { IsSubGraph = true, Kind = kind };
		graph.AddNode( new TOutput { Position = new Vector2( 400, 0 ) } );
		return graph;
	}

	/// <summary>
	/// Creates an empty nested graph.
	/// </summary>
	public static Data CreateSubGraph( GraphKind kind )
		=> new() { IsSubGraph = true, Kind = kind };

	public void AddNode( INode node )
	{
		if ( node is not Node agNode )
			return;

		agNode.Graph = this;
		_nodes[agNode.Identifier] = agNode;
	}

	public void RemoveNode( INode node )
	{
		if ( node is not Node agNode )
			return;

		// Disconnect all inputs pointing to this node
		foreach ( var other in _nodes.Values )
		{
			foreach ( var input in other.Inputs )
			{
				if ( input.ConnectedOutput?.Node == node )
					input.ConnectedOutput = null;
			}
		}

		_nodes.Remove( agNode.Identifier );
	}

	internal void DetachNode( Node node ) => _nodes.Remove( node.Identifier );

	public Node FindNode( string identifier )
	{
		return _nodes.TryGetValue( identifier, out var node ) ? node : null;
	}

	public IEnumerable<INodeType> GetRelevantNodes( IGraph graph, IEnumerable<IPlugOut> fromOutputs = null )
	{
		return EditorTypeLibrary.GetTypes<Node>()
			.Where( x => !x.IsAbstract && !x.HasAttribute<HideAttribute>() )
			.Select( x => new NodeType( x ) )
			.Where( x => x.IsAvailableIn( this ) );
	}

	private static JsonSerializerOptions CreateOptions( bool indented )
	{
		var resolver = new DefaultJsonTypeInfoResolver();
		resolver.Modifiers.Add( info =>
		{
			if ( typeof( Node ).IsAssignableFrom( info.Type ) )
			{
				foreach ( var property in info.Properties.Where( property =>
					property.AttributeProvider is PropertyInfo member && member.GetCustomAttribute<JsonIgnoreAttribute>() is not null ).ToArray() )
					info.Properties.Remove( property );
			}

			if ( info.Type != typeof( Node ) )
				return;

			info.PolymorphismOptions = new() { TypeDiscriminatorPropertyName = "_class" };
			foreach ( var type in EditorTypeLibrary.GetTypes<Node>().Where( type => !type.IsAbstract ) )
				info.PolymorphismOptions.DerivedTypes.Add( new( type.TargetType, type.Name ) );
		} );
		return new()
		{
			WriteIndented = indented,
			PropertyNameCaseInsensitive = true,
			AllowOutOfOrderMetadataProperties = true,
			NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
			ReadCommentHandling = JsonCommentHandling.Skip,
			TypeInfoResolver = resolver,
			Converters = { new JsonStringEnumConverter( null, true ) }
		};
	}

	private static JsonSerializerOptions _options = CreateOptions( false );
	private static JsonSerializerOptions _indentedOptions = CreateOptions( true );

	/// <summary> How node data is written, so anything copying it round trips the same way. </summary>
	internal static JsonSerializerOptions SerializerOptions => _options;

	public string Serialize()
	{
		var doc = new JsonObject
		{
			["Version"] = Version,
			["nodes"] = WriteNodes( _nodes.Values )
		};

		if ( Kind != GraphKind.BlendTree )
			doc["Kind"] = Kind.ToString();

		if ( !IsSubGraph )
		{
			doc["Model"] = _variations.GetVariation( Variation.DefaultVariationID ).TargetModelPath;
			doc["Skeleton"] = _variations.GetVariation( Variation.DefaultVariationID ).ResolveTargetSkeletonPath();
			doc["parameters"] = JsonSerializer.SerializeToNode( Parameters, _indentedOptions );
			doc["virtualParameters"] = JsonSerializer.SerializeToNode( VirtualParameters, _indentedOptions );

			// The default variation lives in "Model" above, the rest carry their own model
			var variations = new JsonArray();

			foreach ( var variation in _variations.All.Where( x => x.ID != Variation.DefaultVariationID ) )
			{
				var obj = new JsonObject
				{
					["ID"] = variation.ID,
					["ParentID"] = variation.ParentID,
					["Model"] = variation.TargetModelPath,
					["Skeleton"] = variation.ResolveTargetSkeletonPath(),
				};

				WritePreviewState( obj, variation );
				variations.Add( obj );
			}

			if ( variations.Count > 0 )
				doc["variations"] = variations;

			// The default variation's preview state stays at the root, next to its model
			WritePreviewState( doc, DefaultVariation );
		}

		return doc.ToJsonString( _indentedOptions );
	}

	/// <summary>
	/// Writes a variation's preview state - what the editor shows on its model, not compiled data.
	/// </summary>
	private static void WritePreviewState( JsonObject obj, Variation variation )
	{
		if ( variation.RawPreviewBodyGroups != ulong.MaxValue )
			obj["PreviewBodyGroups"] = variation.RawPreviewBodyGroups;

		if ( !string.IsNullOrEmpty( variation.PreviewMaterialGroup ) )
			obj["PreviewMaterialGroup"] = variation.PreviewMaterialGroup;

		if ( !string.IsNullOrEmpty( variation.PreviewFocusBone ) )
			obj["PreviewFocusBone"] = variation.PreviewFocusBone;

		if ( variation.PreviewAttachments.Count == 0 )
			return;

		var attachments = new JsonArray();

		foreach ( var a in variation.PreviewAttachments.Where( x => !string.IsNullOrWhiteSpace( x.ModelPath ) ) )
		{
			var attachment = new JsonObject { ["Model"] = a.ModelPath };

			if ( a.RawBodyGroups != ulong.MaxValue )
				attachment["BodyGroups"] = a.RawBodyGroups;

			if ( !string.IsNullOrEmpty( a.MaterialGroup ) )
				attachment["MaterialGroup"] = a.MaterialGroup;

			attachments.Add( attachment );
		}

		if ( attachments.Count > 0 )
			obj["PreviewAttachments"] = attachments;
	}

	/// <inheritdoc cref="WritePreviewState"/>
	private static void ReadPreviewState( JsonElement element, Variation variation )
	{
		variation.PreviewBodyGroups = element.TryGetProperty( "PreviewBodyGroups", out var bodyGroups )
			? bodyGroups.GetUInt64()
			: ulong.MaxValue;

		variation.PreviewMaterialGroup = element.TryGetProperty( "PreviewMaterialGroup", out var matGroup ) ? matGroup.GetString() : null;
		variation.PreviewFocusBone = element.TryGetProperty( "PreviewFocusBone", out var focusBone ) ? focusBone.GetString() : null;

		variation.PreviewAttachments.Clear();

		if ( !element.TryGetProperty( "PreviewAttachments", out var attachments ) || attachments.ValueKind != JsonValueKind.Array )
			return;

		foreach ( var a in attachments.EnumerateArray() )
		{
			if ( !a.TryGetProperty( "Model", out var attachModel ) || attachModel.ValueKind != JsonValueKind.String )
				continue;

			variation.PreviewAttachments.Add( new PreviewAttachment
			{
				ModelPath = attachModel.GetString(),
				BodyGroups = a.TryGetProperty( "BodyGroups", out var bg ) ? bg.GetUInt64() : ulong.MaxValue,
				MaterialGroup = a.TryGetProperty( "MaterialGroup", out var mg ) ? mg.GetString() : null,
			} );
		}
	}

	public Data Clone( bool freshIdentifiers = false )
	{
		var clone = new Data { IsSubGraph = IsSubGraph, Kind = Kind };
		clone.Deserialize( Serialize() );

		if ( freshIdentifiers )
			clone.RegenerateNodeIdentifiers();

		return clone;
	}

	public void Deserialize( string json )
	{
		_nodes.Clear();
		Parameters.Clear();
		VirtualParameters.Clear();

		using var doc = JsonDocument.Parse( json, new() { CommentHandling = JsonCommentHandling.Skip } );
		var root = doc.RootElement;

		if ( root.TryGetProperty( "Version", out var ver ) )
			Version = ver.GetInt32();

		if ( root.TryGetProperty( "Kind", out var kind ) && Enum.TryParse<GraphKind>( kind.GetString(), out var parsedKind ) )
			Kind = parsedKind;

		_variations.Reset();

		var defaultVariation = _variations.GetVariation( Variation.DefaultVariationID );

		defaultVariation.TargetModelPath = root.TryGetProperty( "Model", out var model ) && model.ValueKind == JsonValueKind.String
			? model.GetString()
			: DefaultTargetModel;

		defaultVariation.TargetSkeletonPath = root.TryGetProperty( "Skeleton", out var skeleton ) && skeleton.ValueKind == JsonValueKind.String
			? skeleton.GetString()
			: null;

		if ( root.TryGetProperty( "variations", out var variations ) && variations.ValueKind == JsonValueKind.Array )
		{
			foreach ( var variation in variations.EnumerateArray() )
			{
				if ( !variation.TryGetProperty( "ID", out var id ) || id.ValueKind != JsonValueKind.String )
					continue;

				var added = new Variation
				{
					ID = id.GetString(),
					ParentID = variation.TryGetProperty( "ParentID", out var parentID ) ? parentID.GetString() : Variation.DefaultVariationID,
					TargetModelPath = variation.TryGetProperty( "Model", out var variationModel ) ? variationModel.GetString() : null,
					TargetSkeletonPath = variation.TryGetProperty( "Skeleton", out var variationSkeleton ) ? variationSkeleton.GetString() : null,
				};

				ReadPreviewState( variation, added );
				_variations.Add( added );
			}

			_variations.Repair();
		}

		ReadPreviewState( root, defaultVariation );

		if ( root.TryGetProperty( "parameters", out var paramsElement ) )
			Parameters.AddRange( paramsElement.Deserialize<List<Parameter>>( _options ) ?? [] );

		if ( root.TryGetProperty( "virtualParameters", out var virtualParamsElement ) )
		{
			VirtualParameters.AddRange( virtualParamsElement.Deserialize<List<VirtualParameter>>( _options ) ?? [] );

			foreach ( var parameter in VirtualParameters )
				parameter.Graph.SetRoot( this );
		}

		if ( !root.TryGetProperty( "nodes", out var nodesElement ) )
			return;

		ReadNodes( nodesElement, freshIdentifiers: false );

		if ( !IsSubGraph )
			RegisterParametersFromNodes( AllNodes );
	}

	/// <summary>
	/// Copies a subset of nodes - the GraphView clipboard format.
	/// </summary>
	public string SerializeNodes( IEnumerable<INode> nodes )
	{
		var doc = new JsonObject
		{
			["Version"] = Version,
			["nodes"] = WriteNodes( nodes )
		};

		return doc.ToJsonString( _indentedOptions );
	}

	/// <summary>
	/// Pastes nodes copied with <see cref="SerializeNodes"/>, giving them fresh
	/// identifiers and rewiring connections within the pasted set.
	/// </summary>
	public IEnumerable<INode> DeserializeNodes( string json )
	{
		using var doc = JsonDocument.Parse( json, new() { CommentHandling = JsonCommentHandling.Skip } );

		if ( !doc.RootElement.TryGetProperty( "nodes", out var nodesElement ) )
			return [];

		var created = ReadNodes( nodesElement, freshIdentifiers: true );

		foreach ( var node in created )
		{
			foreach ( var subGraph in node.SubGraphs )
				subGraph.RegenerateNodeIdentifiers();
		}

		RegisterParametersFromNodes( created.Concat(
			created.SelectMany( node => node.SubGraphs.SelectMany( graph => graph.AllNodes ) ) ) );

		return created;
	}

	internal Dictionary<string, string> RegenerateNodeIdentifiers()
	{
		var nodes = _nodes.Values.ToArray();
		var identifiers = nodes.ToDictionary( node => node.Identifier, _ => Guid.NewGuid().ToString() );
		_nodes.Clear();

		foreach ( var node in nodes )
		{
			node.Identifier = identifiers[node.Identifier];
			_nodes.Add( node.Identifier, node );
		}

		foreach ( var node in nodes )
		{
			foreach ( var input in node.Inputs.OfType<AnimPlugIn>() )
			{
				var connection = input.Info.GetInput( node );
				if ( !connection.IsValid || !identifiers.TryGetValue( connection.Identifier, out var identifier ) )
					continue;

				connection.Identifier = identifier;
				input.Info.SetInput( node, connection );
			}

			foreach ( var subGraph in node.SubGraphs )
			{
				foreach ( var (oldIdentifier, newIdentifier) in subGraph.RegenerateNodeIdentifiers() )
					identifiers.TryAdd( oldIdentifier, newIdentifier );
			}
		}

		foreach ( var machine in nodes.OfType<StateMachineNode>() )
		{
			foreach ( var state in machine.States )
			{
				state.TransitionOrder ??= [];

				for ( var i = 0; i < state.TransitionOrder.Count; i++ )
				{
					if ( identifiers.TryGetValue( state.TransitionOrder[i], out var identifier ) )
						state.TransitionOrder[i] = identifier;
				}
			}
		}

		return identifiers;
	}

	private JsonArray WriteNodes( IEnumerable<INode> nodes )
	{
		var array = new JsonArray();

		foreach ( var node in nodes.OfType<Node>() )
		{
			array.Add( JsonSerializer.SerializeToNode( node, typeof( Node ), _options ) );
		}

		return array;
	}

	private List<Node> ReadNodes( JsonElement elements, bool freshIdentifiers )
	{
		var created = new List<Node>();
		var bySavedId = new Dictionary<string, Node>();
		var connections = new List<(IPlugIn Plug, NodeInput Connection)>();

		foreach ( var element in elements.EnumerateArray() )
		{
			var node = element.Deserialize<Node>( _options )
				?? throw new JsonException( "Animation graph contains an empty node" );
			bySavedId[node.Identifier] = node;

			if ( freshIdentifiers )
				node.Identifier = Guid.NewGuid().ToString();

			node.Graph = this;
			_nodes[node.Identifier] = node;
			created.Add( node );

			foreach ( var input in node.Inputs )
			{
				if ( input is AnimPlugIn animInput
					&& animInput.Info.GetInput( node ) is { IsValid: true } connection )
					connections.Add( (input, connection) );
			}
		}

		// Resolve once every node exists - pasted nodes rewire to their copies
		// first, falling back to sources already in the graph
		foreach ( var (plug, connection) in connections )
		{
			var source = bySavedId.GetValueOrDefault( connection.Identifier ) ?? FindNode( connection.Identifier );

			if ( source?.Outputs.FirstOrDefault( o => o.Identifier == connection.Output ) is { } output )
				plug.ConnectedOutput = output;
			else
				plug.ConnectedOutput = null;
		}

		return created;
	}

	[EditorEvent.Hotload]
	internal static void InvalidateSerializationCache()
	{
		_options = CreateOptions( false );
		_indentedOptions = CreateOptions( true );
	}

	/// <summary>
	/// Ensures every parameter node references a defined parameter, so pasted
	/// graphs self-heal into the parameter list.
	/// </summary>
	private void RegisterParametersFromNodes( IEnumerable<INode> nodes )
	{
		var parameters = Root.Parameters;

		foreach ( var node in nodes.OfType<ParameterNode>() )
		{
			if ( string.IsNullOrWhiteSpace( node.ParameterName ) )
				continue;

			if ( node.ParameterType == AnimParameterType.BoneMask )
				continue;

			if ( parameters.Any( p => string.Equals( p.Name, node.ParameterName, StringComparison.OrdinalIgnoreCase ) )
				|| Root.VirtualParameters.Any( p => string.Equals( p.Name, node.ParameterName, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			parameters.Add( new Parameter { Name = node.ParameterName, Type = node.ParameterType } );
		}
	}

	/// <summary>
	/// A parameter name not taken yet - the name itself, or "name 2", "name 3"...
	/// </summary>
	public string UniqueParameterName( string baseName )
	{
		var names = Root.Parameters.Select( p => p.Name )
			.Concat( Root.VirtualParameters.Select( p => p.Name ) )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );

		if ( !names.Contains( baseName ) )
			return baseName;

		for ( var i = 2; ; i++ )
		{
			if ( !names.Contains( $"{baseName} {i}" ) )
				return $"{baseName} {i}";
		}
	}
}

/// <summary>
/// Round-trips nested graphs owned by nodes as a nodes-only document.
/// The root graph never passes through here - it serializes itself.
/// </summary>
public sealed class DataConverter : JsonConverter<Data>
{
	public override Data Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		using var doc = JsonDocument.ParseValue( ref reader );
		var graph = new Data { IsSubGraph = true };
		graph.Deserialize( doc.RootElement.GetRawText() );
		return graph;
	}

	public override void Write( Utf8JsonWriter writer, Data value, JsonSerializerOptions options )
	{
		writer.WriteRawValue( value.Serialize() );
	}
}
