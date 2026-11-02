namespace Facepunch.InteropGen;

[TypeName( "int" )]
public class ArgInt : Arg
{
	public override string ManagedType => "int";
}

[TypeName( "short" )]
public class ArgShort : Arg
{
	public override string ManagedType => "short";
}
