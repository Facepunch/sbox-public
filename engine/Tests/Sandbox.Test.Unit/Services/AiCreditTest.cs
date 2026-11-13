using Sandbox.Services;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace ServicesTests;

/// <summary>
/// Checks the account-wide credit cache populated by AI responses without contacting the backend.
/// </summary>
[TestClass, DoNotParallelize]
public class AiCreditTest
{
	Ai.CreditBalance? previous;
	static readonly DateTimeOffset Renewal = new( 2026, 12, 1, 0, 0, 0, TimeSpan.Zero );

	/// <summary>
	/// Starts with a known login balance while retaining the process's original account state.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = Ai.Credits;
		Ai.Credits = new Ai.CreditBalance { Available = 100m, RenewsAt = Renewal };
	}

	/// <summary>
	/// Restores the original account balance after each test.
	/// </summary>
	[TestCleanup]
	public void Cleanup() => Ai.Credits = previous;

	/// <summary>
	/// Queries, chats, tools and explicit refreshes replace the login balance with server accounting.
	/// </summary>
	[TestMethod]
	[DataRow( "query" )]
	[DataRow( "chat" )]
	[DataRow( "tool" )]
	[DataRow( "refresh" )]
	public async Task ResponsesRefreshTheCachedBalance( string kind )
	{
		var balance = new ServiceApi.CreditBalance { Available = 72.25m, RenewsAt = Renewal };
		object response = kind switch
		{
			"query" => new AiQueryResponse { Credits = balance },
			"chat" => new AiChatResponse { Credits = balance },
			"tool" => new AiChatToolResponse { Credits = balance },
			_ => balance
		};

		Assert.AreSame( response, await Ai.Send( () => Task.FromResult( response ) ) );
		Assert.AreEqual( balance.Available, Ai.Credits.Value.Available );
		Assert.AreEqual( balance.RenewsAt, Ai.Credits.Value.RenewsAt );

		if ( response is AiQueryResponse query )
		{
			var result = new Ai.Response( query );
			Assert.AreEqual( Ai.Credits, result.Credits );
		}
	}

	/// <summary>
	/// Missing accounting must not reset a known balance to zero or make a successful response fail.
	/// </summary>
	[TestMethod]
	public async Task MissingSnapshotsPreserveTheLastKnownBalance()
	{
		await Ai.Send( () => Task.FromResult( new AiQueryResponse { IsSuccess = true } ) );
		await Ai.Send( () => Task.FromResult( new AiChatToolResponse() ) );
		Assert.AreEqual( 100m, Ai.Credits.Value.Available );
	}

	/// <summary>
	/// Failures update credits before callers receive the exception and preserve the renewal information.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task ErrorsExposeCreditsAndRenewal( bool unavailable )
	{
		var error = new AiError
		{
			Code = "insufficient_credits",
			Error = "No credits remaining",
			RenewsAt = Renewal,
			Credits = unavailable ? null : new ServiceApi.CreditBalance { Available = -0.25m, RenewsAt = Renewal }
		};
		using var request = new HttpRequestMessage( HttpMethod.Post, "https://example.com/ai/query" );
		using var response = new HttpResponseMessage( HttpStatusCode.TooManyRequests )
		{
			Content = new StringContent( JsonSerializer.Serialize( error, JsonSerializerOptions.Web ) )
		};
		var failure = await Refit.ApiException.Create( request, HttpMethod.Post, response, new Refit.RefitSettings() );

		var exception = await Assert.ThrowsExceptionAsync<Ai.RequestException>( () => Ai.Send( () => Task.FromException<AiQueryResponse>( failure ) ) );

		Assert.AreEqual( 429, exception.StatusCode );
		Assert.AreEqual( error.Code, exception.Code );
		Assert.AreEqual( error.Credits?.Available, exception.Credits?.Available );
		Assert.AreEqual( error.Credits?.RenewsAt, exception.Credits?.RenewsAt );
		Assert.AreEqual( Renewal, exception.RenewsAt );
		Assert.AreEqual( unavailable ? 100m : -0.25m, Ai.Credits.Value.Available );
	}
}
