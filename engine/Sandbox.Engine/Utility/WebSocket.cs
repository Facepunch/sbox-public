using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;

namespace Sandbox;

/// <summary>
/// A WebSocket client for connecting to external services.
/// </summary>
/// <remarks>
/// Events handlers will be called on the synchronization context that Connect was called on.
/// </remarks>
public sealed class WebSocket : IDisposable
{
	/// <summary>
	/// Event handler which processes text messages from the WebSocket service.
	/// </summary>
	/// <param name="message">The message text that was received.</param>
	public delegate void MessageReceivedHandler( string message );

	/// <summary>
	/// Event handler which processes binary messages from the WebSocket service.
	/// </summary>
	/// <param name="data">The binary message data that was received.</param>
	public delegate void DataReceivedHandler( Span<byte> data );

	/// <summary>
	/// Event handler which fires when the WebSocket disconnects from the server.
	/// </summary>
	/// <param name="status">The close status code from the server, or 0 if there was none. See known values here: https://developer.mozilla.org/en-US/docs/Web/API/CloseEvent</param>
	/// <param name="reason">The reason string for closing the connection. This may not be populated, may be from the server, or may be a client exception message.</param>
	public delegate void DisconnectedHandler( int status, string reason );

	private class Message
	{
		public WebSocketMessageType Type;
		public ArraySegment<byte> Data; // this must be returnable to the pool
	}

	[SkipHotload]
	private CancellationTokenSource _cts;
	[SkipHotload]
	private ClientWebSocket _socket;
	[SkipHotload]
	private readonly Channel<Message> _outgoing;
	private readonly int _maxMessageSize;
	private bool _dispatchedDisconnect;
	private bool _isShuttingDown;

	/// <summary>
	/// Returns true as long as a WebSocket connection is established.
	/// </summary>
	public bool IsConnected => _socket?.State == WebSocketState.Open;

	/// <summary>
	/// Get the sub-protocol that was negotiated during the opening handshake.
	/// </summary>
	public string SubProtocol => _socket?.SubProtocol;

	/// <summary>
	/// Event which fires when a text message is received from the server.
	/// </summary>
	public event MessageReceivedHandler OnMessageReceived;

	/// <summary>
	/// Event which fires when a binary message is received from the server.
	/// </summary>
	public event DataReceivedHandler OnDataReceived;

	/// <summary>
	/// Event which fires when the connection to the WebSocket service is lost, for any reason.
	/// </summary>
	public event DisconnectedHandler OnDisconnected;

	/// <summary>
	/// Enable or disable compression for the websocket. If the server supports it, compression will be enabled for all messages.
	/// Note: compression is disabled by default, and can be dangerous if you are sending secrets across the network.
	/// </summary>
	public bool EnableCompression
	{
		set
		{
			if ( value )
			{
				_socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions
				{
					ServerContextTakeover = false,
					ClientMaxWindowBits = 15
				};
			}
			else
			{
				_socket.Options.DangerousDeflateOptions = null;
			}
		}
	}

	/// <summary>
	/// Initialized a new WebSocket client.
	/// </summary>
	/// <param name="maxMessageSize">The maximum message size to allow from the server, in bytes. Default 64 KiB.</param>
	public WebSocket( int maxMessageSize = 64 * 1024 )
	{
		if ( maxMessageSize <= 0 || maxMessageSize > 4 * 1024 * 1024 )
		{
			throw new ArgumentOutOfRangeException( nameof( maxMessageSize ) );
		}

		_maxMessageSize = Math.Max( maxMessageSize, 4096 );

		_cts = new CancellationTokenSource();
		_socket = new ClientWebSocket();

		_outgoing = Channel.CreateBounded<Message>( new BoundedChannelOptions( 10 )
		{
			SingleReader = true,
			SingleWriter = false,
		} );

		// auto-disposes this when the TaskSource generation changes
		TaskSource.Cancellation.Register( () =>
		{
			_isShuttingDown = true;
			Dispose();
		} );
	}

	~WebSocket()
	{
		Dispose();
	}

	/// <summary>
	/// Cleans up resources used by the WebSocket client. This will also immediately close the connection if it is currently open.
	/// </summary>
	public void Dispose()
	{
		lock ( this )
		{
			DispatchDisconnect( WebSocketCloseStatus.Empty, "Disposed" );

			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;

			_socket?.Dispose();
			_socket = null;

			_outgoing.Writer.TryComplete();
		}

		GC.SuppressFinalize( this );
	}

	/// <summary>
	/// Add a sub-protocol to be negotiated during the WebSocket connection handshake.
	/// </summary>
	/// <param name="protocol"></param>
	public void AddSubProtocol( string protocol )
	{
		EnsureNotDisposed();

		if ( _socket.State != WebSocketState.None )
		{
			throw new InvalidOperationException( "Cannot add sub-protocols while the WebSocket is connected." );
		}

		_socket.Options.AddSubProtocol( protocol );
	}

	/// <summary>
	/// Establishes a connection to an external WebSocket service.
	/// </summary>
	/// <param name="websocketUri">The WebSocket URI to connect to. For example, "ws://hostname.local:1280/" for unencrypted WebSocket or "wss://hostname.local:1281/" for encrypted.</param>
	/// <param name="ct">A <see cref="CancellationToken"/> which allows the connection attempt to be aborted if necessary.</param>
	/// <returns>A <see cref="Task"/> which completes when the connection is established, or throws if it failed to connect.</returns>
	public Task Connect( string websocketUri, CancellationToken ct = default ) =>
		Connect( websocketUri, null, ct );

	/// <summary>
	/// Establishes a connection to an external WebSocket service.
	/// </summary>
	/// <param name="websocketUri">The WebSocket URI to connect to. For example, "ws://hostname.local:1280/" for unencrypted WebSocket or "wss://hostname.local:1281/" for encrypted.</param>
	/// <param name="headers">Headers to send with the connection request.</param>
	/// <param name="ct">A <see cref="CancellationToken"/> which allows the connection attempt to be aborted if necessary.</param>
	/// <returns>A <see cref="Task"/> which completes when the connection is established, or throws if it failed to connect.</returns>
	public async Task Connect( string websocketUri, Dictionary<string, string> headers, CancellationToken ct = default )
	{
		EnsureNotDisposed();

		if ( _socket.State != WebSocketState.None )
		{
			throw new InvalidOperationException( "Connect may only be called once per WebSocket instance." );
		}

		var uri = ParseWebSocketUri( websocketUri );

		if ( !await Http.IsAllowedAsync( uri ) )
		{
			throw new InvalidOperationException( $"Access to '{websocketUri}' is not allowed." );
		}

		if ( headers != null )
		{
			foreach ( var (key, value) in headers )
			{
				if ( !Http.IsHeaderAllowed( key ) )
				{
					throw new InvalidOperationException( $"Not allowed to set header '{key}'." );
				}

				_socket.Options.SetRequestHeader( key, value );
			}
		}

		_socket.Options.SetRequestHeader( "User-Agent", Http.UserAgent );
		_socket.Options.SetRequestHeader( "Referer", Http.Referrer );

		using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource( _cts.Token, ct );

		await _socket.ConnectAsync( uri, linkedCt.Token );

		SendLoop();
		ReceiveLoop();
	}

	/// <summary>
	/// Sends a text message to the WebSocket server.
	/// </summary>
	/// <param name="message">The message text to send. Must not be null.</param>
	/// <returns>A <see cref="ValueTask"/> which completes when the message was queued to be sent.</returns>
	public ValueTask Send( string message )
	{
		EnsureNotDisposed();

		if ( message == null )
		{
			throw new ArgumentNullException( nameof( message ) );
		}

		var byteCount = Encoding.UTF8.GetByteCount( message );
		var buffer = ArrayPool<byte>.Shared.Rent( byteCount );
		var length = Encoding.UTF8.GetBytes( message, buffer );

		return _outgoing.Writer.WriteAsync( new Message
		{
			Type = WebSocketMessageType.Text,
			Data = new ArraySegment<byte>( buffer, 0, length ),
		} );
	}

	/// <summary>
	/// Sends a binary message to the WebSocket server.
	/// </summary>
	/// <remarks>
	/// The <see cref="Send(ArraySegment{byte})"/> and <see cref="Send(Span{byte})"/> overloads allow sending subsections of byte arrays.
	/// </remarks>
	/// <param name="data">The message data to send. Must not be null.</param>
	/// <returns>A <see cref="ValueTask"/> which completes when the message was queued to be sent.</returns>
	public ValueTask Send( byte[] data )
	{
		EnsureNotDisposed();

		if ( data == null )
		{
			throw new ArgumentNullException( nameof( data ) );
		}

		return Send( data.AsSpan() );
	}

	/// <summary>
	/// Sends a binary message to the WebSocket server.
	/// </summary>
	/// <param name="data">The message data to send. Must not be null.</param>
	/// <returns>A <see cref="ValueTask"/> which completes when the message was queued to be sent.</returns>
	public ValueTask Send( ArraySegment<byte> data )
	{
		EnsureNotDisposed();

		if ( data.Array == null )
		{
			throw new ArgumentNullException( nameof( data ) );
		}

		return Send( data.AsSpan() );
	}

	/// <summary>
	/// Sends a binary message to the WebSocket server.
	/// </summary>
	/// <param name="data">The message data to send.</param>
	/// <returns>A <see cref="ValueTask"/> which completes when the message was queued to be sent.</returns>
	public ValueTask Send( Span<byte> data )
	{
		EnsureNotDisposed();

		var buffer = ArrayPool<byte>.Shared.Rent( data.Length );
		data.CopyTo( buffer );

		var message = new Message
		{
			Type = WebSocketMessageType.Binary,
			Data = new ArraySegment<byte>( buffer, 0, data.Length ),
		};

		return _outgoing.Writer.WriteAsync( message, _cts.Token );
	}

	private async void ReceiveLoop()
	{
		var ct = _cts.Token;

		while ( !ct.IsCancellationRequested )
		{
			byte[] buffer = null;

			try
			{
				buffer = ArrayPool<byte>.Shared.Rent( _maxMessageSize );

				WebSocketMessageType? type = null;
				var offset = 0;
				var length = 0;

				while ( true )
				{
					var receiveSegment = new ArraySegment<byte>( buffer, offset, buffer.Length - length );
					var result = await _socket.ReceiveAsync( receiveSegment, ct );

					if ( result.MessageType == WebSocketMessageType.Close )
					{
						Disconnect( result.CloseStatus, result.CloseStatusDescription );
						return;
					}

					if ( type == null )
					{
						type = result.MessageType;
					}
					else if ( result.MessageType != type.Value )
					{
						throw new InvalidOperationException( "WebSocket message type changed unexpectedly" );
					}

					offset += result.Count;
					length += result.Count;

					if ( result.EndOfMessage )
					{
						break;
					}

					if ( length == buffer.Length )
					{
						throw new InvalidOperationException( "WebSocket message exceeds max message size limit" );
					}
				}

				DispatchReceived( type.Value, buffer, length );
			}
			catch ( WebSocketException e ) when ( e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely )
			{
				/*
				 * Conna: only disconnect here if the connection is actually closed. We can sometimes get
				 * premature close messages but we're still connected.
				 */
				if ( !IsConnected )
				{
					Disconnect( WebSocketCloseStatus.InvalidMessageType, "Unexpected Closure" );
					return;
				}
			}
			catch ( Exception e )
			{
				if ( !ct.IsCancellationRequested )
				{
					Log.Error( e );
				}

				// Conna: let's only call disconnect if we are connected..
				if ( IsConnected )
				{
					Disconnect( WebSocketCloseStatus.InternalServerError, e.Message );
				}

				return;
			}
			finally
			{
				if ( buffer != null )
				{
					ArrayPool<byte>.Shared.Return( buffer );
				}
			}
		}
	}

	private void DispatchReceived( WebSocketMessageType type, byte[] buffer, int length )
	{
		if ( _isShuttingDown ) return;

		var data = new Span<byte>( buffer, 0, length );
		if ( type == WebSocketMessageType.Text )
		{
			var messageText = Encoding.UTF8.GetString( data );

			try
			{
				OnMessageReceived?.Invoke( messageText );
			}
			catch ( Exception e )
			{
				Log.Error( e );
			}
		}
		else
		{
			try
			{
				OnDataReceived?.Invoke( data );
			}
			catch ( Exception e )
			{
				Log.Error( e );
			}
		}
	}

	private async void SendLoop()
	{
		var ct = _cts.Token;

		while ( !ct.IsCancellationRequested )
		{
			byte[] data = null;

			try
			{
				var message = await _outgoing.Reader.ReadAsync( ct );
				data = message.Data.Array;

				await _socket.SendAsync( message.Data, message.Type, true, ct );
			}
			catch ( Exception e )
			{
				if ( !ct.IsCancellationRequested )
				{
					Log.Error( e );
				}

				Disconnect( WebSocketCloseStatus.ProtocolError, e.Message );
			}
			finally
			{
				if ( data != null )
				{
					ArrayPool<byte>.Shared.Return( data );
				}
			}
		}
	}

	private void Disconnect( WebSocketCloseStatus? status, string reason )
	{
		DispatchDisconnect( status, reason );
		Dispose();
	}

	private void DispatchDisconnect( WebSocketCloseStatus? status, string reason )
	{
		if ( _dispatchedDisconnect ) return;
		_dispatchedDisconnect = true;

		if ( _isShuttingDown ) return;

		try
		{
			OnDisconnected?.Invoke( (int)status.GetValueOrDefault( 0 ), reason );
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	private void EnsureNotDisposed()
	{
		lock ( this )
		{
			if ( _cts == null )
			{
				throw new ObjectDisposedException( nameof( WebSocket ) );
			}
		}
	}

	private static Uri ParseWebSocketUri( string websocketUri )
	{
		if ( string.IsNullOrEmpty( websocketUri ) )
		{
			throw new ArgumentNullException( nameof( websocketUri ) );
		}

		if ( !Uri.TryCreate( websocketUri, UriKind.Absolute, out var uri ) )
		{
			throw new ArgumentException( "WebSocket URI is not a valid URI.", nameof( websocketUri ) );
		}

		if ( uri.Scheme != "ws" && uri.Scheme != "wss" )
		{
			throw new ArgumentException( "WebSocket URI must use the ws:// or wss:// scheme.", nameof( websocketUri ) );
		}

		return uri;
	}
}
