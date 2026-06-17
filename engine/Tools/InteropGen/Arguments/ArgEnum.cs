namespace Facepunch.InteropGen;

public class ArgEnum : Arg
{
	public Struct Type { get; set; }

	public ArgEnum( Struct t, string name )
	{
		Type = t;
		Name = name;
	}

	public override string ManagedType => Type.ManagedNameWithNamespace;
	public override string ManagedDelegateType => "long";
	public override string NativeType => Type.NativeNameWithNamespace;
	public override string NativeDelegateType => "int64";

	public override string ToInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Managed ? $"(long)({code})" : $"(int64)({code})";
	}

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Managed ? $"({ManagedType})({code})" : $"({NativeType})({code})";
	}
}
