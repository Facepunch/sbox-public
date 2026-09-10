namespace Facepunch.InteropGen;

/// <summary>
/// A defined <see cref="Struct"/> (struct/enum/pointer) passed across the boundary. Plain structs are
/// passed by pointer; enums as their underlying integer; pointer-handles as an IntPtr.
/// </summary>
public class ArgDefinedStruct : Arg
{
	public Struct Type { get; set; }


	public ArgDefinedStruct( Struct c, string name, string[] flags )
	{
		Type = c;
		Name = name;
		Flags = flags;
	}

	public override string ManagedType => Type.ManagedNameWithNamespace;
	public override string NativeType => Type.NativeNameWithNamespace;
	public override string ManagedDelegateType => ManagedType;

	public override string NativeDelegateType => NativeType;

	public override string DelegateType( Side side, Dir dir )
	{
		if ( side == Side.Managed )
		{
			return Type.IsPointer
				? $"IntPtr /* PtrHandle:{Type.NativeName}  */"
				: dir == Dir.Outgoing && Flags == null && !Type.IsEnum && !Type.HasAttribute( "small" )
				? $"{ManagedType}*"
				: base.DelegateType( side, dir );
		}

		if ( dir == Dir.Incoming && Name != null && Flags == null && !Type.IsPointer && !Type.IsEnum && !Type.HasAttribute( "small" ) )
		{
			return $"{NativeDelegateType}*";
		}

		return base.DelegateType( side, dir );
	}

	public override string ToInterop( Side side, string code = null )
	{
		return Type.IsPointer
			? code ?? Name
			: side == Side.Managed && code == null && Name != null && Flags == null && !Type.IsEnum && !Type.HasAttribute( "small" )
			? $"&{Name}"
			: base.ToInterop( side, code );
	}

	public override string FromInterop( Side side, string code = null )
	{
		// non-small arg structs are passed as ptr, so read it as ptr
		if ( side == Side.Native && Flags == null && !Type.IsPointer && !Type.IsEnum && !Type.HasAttribute( "small" ) )
		{
			return $"*{code ?? Name}";
		}

		return base.FromInterop( side, code );
	}

	public override string DefaultValue => $"{NativeType}()";
}
