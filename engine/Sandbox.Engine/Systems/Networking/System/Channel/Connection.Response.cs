
using Sandbox.Network;

namespace Sandbox;

public abstract partial class Connection
{
	readonly Dictionary<Guid, Action<object>> _responseWaiters = new();

	/// <summary>
	/// Send a message to this connection, wait for a response
	/// </summary>
	public Task<object> SendRequest<T>( T t )
	{
		var requestGuid = Guid.NewGuid();

		Assert.NotNull( System );

		var msg = ByteStream.Create( 256 );
		msg.Write( InternalMessageType.Request );
		msg.Write( requestGuid );
		msg.Write( InternalMessageType.Packed );

		System.Serialize( t, ref msg );
		SendStream( msg );

		msg.Dispose();

		var tcs = new TaskCompletionSource<object>();
		_responseWaiters[requestGuid] = ( o ) => tcs.SetResult( o );
		return tcs.Task;
	}

	/// <summary>
	/// Send a response message to this connection.
	/// </summary>
	public void SendResponse<T>( Guid requestId, T t )
	{
		Assert.NotNull( System );

		var msg = ByteStream.Create( 256 );
		msg.Write( InternalMessageType.Response );
		msg.Write( requestId );
		msg.Write( InternalMessageType.Packed );

		System.Serialize( t, ref msg );
		SendStream( msg );

		msg.Dispose();
	}

	/// <summary>
	/// A response to a message has arrived, route it to the correct async function
	/// </summary>
	internal void OnResponse( Guid responseTo, object obj )
	{
		if ( !_responseWaiters.Remove( responseTo, out var waiter ) )
		{
			return;
		}

		try
		{
			waiter( obj );
		}
		catch ( Exception e )
		{
			Log.Warning( e );
		}
	}
}
