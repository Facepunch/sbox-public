namespace Facepunch.InteropGen;

[TypeName( "byte" )]
public class ArgByte : Arg
{
	public override string ManagedType => "byte";
	public override string NativeType => "unsigned char";
}
