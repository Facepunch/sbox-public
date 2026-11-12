using Azure.Messaging.WebPubSub.Clients;

namespace Sandbox.Services;

public static class Messaging
{

	internal static Action<Message> OnMessage { get; set; }

	static WebPubSubClient client;
	static readonly System.Threading.SemaphoreSlim clientLock = new( 1, 1 );

	/// <summary>
	/// Store the messages from the other thread so we can process them 
	/// on the main thread, at an appropriate time in the loop
	/// </summary>
	static System.Threading.Channels.Channel<Message> incoming = System.Threading.Channels.Channel.CreateUnbounded<Message>();

	internal struct Message
	{
		public string User { get; set; }
		public string Group { get; set; }
		public object Data { get; set; }
	}

	/// <summary>
	/// Queued with messages so consumers can reconcile snapshots on the main thread
	/// after the initial connection or a reconnect.
	/// </summary>
	internal sealed record ConnectionEstablished;

	internal static async Task Initialize( string url )
	{
		if ( string.IsNullOrWhiteSpace( url ) )
			return;

		var cred = new WebPubSubClientCredential( new Uri( url ) );

		var options = new WebPubSubClientOptions();
		options.AutoReconnect = true;
		options.AutoRejoinGroups = true;

		await Initialize( new WebPubSubClient( cred, options ) );
	}

	/// <summary>
	/// Replace the messaging connection without leaving the previous login subscribed.
	/// </summary>
	internal static async Task Initialize( WebPubSubClient replacement )
	{
		await clientLock.WaitAsync();
		try
		{
			await StopClient();

			replacement.Connected += args =>
			{
				incoming.Writer.TryWrite( new Message { Data = new ConnectionEstablished() } );
				return Task.CompletedTask;
			};
			replacement.GroupMessageReceived += MessageClient_GroupMessageReceived;
			replacement.ServerMessageReceived += MessageClient_ServerMessageReceived;
			await replacement.StartAsync();
			client = replacement;
		}
		catch
		{
			await replacement.DisposeAsync();
			throw;
		}
		finally
		{
			clientLock.Release();
		}
	}

	internal static async Task Shutdown()
	{
		await clientLock.WaitAsync();
		try
		{
			await StopClient();
		}
		finally
		{
			clientLock.Release();
		}
	}

	private static async Task StopClient()
	{
		var previous = client;
		client = null;
		if ( previous is null )
			return;

		try
		{
			await previous.StopAsync();
		}
		finally
		{
			await previous.DisposeAsync();
		}
	}

	internal static void ProcessMessages()
	{
		while ( incoming.Reader.TryRead( out var msg ) )
		{
			try
			{
				OnMessage?.Invoke( msg );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
		}
	}

	private static Task MessageClient_ServerMessageReceived( WebPubSubServerMessageEventArgs args )
	{
		//Log.Info( $"Server Message: {args.Message.Data.ToString()}" );
		return Task.CompletedTask;
	}

	private static Task MessageClient_GroupMessageReceived( WebPubSubGroupMessageEventArgs args )
	{
		if ( args.Message.DataType != WebPubSubDataType.Binary )
			return Task.CompletedTask;

		// Ignore p2p messages right now. Only listen to messages from system
		if ( !string.IsNullOrEmpty( args.Message.FromUserId ) )
			return Task.CompletedTask;

		var msg = args.Message;
		using var s = args.Message.Data.ToStream();
		var packet = Sandbox.Protobuf.ProtobufHelper.FromWire( s );
		if ( packet is null ) return Task.CompletedTask;

		var message = new Message
		{
			User = args.Message.FromUserId,
			Group = args.Message.Group,
			Data = packet
		};

		incoming.Writer.TryWrite( message );
		return Task.CompletedTask;
	}
}
