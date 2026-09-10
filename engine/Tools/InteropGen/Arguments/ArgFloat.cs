namespace Facepunch.InteropGen;

[TypeName( "float" )]
public class ArgFloat : Arg
{
	public override string ManagedType => "float";
}

[TypeName( "double" )]
public class ArgDouble : Arg
{
	public override string ManagedType => "double";
}
