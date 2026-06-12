namespace Facepunch.InteropGen;

[TypeName( "ushort" )]
public class ArgUShort : Arg
{
	public override string ManagedType => "ushort";
	public override string NativeType => "unsigned short";
}
