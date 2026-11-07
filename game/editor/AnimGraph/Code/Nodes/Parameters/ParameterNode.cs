using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Reads a graph parameter by name and exposes its current value.
/// <br/><br/><b>Use when:</b>&#160;Driving animation behavior from values supplied to the AnimGraph.
/// <br/><b>Important:</b>&#160;The parameter name and type must match the graph parameter definition.
/// </summary>
public abstract class ParameterNode : Node, IParameterReference
{
	[Hide]
	public string ParameterIdentifier { get; set; } = "";

	[Property, Editor( "AnimGraphParameterName" )]
	public string ParameterName { get; set; } = "";

	[JsonIgnore, Hide, Browsable( false )]
	public abstract AnimParameterType ParameterType { get; }

	IParameter IParameterReference.ResolveParameter()
		=> Root is { } root ? ParameterReferences.Resolve( root.AllParameters, ParameterIdentifier, ParameterName, ParameterType ) : null;

	public override Color PrimaryColor => Theme.Yellow;

	internal override string LiveOutputLabel( string identifier )
		=> HasLiveDebugValue ? ValueDebug.ShortValue( OutputKind, LiveDebugValue ) : null;

	public override DisplayInfo DisplayInfo
	{
		get
		{
			var info = TypeDisplayInfo;
			if ( !string.IsNullOrWhiteSpace( ParameterName ) )
				info.Name = ParameterName;
			return info;
		}
	}


	internal override GraphBuilder.Node Compile( Compiler compiler ) => compiler.EmitParameterReference( this );

}
