using Sandbox.Engine;
using Sandbox.Network;
using Sandbox.Tasks;
using System;

namespace NetworkTests;

/// <summary>
/// File requests can only read assets advertised by the receiving host.
/// </summary>
[TestClass, DoNotParallelize]
public class LargeNetworkFilesTests
{
	GlobalContext previous;
	MemoryFileSystem files;
	LargeNetworkFiles downloads;

	/// <summary>
	/// Mounts public and private files without publishing the private source.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		files = new MemoryFileSystem();
		files.CreateDirectory( "assets" );
		files.WriteAllText( "assets/public.bin", "public data" );
		files.WriteAllText( "private.server.csx", "private source" );
		files.WriteAllText( "other.bin", "unadvertised data" );
		GlobalContext.Current = new GlobalContext
		{
			TypeLibrary = SharedTypeLibrary.ScriptingWithSystem,
			FileMount = files
		};
		downloads = new LargeNetworkFiles( "LargeFiles" );
		Assert.IsTrue( downloads.AddFile( files, "assets/public.bin" ) );
	}

	/// <summary>
	/// Restores the original filesystem and context after each request scenario.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.Shutdown();
		GlobalContext.Current = previous;
		files.Dispose();
	}

	/// <summary>
	/// Valid aliases resolve to the advertised path before reading the mounted file.
	/// </summary>
	[TestMethod]
	[DataRow( "assets/public.bin" )]
	[DataRow( "/ASSETS\\PUBLIC.BIN" )]
	public void AdvertisedFilesCanBeDownloaded( string path )
	{
		CollectionAssert.AreEqual( files.ReadAllBytes( "assets/public.bin" ).ToArray(), Request( path ) );
	}

	/// <summary>
	/// Guessing a mounted file or using an invalid path cannot bypass the advertised table.
	/// </summary>
	[TestMethod]
	[DataRow( "private.server.csx" )]
	[DataRow( "other.bin" )]
	[DataRow( "assets/../private.server.csx" )]
	[DataRow( "assets/public.bin::$DATA" )]
	[DataRow( "assets/public.bin " )]
	[DataRow( null )]
	public void UnadvertisedAndInvalidFilesAreDenied( string path )
	{
		Assert.AreEqual( 0, Request( path ).Length );
	}

	/// <summary>
	/// Removing an advertisement immediately revokes access to a file that remains mounted.
	/// </summary>
	[TestMethod]
	public void RemovedFilesCannotBeDownloaded()
	{
		downloads.RemoveFile( "assets/public.bin" );
		Assert.AreEqual( 0, Request( "assets/public.bin" ).Length );
		Assert.IsTrue( files.FileExists( "assets/public.bin" ) );
	}

	/// <summary>
	/// A client cannot be used as a file server even when it has received the advertisement.
	/// </summary>
	[TestMethod]
	public void ClientsCannotServeAdvertisedFiles()
	{
		Assert.AreEqual( 0, Request( "assets/public.bin", isHost: false ).Length );
	}

	byte[] Request( string path, bool isHost = true )
	{
		return AsyncPump.RunSynchronously( async () =>
		{
			var library = GlobalContext.Current.TypeLibrary;
			var system = new NetworkSystem( "file-download-test", library );
			// Select the receiver's role without starting a real lobby or a game instance.
			typeof( NetworkSystem ).GetProperty( nameof( NetworkSystem.IsHost ) ).SetValue( system, isHost );
			using var game = new TestGameNetworkSystem { NetworkSystem = system };
			downloads.NetworkInitialize( game );
			var connection = new CaptureConnection { System = system, RemoteIsHost = !isHost };
			var requestId = Guid.NewGuid();
			var request = ByteStream.Create( 256 );
			try
			{
				request.Write( InternalMessageType.Request );
				request.Write( requestId );
				request.Write( InternalMessageType.Packed );
				system.Serialize( new LargeNetworkFiles.RequestFile( path ), ref request );
				using var message = ByteStream.CreateReader( request.ToArray() );
				system.HandleIncomingMessage( new NetworkSystem.NetworkMessage { Source = connection, Data = message } );
			}
			finally
			{
				request.Dispose();
			}

			var response = await connection.Response.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
			var reader = ByteStream.CreateReader( response );
			try
			{
				Assert.AreEqual( InternalMessageType.Response, reader.Read<InternalMessageType>() );
				Assert.AreEqual( requestId, reader.Read<Guid>() );
				Assert.AreEqual( InternalMessageType.Packed, reader.Read<InternalMessageType>() );
				return library.FromBytes<byte[]>( ref reader );
			}
			finally
			{
				reader.Dispose();
			}
		} );
	}

	private sealed class TestGameNetworkSystem : GameNetworkSystem
	{
	}

	private sealed class CaptureConnection : Connection
	{
		internal readonly TaskCompletionSource<byte[]> Response = new();
		internal bool RemoteIsHost;

		/// <summary>
		/// Gives the receiving peer an authenticated sender role for message dispatch.
		/// </summary>
		public override bool IsHost => RemoteIsHost;

		internal override void InternalSend( NetPacket packet, NetFlags flags ) => Response.SetResult( Decode( packet.Span ).ToArray() );
		internal override void InternalRecv( NetworkSystem.MessageHandler handler ) { }
		internal override void InternalClose( int closeCode, string closeReason ) { }
	}
}
