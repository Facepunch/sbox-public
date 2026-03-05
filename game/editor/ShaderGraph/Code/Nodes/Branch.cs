
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// If True, do this, if False, do that.
/// Give it a name to use a bool attribute.
/// Use no name to use condition from A and B inputs.
/// </summary>
[Title( "Branch" ), Category( "Logic" ), Icon( "alt_route" )]
public sealed class Branch : ShaderNode
{
	[Hide]
	public override string Title => UseCondition ?
		$"{DisplayInfo.For( this ).Name} (A {Op} B)" :
		$"{DisplayInfo.For( this ).Name} ({Name})";

	[Hide]
	private bool UseCondition => string.IsNullOrWhiteSpace( Name );

	[Input, Hide]
	public NodeInput True { get; set; }

	[Input, Hide]
	public NodeInput False { get; set; }

	[Input, Hide]
	public NodeInput A { get; set; }

	[Input, Hide]
	public NodeInput B { get; set; }

	public string Name { get; set; } = "";

	public bool IsAttribute { get; set; } = true;

	public enum OperatorType
	{
		Equal,
		NotEqual,
		GreaterThan,
		LessThan,
		GreaterThanOrEqual,
		LessThanOrEqual
	}

	[HideIf( nameof( UseCondition ), false )]
	public OperatorType Operator { get; set; }

	[HideIf( nameof( UseCondition ), true )]
	public bool Enabled { get; set; }

	[InlineEditor]
	public GenericParameterUI UI { get; set; }

	[Hide]
	private string Op
	{
		get
		{
			return Operator switch
			{
				OperatorType.Equal => "==",
				OperatorType.NotEqual => "!=",
				OperatorType.GreaterThan => ">",
				OperatorType.LessThan => "<",
				OperatorType.GreaterThanOrEqual => ">=",
				OperatorType.LessThanOrEqual => "<=",
				_ => throw new NotImplementedException(),
			};
		}
	}

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var useCondition = UseCondition;
		var results = compiler.Result( True, False, 0.0f, 0.0f );
		var resultA = useCondition ? compiler.ResultOrDefault( A, 0.0f ) : default;
		var resultB = useCondition ? compiler.ResultOrDefault( B, 0.0f ) : default;

		return new NodeResult( results.Item1.Components, $"{(useCondition ?
			$"{resultA.Cast( 1 )} {Op} {resultB.Cast( 1 )}" : compiler.ResultParameter( Name, Enabled, default, default, false, IsAttribute, UI ))} ?" +
			$" {results.Item1} :" +
			$" {results.Item2}" );
	};
}
