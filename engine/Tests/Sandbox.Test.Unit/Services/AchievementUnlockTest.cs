using Refit;
using Sandbox.Services;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace ServicesTests;

/// <summary>
/// Checks when a backend response becomes a local achievement unlock.
/// </summary>
[TestClass]
public class AchievementUnlockTest
{
	private sealed class Transport : HttpMessageHandler
	{
		internal int Requests;
		internal TaskCompletionSource<string> Reply = new();

		protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken token )
		{
			Requests++;
			var json = await Reply.Task;
			return new HttpResponseMessage( HttpStatusCode.OK ) { Content = new StringContent( json ) };
		}
	}

	/// <summary>
	/// Concurrent calls share one write. An unconfirmed award stays eligible for a later call.
	/// </summary>
	[DataTestMethod]
	[DataRow( """{"Valid":true,"Result":{"Actioned":false}}""" )]
	[DataRow( """{"Valid":false,"Result":{"Actioned":true}}""" )]
	[DataRow( null )]
	public async Task UnlockRequiresConfirmation( string firstReply )
	{
		using var transport = new Transport();
		using var client = new HttpClient( transport ) { BaseAddress = new Uri( "https://fixture.invalid" ) };
		var api = RestService.For<ServiceApi.IAchievementApi>( client );
		var entry = new Achievement( new AchievementDto { Name = "winner" } );

		var first = entry.Unlock( "test.game", api );
		var duplicate = entry.Unlock( "test.game", api );
		Assert.AreEqual( 1, transport.Requests );
		Assert.IsFalse( entry.IsUnlocked );

		if ( firstReply is null )
		{
			transport.Reply.SetException( new HttpRequestException( "Connection lost" ) );
		}
		else
		{
			transport.Reply.SetResult( firstReply );
		}

		await Task.WhenAll( first, duplicate );
		Assert.IsFalse( entry.IsUnlocked );

		transport.Reply = new();
		transport.Reply.SetResult( """{"Valid":true,"Result":{"Actioned":true}}""" );
		await entry.Unlock( "test.game", api );
		await entry.Unlock( "test.game", api );

		Assert.IsTrue( entry.IsUnlocked );
		Assert.AreEqual( 2, transport.Requests );
	}
}
