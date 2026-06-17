namespace Facepunch.InteropGen;

public class ArgLiteral : Arg
{
	private readonly string Value;

	public ArgLiteral( string val )
	{
		Value = val;
	}

	public override string ManagedType => "string";
	public override string ManagedDelegateType => "string";
	public override string NativeType => "const char*";

	public override bool IsRealArgument => false;

	public override string FromInterop( Side side, string code = null )
	{
		return $"/* literal */ {Value}";
	}
}
