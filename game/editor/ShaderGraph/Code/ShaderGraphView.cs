using System.IO;

namespace Editor.ShaderGraph;

public class ShaderGraphView : GraphView
{
	private readonly MainWindow _window;
	private readonly UndoStack _undoStack;

	protected override string ClipboardIdent => "shadergraph";

	protected override string ViewCookie => _window?.AssetPath;

	private static bool? _cachedConnectionStyle;

	public static bool EnableGridAlignedWires
	{
		get => _cachedConnectionStyle ??= EditorCookie.Get( "shadergraph.gridwires", false );
		set => EditorCookie.Set( "shadergraph.gridwires", _cachedConnectionStyle = value );
	}

	private ConnectionStyle _oldConnectionStyle;

	public new ShaderGraph Graph
	{
		get => (ShaderGraph)base.Graph;
		set => base.Graph = value;
	}

	public Action OnNewParameterNodeCreated { get; set; }

	private readonly Dictionary<string, INodeType> AvailableNodes = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, IBlackboardParameterType> AvailableParameters = new( StringComparer.OrdinalIgnoreCase );

	public override ConnectionStyle ConnectionStyle => EnableGridAlignedWires
		? GridConnectionStyle.Instance
		: ConnectionStyle.Default;

	public ShaderGraphView( Widget parent, MainWindow window ) : base( parent )
	{
		_window = window;
		_undoStack = window.UndoStack;

		OnSelectionChanged += SelectionChanged;
	}

	protected override INodeType RerouteNodeType { get; } = new ClassNodeType( EditorTypeLibrary.GetType<Reroute>() );
	protected override INodeType CommentNodeType { get; } = new ClassNodeType( EditorTypeLibrary.GetType<CommentNode>() );

	public void AddNodeType<T>()
		where T : BaseNode
	{
		AddNodeType( EditorTypeLibrary.GetType<T>() );
	}

	public void AddNodeType( TypeDescription type )
	{
		var nodeType = new ClassNodeType( type );


		AvailableNodes.TryAdd( nodeType.Identifier, nodeType );
	}

	public void AddNodeType( string subgraphPath )
	{
		var subgraphTxt = Editor.FileSystem.Content.ReadAllText( subgraphPath );
		var subgraph = new ShaderGraph();
		subgraph.Deserialize( subgraphTxt );
		if ( !subgraph.AddToNodeLibrary ) return;
		var nodeType = new SubgraphNodeType( subgraphPath, EditorTypeLibrary.GetType<SubgraphNode>() );
		nodeType.SetDisplayInfo( subgraph );
		AvailableNodes.TryAdd( nodeType.Identifier, nodeType );
	}

	public INodeType FindNodeType( Type type )
	{
		return AvailableNodes.TryGetValue( type.FullName!, out var nodeType ) ? nodeType : null;
	}

	public IBlackboardParameterType FindParameterType( Type type )
	{
		return AvailableParameters.TryGetValue( type.FullName!, out var parameterType ) ? parameterType : null;
	}

	public void AddParameterType<T>() where T : BlackboardParameter
	{
		AddParameterType( EditorTypeLibrary.GetType<T>() );
	}

	public void AddParameterType( TypeDescription type )
	{
		var parameterType = new ClassBlackboardParameterType( type );

		AvailableParameters.TryAdd( parameterType.Identifier, parameterType );
	}

	protected override INodeType NodeTypeFromDragEvent( DragEvent ev )
	{
		if ( ev.Data.Assets.FirstOrDefault() is { } asset )
		{
			if ( asset.IsInstalled )
			{
				if ( string.Equals( Path.GetExtension( asset.AssetPath ), ".shdrfunc", StringComparison.OrdinalIgnoreCase ) )
				{
					return new SubgraphNodeType( asset.AssetPath, EditorTypeLibrary.GetType<SubgraphNode>() );
				}
				else
				{
					var realAsset = asset.GetAssetAsync().Result;
					if ( realAsset.AssetType == AssetType.ImageFile )
					{
						return new TextureNodeType( EditorTypeLibrary.GetType<TextureSampler>(), asset.AssetPath );
					}
				}
			}
		}

		if ( ev.Data.Object is BlackboardParameter blackboardParameter )
		{
			return new ParameterNodeType( blackboardParameter );
		}

		return AvailableNodes.TryGetValue( ev.Data.Text, out var type )
			? type
			: null;
	}

	protected override IEnumerable<INodeType> GetRelevantNodes( NodeQuery query )
	{
		return AvailableNodes.Values.Filter( query ).Where( x =>
		{
			if ( x is ClassNodeType classNodeType )
			{
				var targetType = classNodeType.Type.TargetType;
				if ( !Graph.IsSubgraph && targetType == typeof( FunctionResult ) ) return false;
				if ( classNodeType.Type.HasAttribute<HideAttribute>() ) return false;
				if ( Graph.IsSubgraph && targetType == typeof( Result ) ) return false;
				if ( targetType == typeof( SubgraphNode ) && classNodeType.DisplayInfo.Name == targetType.Name.ToTitleCase() ) return false;
				// Only show SubgraphInput when editing subgraphs
				if ( !Graph.IsSubgraph && targetType == typeof( SubgraphInput ) ) return false;
			}
			return true;
		} );
	}

	private static bool TryGetHandleConfig( Type type, out Type matchingType, out HandleConfig config )
	{
		if ( ShaderGraphTheme.HandleConfigs.TryGetValue( type, out config ) )
		{
			matchingType = type;
			return true;
		}

		matchingType = null;
		return false;
	}

	protected override HandleConfig OnGetHandleConfig( Type type )
	{
		if ( TryGetHandleConfig( type, out var matchingType, out var config ) )
		{
			return config with { Name = type == matchingType ? config.Name : null };
		}

		return base.OnGetHandleConfig( type );
	}

	protected override void OnPopulateNodeMenuSpecialOptions( Menu menu, Vector2 clickPos, Plug targetPlug, string filter )
	{
		base.OnPopulateNodeMenuSpecialOptions( menu, clickPos, targetPlug, filter );
		var isSubgraph = Graph.IsSubgraph;

		if ( !targetPlug.IsValid() )
		{
			var newParameterMenu = menu.AddMenu( $"Create {(isSubgraph ? "Subgraph Input" : "Parameter")}", "add" );

			foreach ( var classType in BlackboardParameter.GetRelevantParameters( AvailableParameters, Graph.IsSubgraph ).OrderBy( x => x.Type.GetAttribute<OrderAttribute>().Value ) )
			{
				var targetType = classType.Type.TargetType;

				newParameterMenu.AddOption( classType.Type.Title, classType.Type.Icon, () =>
				{
					Dialog.AskString( ( string parameterName ) =>
					{
						var parameter = CreateNewBlackboardParameter( classType );
						parameter.Name = parameterName;

						CreateNewParameterNode( parameter, clickPos, true );
					},
					$"Specify a name for the {(isSubgraph ? "subgraph input" : "parameter")}" );
				} );
			}
		}

		menu.AddSeparator();
	}

	public override void ChildValuesChanged( Widget source )
	{
		BindSystem.Flush();

		base.ChildValuesChanged( source );

		BindSystem.Flush();
	}

	public override void PushUndo( string name )
	{
		Log.Info( $"Push Undo ({name})" );
		_undoStack.PushUndo( name, Graph.UndoStackSerialize() );
		_window.OnUndoPushed();
	}

	public override void PushRedo()
	{
		Log.Info( "Push Redo" );
		_undoStack.PushRedo( Graph.UndoStackSerialize() );
		_window.SetDirty();
	}

	protected override void OnOpenContextMenu( Menu menu, Plug targetPlug )
	{
		var selectedNodes = SelectedItems.OfType<NodeUI>().ToArray();

		// TODO : Commenting this stuff out since CreateSubgraphFromSelection dosent work quite right and needs to be fixed.
		/*
		if ( selectedNodes.Length > 1 && !selectedNodes.Any( x => x.Node is BaseResult ) )
		{
			menu.AddOption( "Create Custom Node...", "add_box", () =>
			{
				const string extension = "shdrfunc";
				
				var fd = new FileDialog( null );
				fd.Title = "Create Shader Graph Function";
				fd.Directory = Project.Current.RootDirectory.FullName;
				fd.DefaultSuffix = $".{extension}";
				fd.SelectFile( $"untitled.{extension}" );
				fd.SetFindFile();
				fd.SetModeSave();
				fd.SetNameFilter( $"ShaderGraph Function (*.{extension})" );
				if ( !fd.Execute() ) return;
			
				CreateSubgraphFromSelection( fd.SelectedFile );
			} );
		}
		*/

		if ( selectedNodes.Length > 1 && selectedNodes.All( x => x.Node is IConstantNode ) )
		{
			var convertOption = menu.AddOption( $"Convert {selectedNodes.Count()} Constants to {(Graph.IsSubgraph ? "Subgraph Inputs" : "Material Parameters")}", "swap_horiz", () =>
			{
				using var undoScope = UndoScope( $"Convert {selectedNodes.Count()} Constants to {(Graph.IsSubgraph ? "Subgraph Inputs" : "Material Parameters")}" );
				var lastNode = selectedNodes.First().Node as BaseNode;
				foreach ( var selectedNode in selectedNodes )
				{
					var baseNode = selectedNode.Node as BaseNode;
					var constantNode = baseNode as IConstantNode;
					Dictionary<IPlugIn, IPlugOut> oldConnections = new();

					if ( !Graph.IsSubgraph )
					{
						foreach ( var node in Graph.Nodes )
						{
							foreach ( var input in node.Inputs )
							{
								if ( input.ConnectedOutput is null )
									continue;

								if ( input.ConnectedOutput.Node == baseNode )
								{
									oldConnections[input] = input.ConnectedOutput;

									continue;
								}
							}
						}
					}

					Graph.RemoveNode( baseNode );

					var newName = $"{(Graph.IsSubgraph ? "SubgraphInput" : "MaterialParameter")}";
					var id = 0;
					while ( Graph.HasParameterWithName( $"{newName}{id}" ) )
					{
						id++;
					}

					lastNode = ConvertConstantNodeToParameter( constantNode, $"{newName}{id}", selectedNode.Position );
					
					if ( !Graph.IsSubgraph )
					{
						// fix connections
						foreach ( var node in Graph.Nodes )
						{
							foreach ( var input in node.Inputs )
							{
								if ( input.ConnectedOutput is null && oldConnections.TryGetValue( input, out var correspondingOutput ) )
								{
									node.ConnectNode( input.Identifier, correspondingOutput.Identifier, lastNode.Identifier );

									continue;
								}
							}
						}
					}
				}

				RebuildFromGraph();

				// Select the last node in the list.
				_window.OnSelected( lastNode );
				SelectNode( lastNode );
			} );
		}

		if ( selectedNodes.Length == 1 )
		{
			var item = selectedNodes.FirstOrDefault();

			if ( item is null )
				return;

			if ( item.Node is BaseNode baseNode && baseNode is IConstantNode constantNode )
			{
				string nodeTypeTitle = constantNode.GetType() switch
				{
					Type t when t == typeof( ConstantBool ) => "Bool",
					Type t when t == typeof( ConstantInt ) => "Int",
					Type t when t == typeof( ConstantFloat ) => "Float",
					Type t when t == typeof( ConstantFloat2 ) => "Float2",
					Type t when t == typeof( ConstantFloat3 ) => "Float3",
					Type t when t == typeof( ConstantFloat4 ) => "Float4",
					Type t when t == typeof( ConstantColor ) => "Color",

					_ => throw new NotImplementedException( $"Unknown IConstantNode \"{constantNode.GetType()}\"" ),
				};

				var convertOption = menu.AddOption( $"Convert {baseNode.DisplayInfo.Name} to {nodeTypeTitle} {(Graph.IsSubgraph ? "Subgraph Input" : "Material Parameter")}", "swap_horiz", () =>
				{
					Dialog.AskString( ( string parameterName ) =>
					{
						using var undoScope = UndoScope( $"Convert {baseNode.DisplayInfo.Name} node to {nodeTypeTitle} {(Graph.IsSubgraph ? "Subgraph Input node" : "Material Parameter node")}" );

						Dictionary<IPlugIn, IPlugOut> oldConnections = new();
			
						if ( !Graph.IsSubgraph )
						{
							foreach ( var node in Graph.Nodes )
							{
								foreach ( var input in node.Inputs )
								{
									if ( input.ConnectedOutput is null )
										continue;

									if ( input.ConnectedOutput.Node == baseNode )
									{
										oldConnections[input] = input.ConnectedOutput;

										continue;
									}
								}
							}
						}

						Graph.RemoveNode( baseNode );

						var newNode = ConvertConstantNodeToParameter( constantNode, parameterName, item.Node.Position );

						if ( !Graph.IsSubgraph )
						{
							// fix connections
							foreach ( var node in Graph.Nodes )
							{
								foreach ( var input in node.Inputs )
								{
									if ( input.ConnectedOutput is null && oldConnections.TryGetValue( input, out var correspondingOutput ) )
									{
										node.ConnectNode( input.Identifier, correspondingOutput.Identifier, newNode.Identifier );

										continue;
									}
								}
							}
						}

						RebuildFromGraph();

						_window.OnSelected( newNode );
						SelectNode( newNode );
					},
					$"Specify {(Graph.IsSubgraph ? "input" : "parameter")} name for the new {nodeTypeTitle} {(Graph.IsSubgraph ? "Subgraph Input node" : "Material Parameter node")}." );
				} );
			}
		}
	}

	private BaseNode ConvertConstantNodeToParameter( IConstantNode constantNode, string parameterName, Vector2 nodePosition )
	{
		BaseNode node = null;

		if ( !Graph.IsSubgraph )
		{
			string bptFullName = constantNode switch
			{
				ConstantBool => DisplayInfo.ForType( typeof( BoolParameter ) ).Fullname,
				ConstantInt => DisplayInfo.ForType( typeof( IntParameter ) ).Fullname,
				ConstantFloat => DisplayInfo.ForType( typeof( FloatParameter ) ).Fullname,
				ConstantFloat2 => DisplayInfo.ForType( typeof( Float2Parameter ) ).Fullname,
				ConstantFloat3 => DisplayInfo.ForType( typeof( Float3Parameter ) ).Fullname,
				ConstantFloat4 => DisplayInfo.ForType( typeof( Float4Parameter ) ).Fullname,
				ConstantColor => DisplayInfo.ForType( typeof( ColorParameter ) ).Fullname,
				_ => throw new NotImplementedException(),
			};

			if ( AvailableParameters.TryGetValue( bptFullName, out var bpParameterType ) )
			{
				var parameter = CreateNewBlackboardParameter( bpParameterType );
				parameter.Name = parameterName;
				parameter.SetValue( constantNode.GetValue() );

				node = CreateNewParameterNode( parameter, nodePosition, false );
			}

			if ( node != null )
			{
				return node;
			}

			throw new Exception( $"Unable to convert constant node \"{constantNode.GetType()}\" to material parameter." );
		}
		else
		{
			string bptFullName = constantNode switch
			{
				ConstantBool => DisplayInfo.ForType( typeof( BoolSubgraphInputParameter ) ).Fullname,
				ConstantInt => DisplayInfo.ForType( typeof( IntSubgraphInputParameter ) ).Fullname,
				ConstantFloat => DisplayInfo.ForType( typeof( FloatSubgraphInputParameter ) ).Fullname,
				ConstantFloat2 => DisplayInfo.ForType( typeof( Float2SubgraphInputParameter ) ).Fullname,
				ConstantFloat3 => DisplayInfo.ForType( typeof( Float3SubgraphInputParameter ) ).Fullname,
				ConstantFloat4 => DisplayInfo.ForType( typeof( Float4SubgraphInputParameter ) ).Fullname,
				ConstantColor => DisplayInfo.ForType( typeof( ColorSubgraphInputParameter ) ).Fullname,
				_ => throw new NotImplementedException(),
			};

			// Any connections will be broken and have to be reconnected by the user since the SubgraphInput node does not have the same outputs as the constant node.

			if ( AvailableParameters.TryGetValue( bptFullName, out var bpParameterType ) )
			{
				var parameter = CreateNewBlackboardParameter( bpParameterType );
				parameter.Name = parameterName;
				parameter.SetValue( constantNode.GetValue() );

				node = CreateNewParameterNode( parameter, nodePosition, false );
			}

			if ( node != null )
			{
				return node;
			}

			throw new Exception( $"Unable to convert constant node \"{constantNode.GetType()}\" to subgraph input parameter." );
		}
	}

	protected BlackboardParameter CreateNewBlackboardParameter( IBlackboardParameterType type )
	{
		if ( type == null )
			return null;

		var parameter = type.CreateParameter( Graph );

		if ( parameter is null )
			return null;

		Graph?.AddParameter( (BlackboardParameter)parameter );

		return (BlackboardParameter)parameter;
	}

	private BaseNode CreateNewParameterNode( BlackboardParameter parameter, Vector2 position, bool selectParameter = false )
	{
		var node = BlackboardParameter.InitializeParameterNode( parameter );
		node.Graph = Graph;
		node.Position = position.SnapToGrid( GridSize );

		Graph?.AddNode( node );

		OnNodeCreated( node );

		var nodeUI = node.CreateUI( this );

		Add( nodeUI );

		OnNewParameterNodeCreated?.Invoke();

		if ( selectParameter )
			_window.OnSelected( parameter );

		return node;
	}

	private T CreateBlackboardParameter<T>( ShaderGraph graph ) where T : BlackboardParameter
	{
		return (T)FindParameterType( typeof( T ) ).CreateParameter( graph );
	}

	private void CreateSubgraphFromSelection( string filePath )
	{
		if ( string.IsNullOrWhiteSpace( filePath ) ) return;
		
		var fileName = Path.GetFileNameWithoutExtension( filePath );
		var subgraph = new ShaderGraph();
		subgraph.Title = fileName.ToTitleCase();
		subgraph.IsSubgraph = true;

		// Grab all selected nodes
		Vector2 rightmostPos = new Vector2( -9999, 0 );
		var selectedNodes = SelectedItems.OfType<NodeUI>();
		var selectedParameters = new List<Guid>();
		Dictionary<IPlugIn, IPlugOut> oldConnections = new();
		foreach ( var node in selectedNodes )
		{
			if ( node.Node is not BaseNode baseNode ) continue;

			foreach ( var input in baseNode.Inputs )
			{
				oldConnections[input] = input.ConnectedOutput;
			}
			
			subgraph.AddNode( baseNode );

			rightmostPos.y += baseNode.Position.y;
			if ( baseNode.Position.x > rightmostPos.x )
			{
				rightmostPos = rightmostPos.WithX( baseNode.Position.x );
			}
		}
		rightmostPos.y /= selectedNodes.Count();

		// Create Inputs/Constants
		var nodesToAdd = new List<BaseNode>();
		var previousOutputs = new Dictionary<string, IPlugOut>();
		foreach ( var node in subgraph.Nodes )
		{
			foreach ( var input in node.Inputs )
			{
				var correspondingOutput = oldConnections[input];
			
				var correspondingNode = subgraph.Nodes.FirstOrDefault( x => x.Identifier == correspondingOutput?.Node?.Identifier );
				if ( correspondingOutput is not null && correspondingNode is null )
				{
					var inputName = $"{input.Identifier}_{correspondingOutput?.Node?.Identifier}";
					var existingParameterNode = nodesToAdd.OfType<IParameterNode>().FirstOrDefault( x => x.Name == inputName );
					if ( input.ConnectedOutput is not null )
					{
						previousOutputs[inputName] = input.ConnectedOutput;
					}
					if ( existingParameterNode is not null )
					{
						input.ConnectedOutput = (existingParameterNode as BaseNode).Outputs.FirstOrDefault();
						continue;
					}

					BlackboardParameter parameter = null;
					
					if ( input.Type == typeof( bool ) )
					{
						parameter = CreateBlackboardParameter<BoolSubgraphInputParameter>( subgraph );
					}
					if ( input.Type == typeof( int ) )
					{
						parameter = CreateBlackboardParameter<IntSubgraphInputParameter>( subgraph );
					}
					if ( input.Type == typeof( float ) )
					{
						Log.Info( $"input.Type == typeof( float )" );
						parameter = CreateBlackboardParameter<FloatSubgraphInputParameter>( subgraph );
					}
					else if ( input.Type == typeof( Vector2 ) )
					{
						parameter = CreateBlackboardParameter<Float2SubgraphInputParameter>( subgraph );
					}
					else if ( input.Type == typeof( Vector3 ) )
					{
						parameter = CreateBlackboardParameter<Float3SubgraphInputParameter>( subgraph );
					}
					else if ( input.Type == typeof( Vector4 ) )
					{
						parameter = CreateBlackboardParameter<Float4SubgraphInputParameter>( subgraph );
					}
					else if ( input.Type == typeof( Color ) )
					{
						parameter = CreateBlackboardParameter<ColorSubgraphInputParameter>( subgraph );
					}

					if ( parameter != null )
					{
						if ( parameter is IBlackboardSubgraphInputParameter subgraphParameter )
						{
							subgraphParameter.PortOrder = nodesToAdd.Count;
						}

						subgraph.AddParameter( parameter );

						var subgraphInput = FindNodeType( typeof( SubgraphInput ) ).CreateNode( subgraph );
						subgraphInput.Position = node.Position - new Vector2( 240, 0 );
						if ( subgraphInput is SubgraphInput subgraphInputNode )
						{
							subgraphInputNode.ParameterIdentifier = parameter.Identifier;
							subgraphInputNode.OnFrame(); // Trigger update to create outputs
							input.ConnectedOutput = subgraphInputNode.Outputs.FirstOrDefault();
							nodesToAdd.Add( subgraphInputNode );
						}
					}
					else
					{
						var defaultparameter = CreateBlackboardParameter<FloatSubgraphInputParameter>( subgraph );
						defaultparameter.Name = inputName;
						defaultparameter.PortOrder = nodesToAdd.Count;

						subgraph.AddParameter( defaultparameter );

						// Default to float for unknown types
						var subgraphInput = FindNodeType( typeof( SubgraphInput ) ).CreateNode( subgraph );
						subgraphInput.Position = node.Position - new Vector2( 240, 0 );
						if ( subgraphInput is SubgraphInput subgraphInputNode )
						{
							subgraphInputNode.ParameterIdentifier = defaultparameter.Identifier;
							subgraphInputNode.OnFrame(); // Trigger update to create outputs
							input.ConnectedOutput = subgraphInputNode.Outputs.FirstOrDefault();
							nodesToAdd.Add( subgraphInputNode );
						}
					}
				}
			}
		}
		
		// Create Output/Result node
		var frNode = FindNodeType( typeof( FunctionResult ) ).CreateNode( subgraph );
		if ( frNode is FunctionResult resultNode )
		{
			resultNode.Position = rightmostPos + new Vector2( 240, 0 );
			resultNode.FunctionOutputs = new();
			foreach ( var node in subgraph.Nodes )
			{
				foreach ( var output in node.Outputs )
				{
					var correspondingNode = Graph.Nodes.FirstOrDefault( x => !subgraph.Nodes.Contains( x ) && x.Inputs.Any( x => x.ConnectedOutput == output ) );
					if ( correspondingNode is null ) continue;
					var inputName = $"{output.Identifier}_{output.Node.Identifier}";
					resultNode.FunctionOutputs.Add( new FunctionOutput
					{
						Name = inputName,
						TypeName = output.Type.FullName
					} );
					resultNode.CreateInputs();

					var input = resultNode.Inputs.FirstOrDefault( x => x is BasePlugIn plugIn && plugIn.Info.Name == inputName );
					input.ConnectedOutput = output;
					break;
				}
			}
			nodesToAdd.Add( resultNode );
		}

		// Add all the newly created nodes
		foreach ( var node in nodesToAdd )
		{
			subgraph.AddNode( node );
		}

		// Save the newly created sub-graph
		System.IO.File.WriteAllText( filePath, subgraph.Serialize() );
		var asset = AssetSystem.RegisterFile( filePath );
		MainAssetBrowser.Instance?.Local.UpdateAssetList();

		PushUndo( "Create Subgraph from Selection" );

		// Create the new subgraph node centered on the selected nodes
		Vector2 centerPos = Vector2.Zero;
		foreach ( var node in selectedNodes )
		{
			centerPos += node.Position;
		}
		centerPos /= selectedNodes.Count();
		var subgraphNode = CreateNewNode( new SubgraphNodeType( asset.RelativePath, EditorTypeLibrary.GetType<SubgraphNode>() ) ).Node as SubgraphNode;
		subgraphNode.Position = centerPos;

		// Get all the collected inputs/outputs and connect them to the new subgraph node
		foreach ( var node in Graph.Nodes )
		{
			if ( node == subgraphNode ) continue;

			if ( selectedNodes.Any( x => x.Node == node ) )
			{
				foreach ( var input in node.Inputs )
				{
					var correspondingOutput = oldConnections[input];
					if ( correspondingOutput is not null && !selectedNodes.Any( x => x.Node == correspondingOutput.Node ) )
					{
						var inputName = $"{input.Identifier}_{correspondingOutput.Node.Identifier}";
						var newInput = subgraphNode.Inputs.FirstOrDefault( x => x.Identifier == inputName );
						if ( previousOutputs.TryGetValue( inputName, out var previousOutput ) )
						{
							newInput.ConnectedOutput = previousOutput;
						}
					}
				}
			}
			else
			{
				foreach ( var input in node.Inputs )
				{
					var correspondingOutput = input.ConnectedOutput;
					if ( correspondingOutput is not null && selectedNodes.Any( x => x.Node == correspondingOutput.Node ) )
					{
						var inputName = $"{correspondingOutput.Identifier}_{correspondingOutput.Node.Identifier}";
						var newOutput = subgraphNode.Outputs.FirstOrDefault( x => x.Identifier == inputName );
						if ( newOutput is not null )
						{
							input.ConnectedOutput = newOutput;
						}
					}
				}
			}
		}

		PushRedo();
		DeleteSelection();

		// Delete all previously selected nodes
		UpdateConnections( Graph.Nodes );
	}

	private void SelectionChanged()
	{
		var item = SelectedItems
			.OfType<NodeUI>()
			.OrderByDescending( n => n is CommentUI )
			.FirstOrDefault();

		if ( !item.IsValid() )
		{
			_window.OnSelected( null );
			return;
		}

		_window.OnSelected( (BaseNode)item.Node );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		var item = SelectedItems
			.OfType<NodeUI>()
			.OrderByDescending( n => n is CommentUI )
			.FirstOrDefault();

		if ( !item.IsValid() )
		{
			_window.OnGraphViewClicked();
		}
	}

	protected override void OnNodeCreated( INode node )
	{
		if ( node is SubgraphNode subgraphNode )
		{
			subgraphNode.OnNodeCreated();
		}
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		foreach ( var node in Items )
		{
			if ( node is NodeUI nodeUI && nodeUI.Node is BaseNode baseNode )
			{
				baseNode.OnFrame();
			}
		}

		if ( _oldConnectionStyle != ConnectionStyle )
		{
			_oldConnectionStyle = ConnectionStyle;

			foreach ( var connection in Items.OfType<NodeEditor.Connection>() )
			{
				connection.Layout();
			}
		}
	}
}

