using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Blends one or more Local Layer or State Machine Layer definitions over a base pose.
/// <br/><br/><b>Use when:</b>&#160;Combining upper-body actions, additive motion, model-space adjustments, or state-driven layers with a base animation.
/// <br/><b>Important:</b>&#160;Invalid or empty layer slots are skipped. With no valid layers, the base pose passes through unchanged.
/// </summary>
[Title( "Layer Blend" ), Group( "Layers" ), Icon( "layers" )]
public class LayerBlendNode : Node, IReorderableInputNode
{

	[Input( typeof( PoseLink ) )]
	public NodeInput Base { get; set; }

	[Property, Hide]
	public List<NodeInput> Layers { get; set; } = [default];

	/// <summary>
	/// Sample root motion only from the base pose, ignoring layer root motion.
	/// </summary>
	[Property]
	public bool OnlySampleBaseRootMotion { get; set; } = true;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Blue;

	private readonly List<IPlugIn> _layerPlugs = new();

	protected override IEnumerable<IPlugIn> DynamicInputs
	{
		get
		{
			while ( _layerPlugs.Count > Layers.Count )
				_layerPlugs.RemoveAt( _layerPlugs.Count - 1 );

			while ( _layerPlugs.Count < Layers.Count )
			{
				var i = _layerPlugs.Count;
				var info = new AnimPlugInfo( "Layer", i, index => $"Layer {index}",
					( _, index ) => index < Layers.Count ? Layers[index] : default,
					( _, index, value ) => { if ( index < Layers.Count ) Layers[index] = value; } );

				_layerPlugs.Add( new AnimPlugIn( this, info, typeof( LayerLink ) ) );
			}

			return _layerPlugs;
		}
	}

	bool IReorderableInputNode.CanReorderInput( IPlugIn input ) => _layerPlugs.Count > 1 && _layerPlugs.Contains( input );
	void IReorderableInputNode.ReorderInput( IPlugIn input, IPlugIn target ) => ReorderInput( input, target, Layers, _layerPlugs );

	public override Menu CreateContextMenu( NodeUI node )
	{
		var menu = new Menu( "Layer Blend" );
		menu.AddOption( "Add Layer", "add", () => { Layers.Add( default ); Update(); } );

		if ( Layers.Count > 1 )
			menu.AddOption( "Remove Last Layer", "remove", () => { Layers.RemoveAt( Layers.Count - 1 ); Update(); } );

		return menu;
	}

	private readonly record struct CompiledLayer(
		GraphBuilder.Node Input,
		GraphBuilder.Node Weight,
		GraphBuilder.Node BoneMask,
		GraphBuilder.Node RootMotionWeight,
		GraphBuilder.LayerBlendMode BlendMode,
		bool IsSynchronized,
		bool IgnoreEvents,
		StateMachineLayerNode StateMachineLayer );


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var basePose = compiler.EmitPose( this, Base, "Layer Blend" );
		var compiledLayers = new List<CompiledLayer>();

		for ( var i = 0; i < Layers.Count; i++ )
		{
			switch ( GetConnectedNode( Layers[i] ) )
			{
				case LocalLayerNode layer:
					{
						if ( layer.GetConnectedNode( layer.Input ) is StateMachineNode )
							compiler.Warn( "State machines must be layered with a State Machine Layer", layer );

						var mask = compiler.EmitValue( layer, layer.BoneMask );

						if ( layer.BlendMode is GraphBuilder.LayerBlendMode.ModelSpace or GraphBuilder.LayerBlendMode.ModelSpaceRotationOnly or GraphBuilder.LayerBlendMode.ModelSpaceHierarchical && mask is null )
							compiler.Warn( "Model space layers require a bone mask", layer );

						var inputIdx = compiler.EmitPose( layer, layer.Input, "Local Layer", synchronized: layer.IsSynchronized );
						compiler.SetEmitted( layer, inputIdx );

						compiledLayers.Add( new(
							inputIdx,
							compiler.EmitValue( layer, layer.Weight ),
							mask,
							compiler.EmitValue( layer, layer.RootMotionWeight ),
							layer.BlendMode,
							layer.IsSynchronized,
							layer.IgnoreEvents,
							null ) );
						break;
					}

				case StateMachineLayerNode layer:
					{
						if ( layer.GetConnectedNode( layer.StateMachine ) is not StateMachineNode )
						{
							compiler.Warn( "State Machine Layer requires a State Machine connected", layer );
							break;
						}

						var stateMachineIdx = compiler.EmitPose( layer, layer.StateMachine, "State Machine Layer", synchronized: layer.IsSynchronized );
						compiler.SetEmitted( layer, stateMachineIdx );

						compiledLayers.Add( new(
							stateMachineIdx,
							default,
							default,
							default,
							layer.BlendMode,
							layer.IsSynchronized,
							layer.IgnoreEvents,
							layer ) );
						break;
					}

				default:
					compiler.Warn( $"Layer Blend has nothing connected to Layer {i}", this );
					break;
			}
		}

		if ( compiledLayers.Count == 0 )
		{
			compiler.Warn( "Layer Blend has no connected layers", this );
			return basePose;
		}

		var layers = compiler.Builder.Layers( basePose );
		GraphBuilder.Node emitted = layers;

		foreach ( var layer in compiledLayers )
		{
			if ( layer.StateMachineLayer is { } stateMachineLayer )
			{
				var layerIndex = layers.AddStateMachineLayer( layer.Input, new GraphBuilder.LayerSettings
				{
					BlendMode = layer.BlendMode,
					Synchronized = layer.IsSynchronized,
					IgnoreEvents = layer.IgnoreEvents,
				} );
				compiler.RegisterLayerDebug( stateMachineLayer, emitted, layerIndex );
				continue;
			}

			layers.AddLayer(
				layer.Input,
				new GraphBuilder.LayerSettings
				{
					Weight = layer.Weight,
					BoneMask = layer.BoneMask,
					BlendMode = layer.BlendMode,
					Synchronized = layer.IsSynchronized,
					IgnoreEvents = layer.IgnoreEvents,
					RootMotionWeight = layer.RootMotionWeight,
				} );
		}

		layers.IncludeLayerRootMotion( !OnlySampleBaseRootMotion );

		return emitted;
	}

}
