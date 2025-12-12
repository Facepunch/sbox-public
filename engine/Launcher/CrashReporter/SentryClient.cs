namespace CrashReporter;

public class SentryClient
{
	public static async Task SubmitEnvelopeAsync( string dsn, Envelope envelope, CancellationToken cancellationToken = default )
	{
		// <scheme>://<key>@<host>:<port>/<project-id> ->
		// <scheme>://<host>:<port>/api/<project-id>/envelope
		var uri = new Uri( dsn );
		var projectId = uri.LocalPath.Trim( '/' );
		var uriBuilder = new UriBuilder()
		{
			Scheme = uri.Scheme,
			Host = uri.Host,
			Port = uri.Port,
			Path = $"/api/{projectId}/envelope/"
		};

		var stream = new MemoryStream();
		await envelope.SerializeAsync( stream, cancellationToken ).ConfigureAwait( false );
		await stream.FlushAsync( cancellationToken ).ConfigureAwait( false );
		stream.Seek( 0, SeekOrigin.Begin );

		var request = new HttpRequestMessage( HttpMethod.Post, uriBuilder.Uri )
		{
			Content = new StreamContent( stream )
		};

		using var httpClient = new HttpClient();
		using var response = await httpClient.SendAsync( request, cancellationToken ).ConfigureAwait( false );
		response.EnsureSuccessStatusCode();

		var content = await response.Content.ReadAsStringAsync( cancellationToken ).ConfigureAwait( false );

		Console.WriteLine( $"content: {content}" );

	}
}
