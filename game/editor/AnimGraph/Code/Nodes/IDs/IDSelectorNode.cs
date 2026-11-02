using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Evaluates option conditions in order every update and outputs the ID of the first true option, or <b>Default ID</b> when none are true.
/// <br/><br/><b>Use when:</b>&#160;Selecting a named ID from prioritized boolean conditions.
/// <br/><b>Important:</b>&#160;Every condition must be connected. No options or any disconnected condition produces a warning and returns the default ID.
/// </summary>
[Title( "ID Selector" ), Group( "Values" ), Icon( "list" )]
public class IDSelectorNode : Node
{
	public class Option
	{
		[Property, KeyProperty]
		public string Value { get; set; } = "";

		[Hide]
		public NodeInput Condition { get; set; }
	}

	[Property]
	public List<Option> Options { get; set; } =
	[
		new(),
		new(),
	];

	[Property]
	public string DefaultId { get; set; } = "";

	[Output( typeof( IDLink ) )]
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

	private string OptionTitle( int i ) =>
		i < Options.Count && !string.IsNullOrWhiteSpace( Options[i].Value ) ? Options[i].Value : $"Option {i + 1}";

	public override Menu CreateContextMenu( NodeUI node )
	{
		var menu = new Menu( "ID Selector" );
		menu.AddOption( "Add Option", "add", () => { Options.Add( new Option() ); Update(); } );

		if ( Options.Count > 0 )
			menu.AddOption( "Remove Last Option", "remove", () => { Options.RemoveAt( Options.Count - 1 ); Update(); } );

		return menu;
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var options = Options ?? [];
		if ( options.Count == 0 )
		{
			compiler.Warn( "ID Selector has no options" );
			return compiler.ConstID( DefaultId );
		}

		var cases = new List<(GraphBuilder.Node Condition, string Value)>();
		foreach ( var option in options )
		{
			var condition = compiler.EmitValue( this, option.Condition );
			if ( condition is null )
			{
				compiler.Warn( "ID Selector has a disconnected condition" );
				return compiler.ConstID( DefaultId );
			}

			cases.Add( (condition, option.Value) );
		}

		return compiler.Builder.IdSelect( cases, DefaultId );
	}

}
