namespace Facepunch.InteropGen;

/// <summary>
/// A native class passed across the boundary as a pointer (or, for handle/resource-handle types, as a
/// handle id), with the conversions each of those cases needs.
/// </summary>
public class ArgDefinedClass : Arg
{
	public Class Class { get; set; }


	public ArgDefinedClass( Class c, string name, string[] flags )
	{
		Class = c;
		Name = name;
		Flags = flags;
	}

	public override string ManagedType => Class.IsHandleType ? $"global::{Class.HandleIndex}" : "global::" + Class.ManagedNameWithNamespace;


	public override string ManagedDelegateType => "IntPtr";

	public override string DelegateType( Side side, Dir dir )
	{
		if ( side == Side.Managed )
		{
			return (Class.IsHandleType || Class.IsChildHandleType) && dir == Dir.Incoming ? "int" : "IntPtr";
		}

		return Class.IsResourceHandle
			? $"{Class.ResourceHandleName}Strong*"
			: dir == Dir.Outgoing && (Class.IsHandleType || Class.IsChildHandleType)
			? "int"
			: IsReturn ? $"const {Class.NativeNameWithNamespace}*" : NativeType;
	}


	public override string NativeType => Class.IsResourceHandle ? $"{Class.ResourceHandleName}Strong*" : $"{Class.NativeNameWithNamespace}*";

	public override string NativeDelegateType => Class.IsResourceHandle
				? $"{Class.ResourceHandleName}Strong*"
				: Class.IsHandleType || Class.IsChildHandleType ? "int" : NativeType;

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		if ( Class.IsHandleType )
		{
			if ( side == Side.Managed )
			{
				return $"Sandbox.HandleIndex.Get<{Class.HandleIndex}>( {code} )";
			}
		}

		if ( Class.IsResourceHandle )
		{
			if ( side == Side.Native )
			{
				// Using custom functions to call ->GetHandle() so we can
				// handle if {code} is null in it by returning an invalid handle
				return $"ResourceHandle_GetHandle( {code} )";
			}
		}

		return side == Side.Native ? $"({NativeType}){code}" : base.FromInterop( side, code );
	}

	public override string ToInterop( Side side, string code = null )
	{
		code ??= Name;

		if ( Class.IsHandleType || Class.IsChildHandleType )
		{
			return side == Side.Native ? $"GetManagedHandle( {code} )" : $"{code} == null ? IntPtr.Zero : {code}.native";
		}

		if ( Class.IsResourceHandle )
		{
			if ( side == Side.Native && IsReturn )
			{
				return $"new {Class.ResourceHandleName}StrongCopyable( {code} )";
			}
		}

		//
		// Passing a managed class to native - we just use the .NativePointer property
		// Which should be using the NativePointer class to create a GCHandle.
		//
		return side == Side.Managed && !Class.Native
			? $" ( {code} == null ? IntPtr.Zero : Sandbox.InteropSystem.GetAddress( {code}, true ) )"
			: side == Side.Native && !Class.Native
			? $" ( {code} == nullptr ? nullptr : {code}->ptr() )"
			: side == Side.Native ? $"{code}" : base.ToInterop( side, code );
	}

}
