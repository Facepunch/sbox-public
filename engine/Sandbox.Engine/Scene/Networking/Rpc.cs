using Sandbox.Utility;

namespace Sandbox;

/// <summary>
/// Marks a method as being an RPC that when invoked will be called for all connected clients including the host.
/// The state of the object the RPC is called on will be up-to-date including its <see cref="GameTransform"/> and any
/// properties with the <see cref="SyncAttribute"/> or <see cref="HostSyncAttribute"/> attributes by the time the method
/// is called on remote clients. The only except is any synchronized properties marked with <see cref="SyncAttribute.Query"/> which
/// will generally only be received every network tick.
/// </summary>
[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapMethod, "__rpc_Wrapper" )]
[CodeGenerator( CodeGeneratorFlags.Static | CodeGeneratorFlags.WrapMethod, "Sandbox.Rpc.OnCallRpc" )]
[Obsolete( "Please use Rpc.Broadcast" )]
public sealed class BroadcastAttribute : RpcAttribute
{
	public BroadcastAttribute() : base( RpcMode.Broadcast ) { }

	NetPermission _permission;

	public NetPermission Permission
	{
		get => _permission;
		set
		{
			_permission = value;

			if ( _permission == NetPermission.HostOnly ) Flags |= NetFlags.HostOnly;
			if ( _permission == NetPermission.OwnerOnly ) Flags |= NetFlags.OwnerOnly;
		}
	}

	public BroadcastAttribute( NetPermission permission ) : base( RpcMode.Broadcast )
	{
		Permission = permission;
	}
}

/// <summary>
/// Marks a method as being an RPC specifically targeted to the owner of the <see cref="GameObject"/>, or the host
/// if the <see cref="GameObject"/> doesn't have an owner.
/// <br/><br/>
/// The state of the object the RPC is called on will be up-to-date including its <see cref="GameTransform"/> and any
/// properties with the <see cref="SyncAttribute"/> or <see cref="HostSyncAttribute"/> attributes by the time the method
/// is called on remote clients. The only except is any synchronized properties marked with <see cref="SyncAttribute.Query"/> which
/// will generally only be received every network tick.
/// </summary>
[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapMethod, "__rpc_Wrapper" )]
[CodeGenerator( CodeGeneratorFlags.Static | CodeGeneratorFlags.WrapMethod, "Sandbox.Rpc.OnCallRpc" )]
[Obsolete( "Please use Rpc.Owner" )]
public sealed class AuthorityAttribute : RpcAttribute
{
	public AuthorityAttribute() : base( RpcMode.Owner ) { }

	NetPermission _permission;

	public NetPermission Permission
	{
		get => _permission;
		set
		{
			_permission = value;

			if ( _permission == NetPermission.HostOnly ) Flags |= NetFlags.HostOnly;
			if ( _permission == NetPermission.OwnerOnly ) Flags |= NetFlags.OwnerOnly;
		}
	}

	public AuthorityAttribute( NetPermission permission ) : base( RpcMode.Owner )
	{
		Permission = permission;
	}
}

/// <summary>
/// Specifies who can invoke an action over the network.
/// </summary>
[Expose, Obsolete]
public enum NetPermission
{
	/// <summary>
	/// Anyone can invoke this.
	/// </summary>
	Anyone,

	/// <summary>
	/// Only the host can invoke this.
	/// </summary>
	HostOnly,

	/// <summary>
	/// Only the owner can invoke this. If the action is static, this works the same way as <see cref="HostOnly"/>.
	/// </summary>
	OwnerOnly
}

public static partial class Rpc
{
	/// <summary>
	/// The <see cref="Connection"/> that is calling this method.
	/// </summary>
	public static Connection Caller { get; private set; }

	/// <summary>
	/// The id of the <see cref="Connection"/> that is calling this method.
	/// </summary>
	public static Guid CallerId => Caller.Id;

	/// <summary>
	/// Whether we're currently being called from a remote <see cref="Connection"/>.
	/// </summary>
	public static bool IsRemoteCall { get; private set; }

	internal static Connection.Filter? Filter { get; private set; }

	internal static DisposeAction<Connection, bool> WithCaller( Connection caller )
	{
		var oldCaller = Caller;
		var oldIsRemoteCall = IsRemoteCall;

		IsRemoteCall = caller != Connection.Local;
		Caller = caller;

		unsafe
		{
			return new DisposeAction<Connection, bool>( &RestoreCaller, oldCaller, oldIsRemoteCall );
		}
	}

	static void RestoreCaller( Connection oldCaller, bool oldIsRemoteCall )
	{
		IsRemoteCall = oldIsRemoteCall;
		Caller = oldCaller;
	}

	/// <summary>
	/// Temporarily stores the connection that initiated a remote RPC.
	/// This is consumed by  <see cref="ConsumePendingRpcCaller"/> to establish the caller context.
	/// <para>
	/// This deferred approach ensures nested RPC calls maintain correct caller information:
	/// - Remote RPC arrives → sets PendingRpcCaller
	/// - First wrapper consumes it → creates WithCaller scope, resets to null
	/// - Nested RPCs see null → correctly use Connection.Local
	/// </para>
	/// </summary>
	private static Connection PendingRpcCaller { get; set; }

	/// <summary>
	/// Consumes <see cref="PendingRpcCaller"/> (if set) and resets it to null.
	/// Returns the caller to use for the current RPC execution context.
	/// </summary>
	/// <returns>The remote caller if pending, otherwise <see cref="Connection.Local"/></returns>
	internal static Connection ConsumePendingRpcCaller()
	{
		var caller = PendingRpcCaller ?? Connection.Local;
		PendingRpcCaller = null;
		return caller;
	}

	/// <summary>
	/// Sets the pending caller for a remote RPC invocation.
	/// </summary>
	/// <param name="source">The remote connection invoking the RPC</param>
	internal static DisposeAction WithPendingCaller( Connection source )
	{
		PendingRpcCaller = source;

		unsafe
		{
			return new DisposeAction( ResetPendingCaller );
		}
	}

	/// <summary>
	/// Resets the <see cref="PendingRpcCaller"/> to null.
	/// This doesn't need to restore the previous value as there will never be nested remote RPCs.
	/// </summary>
	private static void ResetPendingCaller()
	{
		PendingRpcCaller = null;
	}

	/// <summary>
	/// Resume a method from an RPC. If the RPC caller is our local connection then we'll
	/// first disable any active filter and restore it afterwards.
	/// </summary>
	/// <param name="m"></param>
	internal static void Resume( WrappedMethod m )
	{
		try
		{
			if ( Caller != Connection.Local )
			{
				m.Resume?.Invoke();
				return;
			}

			var oldFilter = Filter;
			Filter = null;

			try
			{
				m.Resume?.Invoke();
			}
			finally
			{
				Filter = oldFilter;
			}
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	/// <summary>
	/// Filter the recipients of any Rpc called in this scope to only include the specified <see cref="Connection"/> set.
	/// </summary>
	/// <param name="connections">Only send the RPC to these connections.</param>
	public static IDisposable FilterInclude( IEnumerable<Connection> connections )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Include, connections );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}

	/// <summary>
	/// Filter the recipients of any Rpc called in this scope to only include a <see cref="Connection"/> based on a predicate.
	/// </summary>
	/// <param name="predicate">Only send the RPC to connections that meet the criteria of the predicate.</param>
	public static IDisposable FilterInclude( Predicate<Connection> predicate )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Include, predicate );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}

	/// <summary>
	/// Filter the recipients of any Rpc called in this scope to only include the specified <see cref="Connection"/>.
	/// </summary>
	/// <param name="connection">Only send the RPC to this connection.</param>
	public static IDisposable FilterInclude( Connection connection )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Include, c => c == connection );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}

	/// <summary>
	/// Filter the recipients of any Rpc called in this scope to exclude a <see cref="Connection"/> based on a predicate.
	/// </summary>
	/// <param name="predicate">Exclude connections that don't meet the criteria of the predicate from receiving the RPC.</param>
	public static IDisposable FilterExclude( Predicate<Connection> predicate )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Exclude, predicate );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}

	/// <summary>
	/// Filter the recipients of any Rpc called in this scope to exclude the specified <see cref="Connection"/> set.
	/// </summary>
	/// <param name="connections">Exclude these connections from receiving the RPC.</param>
	public static IDisposable FilterExclude( IEnumerable<Connection> connections )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Exclude, connections );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}

	/// <summary>
	/// Filter the recipients of any Rpc called in this scope to exclude the specified <see cref="Connection"/>.
	/// </summary>
	/// <param name="connection">Exclude this connection from receiving the RPC.</param>
	public static IDisposable FilterExclude( Connection connection )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Exclude, c => c == connection );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}
}
