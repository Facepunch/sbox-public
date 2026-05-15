namespace Sandbox;

/// <summary>
/// Marks a method as being an RPC. This means that it can be called over the network.
/// </summary>
[AttributeUsage( AttributeTargets.Method )]
[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapMethod, "__rpc_Wrapper" )]
[CodeGenerator( CodeGeneratorFlags.Static | CodeGeneratorFlags.WrapMethod, "Sandbox.Rpc.OnCallRpc" )]
public abstract class RpcAttribute : Attribute
{
	internal RpcMode Mode { get; set; } = RpcMode.Broadcast;

	public NetFlags Flags { get; set => field = NormalizeFlags( value ); } = NetFlags.Reliable;

	internal RpcAttribute( RpcMode mode, NetFlags flags = NetFlags.Reliable )
	{
		Mode = mode;
		Flags = flags;
	}

	static NetFlags NormalizeFlags( NetFlags flags )
	{
		if ( flags.Contains( NetFlags.Unreliable ) )
			return flags & ~NetFlags.Reliable;

		return flags | NetFlags.Reliable;
	}
}

public static partial class Rpc
{
	/// <summary>
	/// Marks a method as being an RPC. It will be called for everyone.
	/// </summary>
	[AttributeUsage( AttributeTargets.Method )]
	[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapMethod, "__rpc_Wrapper" )]
	[CodeGenerator( CodeGeneratorFlags.Static | CodeGeneratorFlags.WrapMethod, "Sandbox.Rpc.OnCallRpc" )]
	public class BroadcastAttribute : RpcAttribute
	{
		public BroadcastAttribute( NetFlags flags = NetFlags.Reliable ) : base( RpcMode.Broadcast, flags )
		{
		}
	}

	/// <summary>
	/// Marks a method as being an RPC. It will only be called on the host.
	/// </summary>
	[AttributeUsage( AttributeTargets.Method )]
	[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapMethod, "__rpc_Wrapper" )]
	[CodeGenerator( CodeGeneratorFlags.Static | CodeGeneratorFlags.WrapMethod, "Sandbox.Rpc.OnCallRpc" )]
	public class HostAttribute : RpcAttribute
	{
		public HostAttribute( NetFlags flags = NetFlags.Reliable ) : base( RpcMode.Host, flags )
		{
		}
	}

	/// <summary>
	/// Marks a method as being an RPC. It will only be called on owner of this object.
	/// </summary>
	[AttributeUsage( AttributeTargets.Method )]
	[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapMethod, "__rpc_Wrapper" )]
	[CodeGenerator( CodeGeneratorFlags.Static | CodeGeneratorFlags.WrapMethod, "Sandbox.Rpc.OnCallRpc" )]
	public class OwnerAttribute : RpcAttribute
	{
		public OwnerAttribute( NetFlags flags = NetFlags.Reliable ) : base( RpcMode.Owner, flags )
		{
		}
	}

}
