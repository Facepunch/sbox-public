namespace Sandbox;

internal interface IStreamService
{
	StreamService ServiceType { get; }

	Task<bool> Connect();
	void Disconnect();

	void SetChannelLanguage( string language );
	void SetChannelDelay( int delay );

	Task<Streamer.User> GetUser( string username );

	void Tick();
}
