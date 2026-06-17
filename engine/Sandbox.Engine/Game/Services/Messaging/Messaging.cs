using Azure.Messaging.WebPubSub.Clients;

namespace Sandbox.Services;

public static class Messaging
{

	internal static Action<Message> OnMessage { get; set; }

	static WebPubSubClient client;

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

	internal static async Task Initialize( string url )
	{
		if ( string.IsNullOrWhiteSpace( url ) )
			return;

		var cred = new WebPubSubClientCredential( new Uri( url ) );

		var options = new WebPubSubClientOptions();
		options.AutoReconnect = true;
		options.AutoRejoinGroups = true;

		client = new WebPubSubClient( cred, options );
		client.GroupMessageReceived += MessageClient_GroupMessageReceived;
		client.ServerMessageReceived += MessageClient_ServerMessageReceived;
		await client.StartAsync();
	}

	internal static async Task Shutdown()
	{
		if ( client is null )
			return;

		await client.StopAsync();
		await client.DisposeAsync();
		client = null;
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
