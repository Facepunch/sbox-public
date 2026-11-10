namespace Facepunch.InteropGen;

[TypeName( "bool" )]
public class ArgBool : Arg
{
	public override string ManagedType => "bool";
	public override string NativeType => "bool";
	public override string ManagedDelegateType => "int";
	public override string NativeDelegateType => "int";

	public override string ToInterop( Side side, string code = null )
	{
		code ??= Name;
		return $"{code} ? 1 : 0";
	}

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;
		return $"{code} != 0";
	}
}
