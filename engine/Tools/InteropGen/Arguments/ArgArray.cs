namespace Facepunch.InteropGen;

/// <summary>
/// Wraps an element arg as an array ("type[]"), turning its types into pointers.
/// </summary>
public class ArgArray : ArgWrapper
{
	public ArgArray( Arg val )
	{
		Base = val;
		Name = Base.Name;
	}

	public override string NativeType => $"{Base.NativeType}*";
	public override string ManagedType => $"{Base.ManagedType}*";
	public override string ManagedDelegateType => $"{Base.ManagedType}*";
	public override string NativeDelegateType => NativeType;

	// Unlike ArgWrapper (which forwards to Base), an array's delegate type keeps the pointer.
	public override string DelegateType( Side side, Dir dir )
	{
		return side == Side.Managed ? ManagedDelegateType : NativeDelegateType;
	}
}
