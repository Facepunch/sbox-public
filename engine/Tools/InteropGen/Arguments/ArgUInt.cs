namespace Facepunch.InteropGen;

[TypeName( "uint" )]
public class ArgUInt : Arg
{
	public override string ManagedType => "uint";
	public override string NativeType => "unsigned int";
}
