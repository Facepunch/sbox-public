namespace Facepunch.InteropGen;

[TypeName( "intptr" )]
[TypeName( "void*" )]
public class ArgPointer : Arg
{
	public override string ManagedType => "IntPtr";
	public override string NativeType => "void*";

	public override string NativeDelegateType => "void*";

	public override string DelegateType( Side side, Dir dir )
	{
		if ( side == Side.Native )
		{
			return dir == Dir.Outgoing && !HasFlag( "asref" ) ? "const void*" : NativeDelegateType;
		}

		return base.DelegateType( side, dir );
	}

}
