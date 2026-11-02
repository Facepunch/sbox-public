using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns true only when every connected boolean input is true, using short-circuit evaluation.
/// <br/><br/><b>Use when:</b>&#160;Multiple conditions must all pass.
/// <br/><b>Important:</b>&#160;Each disconnected input uses true.
/// </summary>
[Title( "And" ), Group( "Values" ), Icon( "join_inner" )]
public class AndNode : Node, IReorderableInputNode
{
	[Title( "And" )]
	public sealed class Condition
	{
		[Hide]
		public NodeInput Input { get; set; }
	}

	[Property, Title( "Inputs" )]
	public List<Condition> Conditions { get; set; } = [new(), new()];

	[Hide, JsonPropertyName( "A" )]
	public NodeInput LegacyA
	{
		set => SetLegacyInput( 0, value );
	}

	[Hide, JsonPropertyName( "B" )]
	public NodeInput LegacyB
	{
		set => SetLegacyInput( 1, value );
	}

	[Hide, JsonPropertyName( "AdditionalInputs" )]
	public List<NodeInput> LegacyAdditionalInputs
	{
		set
		{
			if ( value is null )
				return;

			Conditions.AddRange( value.Select( x => new Condition { Input = x } ) );
		}
	}

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	private readonly Dictionary<Condition, IPlugIn> _conditionPlugs = [];
	private int _nextConditionPlugId;

	protected override IEnumerable<IPlugIn> DynamicInputs
	{
		get
		{
			foreach ( var removed in _conditionPlugs.Keys.Where( x => !Conditions.Contains( x ) ).ToArray() )
				_conditionPlugs.Remove( removed );

			foreach ( var condition in Conditions )
			{
				if ( _conditionPlugs.ContainsKey( condition ) )
					continue;

				var info = new AnimPlugInfo(
					$"Condition{_nextConditionPlugId++}",
					() => "And",
					_ => condition.Input,
					( _, value ) => condition.Input = value );

				_conditionPlugs.Add( condition, new AnimPlugIn( this, info, typeof( BoolLink ) ) );
			}

			return Conditions.Select( condition => _conditionPlugs[condition] );
		}
	}

	bool IReorderableInputNode.CanReorderInput( IPlugIn input )
		=> Conditions.Count > 1 && _conditionPlugs.ContainsValue( input );

	void IReorderableInputNode.ReorderInput( IPlugIn input, IPlugIn target )
	{
		var plugs = DynamicInputs.ToList();
		var sourceIndex = plugs.IndexOf( input );
		var targetIndex = plugs.IndexOf( target );

		if ( sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex )
			return;

		var condition = Conditions[sourceIndex];
		Conditions.RemoveAt( sourceIndex );
		Conditions.Insert( targetIndex, condition );
		Update();
	}

	public override Menu CreateContextMenu( NodeUI node )
	{
		var menu = new Menu( "And" );
		menu.AddOption( "Add Input", "add", () => { Conditions.Add( new() ); Update(); } );

		if ( Conditions.Count > 0 )
			menu.AddOption( "Remove Last Input", "remove", () => { Conditions.RemoveAt( Conditions.Count - 1 ); Update(); } );

		return menu;
	}

	private void SetLegacyInput( int index, NodeInput input )
	{
		while ( Conditions.Count <= index )
			Conditions.Add( new() );

		Conditions[index].Input = input;
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( Conditions.Count == 0 )
			return compiler.ConstBool( true );

		var conditions = Conditions
			.Select( item =>
			{
				var condition = compiler.EmitValue( this, item.Input );
				return condition ?? compiler.ConstBool( true );
			} )
			.ToArray();
		return compiler.Builder.And( conditions );
	}

}
