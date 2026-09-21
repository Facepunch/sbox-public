namespace Facepunch.InteropGen;

[TypeName( "Guid" )]
public class ArgGuid : Arg
{
	public override string ManagedType => "Guid";
	public override string NativeType => "UniqueId_t";
}
