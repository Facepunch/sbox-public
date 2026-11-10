using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Evaluates conditions in order every update and targets the value of the first true option, or <b>Default Value</b> when none are true.
/// <br/><br/><b>Use when:</b>&#160;Selecting among several float values from prioritized live conditions.
/// <br/><b>Important:</b>&#160;The output changes immediately or eases over time. Disconnected conditions are false.
/// </summary>
[Title( "Float Selector" ), Group( "Values" ), Icon( "list" )]
public class FloatSelectorNode : Node
{
	public class Option
	{
		[Property, KeyProperty]
		public string Name { get; set; } = "";

		[Property, KeyProperty]
		public float Value { get; set; } = 0.0f;

		[Hide]
		public NodeInput Condition { get; set; }
	}

	[Property]
	public List<Option> Options { get; set; } =
	[
		new(),
		new(),
	];

	/// <summary>
	/// Value used when no option's condition is true.
	/// </summary>
	[Property]
	public float DefaultValue { get; set; } = 0.0f;

	[Property]
	public GraphBuilder.Easing Easing { get; set; } = GraphBuilder.Easing.None;

	/// <summary>
	/// Time in seconds to ease to a newly selected value.
	/// </summary>
	[Property, HideIf( nameof( Easing ), GraphBuilder.Easing.None )]
	public float EaseTime { get; set; } = 0.3f;

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;

	private readonly List<IPlugIn> _optionPlugs = new();

	protected override IEnumerable<IPlugIn> DynamicInputs
	{
		get
		{
			while ( _optionPlugs.Count > Options.Count )
				_optionPlugs.RemoveAt( _optionPlugs.Count - 1 );

			while ( _optionPlugs.Count < Options.Count )
			{
				var i = _optionPlugs.Count;
				var info = new AnimPlugInfo( $"Option{i}", () => OptionTitle( i ),
					_ => i < Options.Count ? Options[i].Condition : default,
					( _, value ) => { if ( i < Options.Count ) Options[i].Condition = value; } );

				_optionPlugs.Add( new AnimPlugIn( this, info, typeof( BoolLink ) ) );
			}

			return _optionPlugs;
		}
	}

	private string OptionTitle( int i )
	{
		if ( i >= Options.Count )
			return $"Option {i + 1}";

		return string.IsNullOrWhiteSpace( Options[i].Name ) ? $"Option {i + 1}" : Options[i].Name;
	}

	public override Menu CreateContextMenu( NodeUI node )
	{
		var menu = new Menu( "Float Selector" );
		menu.AddOption( "Add Option", "add", () => { Options.Add( new Option() ); Update(); } );

		if ( Options.Count > 0 )
			menu.AddOption( "Remove Last Option", "remove", () => { Options.RemoveAt( Options.Count - 1 ); Update(); } );

		return menu;
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var cases = new List<(GraphBuilder.Node Condition, float Value)>();
		foreach ( var option in Options )
		{
			var condition = compiler.EmitValue( this, option.Condition );
			if ( condition is null )
			{
				compiler.Warn( "Float Selector has an unconnected condition input" );
				condition = compiler.ConstBool( false );
			}
			cases.Add( (condition, option.Value) );
		}

		return compiler.Builder.FloatSelector(
			cases, DefaultValue, EaseTime, Easing );
	}

}
