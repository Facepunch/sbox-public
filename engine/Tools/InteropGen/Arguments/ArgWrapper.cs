namespace Facepunch.InteropGen;

/// <summary>
/// Base for args that decorate another arg (the wrapped <see cref="Base"/>): forwards type/marshalling
/// to it by default. Used by <see cref="ArgArray"/> and <see cref="ArgFlagsWrapper"/>.
/// </summary>
public class ArgWrapper : Arg
{
	public Arg Base;

	public override string NativeType => Base.NativeType;
	public override string NativeDelegateType => Base.NativeDelegateType;

	public override string ManagedType => Base.ManagedType;
	public override string ManagedDelegateType => Base.ManagedDelegateType;

	public override bool IsVoid => Base.IsVoid;

	public override bool IsRealArgument => Base.IsRealArgument;

	public override bool WrapsManagedCall => Base.WrapsManagedCall;

	public override string DelegateType( Side side, Dir dir )
	{
		return Base.DelegateType( side, dir );
	}
}
