namespace Facepunch.InteropGen;

public class ArgDelegate : Arg
{
	private readonly string DelegateName;

	public ArgDelegate( string type, string name, string[] flags )
	{
		DelegateName = type;
		Name = name;
		Flags = flags;
	}

	public override string ManagedType => "IntPtr";
	public override string NativeType => "void*";

	public override string FromInterop( Side side, string code = null )
	{
		return side == Side.Native ? $"FunctionPointerToDelegate<{DelegateName}>( {code ?? Name} )" : base.FromInterop( side, code );
	}

}
