using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sentry;
using Sentry.Protocol.Envelopes;
using System.Net.Http;
using System.Text.Json;

namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class ManagedExceptionTransportTest
{
	sealed class Submission
	{
		public List<JsonElement> Requests = new();
		public bool Fails;
		public bool LoggedIn = true;
		public Task Send( object batch )
		{
			Requests.Add( JsonSerializer.SerializeToElement( batch ) );
			return Fails ? Task.FromException( new HttpRequestException( "Test failure" ) ) : Task.CompletedTask;
		}
		public ManagedExceptionTransport Transport() => new( Send, () => LoggedIn );
	}

	static Envelope Report()
	{
		var report = new SentryEvent { Release = "test-build", Level = SentryLevel.Error };
		report.SetTag( "activity_session_id", "session" );
		return new Envelope( new Dictionary<string, object>(), [EnvelopeItem.FromEvent( report )] );
	}

	[TestMethod]
	public async Task UsesExistingEventApiAndPreservesTheFullReport()
	{
		var submission = new Submission();
		var transport = submission.Transport();
		using var envelope = Report();
		await transport.SendEnvelopeAsync( envelope );
		var record = submission.Requests.Single().GetProperty( "Events" )[0];
		Assert.AreEqual( "managed.exception", record.GetProperty( "Name" ).GetString() );
		var report = record.GetProperty( "Data" ).GetProperty( "report" );
		Assert.AreEqual( "test-build", report.GetProperty( "release" ).GetString() );
		Assert.AreEqual( "session", report.GetProperty( "tags" ).GetProperty( "activity_session_id" ).GetString() );
	}

	[TestMethod]
	public async Task FailuresRemainRetryableAndKeepTheSameEventId()
	{
		var submission = new Submission { Fails = true };
		var transport = submission.Transport();
		using var envelope = Report();
		await Assert.ThrowsExceptionAsync<HttpRequestException>( () => transport.SendEnvelopeAsync( envelope ) );
		submission.Fails = false;
		await transport.SendEnvelopeAsync( envelope );
		Assert.AreEqual( submission.Requests[0].GetRawText(), submission.Requests[1].GetRawText() );
	}

	[TestMethod]
	public async Task OptingOutAlsoPreventsCachedReportsFromBeingSent()
	{
		var previous = AccountInformation.UseAnalytics;
		try
		{
			AccountInformation.UseAnalytics = false;
			var submission = new Submission();
			using var envelope = Report();
			await submission.Transport().SendEnvelopeAsync( envelope );
			Assert.AreEqual( 0, submission.Requests.Count );
		}
		finally { AccountInformation.UseAnalytics = previous; }
	}

	[TestMethod]
	public void RollingLimitsBoundRepeatedAndDistinctErrorsAndRecoverAfterOneMinute()
	{
		long now = 0;
		var limiter = new ExceptionReportLimiter( 20, 3, () => now );
		for ( var i = 0; i < 3; i++ ) Assert.IsTrue( limiter.TryAccept( "same", out _ ) );
		for ( var i = 0; i < 1000; i++ ) Assert.IsFalse( limiter.TryAccept( "same", out _ ) );
		Assert.IsTrue( limiter.TryAccept( "different", out var suppressed ) );
		Assert.AreEqual( 1000L, suppressed );
		for ( var i = 0; i < 16; i++ ) Assert.IsTrue( limiter.TryAccept( $"other-{i}", out _ ) );
		Assert.IsFalse( limiter.TryAccept( "new", out _ ) );
		now = 59_999;
		Assert.IsFalse( limiter.TryAccept( "new", out _ ) );
		now = 60_000;
		Assert.IsTrue( limiter.TryAccept( "same", out suppressed ) );
		Assert.AreEqual( 2L, suppressed );
	}

	[TestMethod]
	public void WarningExceptionsAreExcludedAndChangingMessagesCannotBypassSampling()
	{
		var limiter = new ExceptionReportLimiter( 20, 3 );
		Assert.IsFalse( ManagedExceptionReporting.Accept( new SentryEvent( new Exception() ) { Level = SentryLevel.Warning }, limiter ) );
		for ( var i = 0; i < 3; i++ )
			Assert.IsTrue( ManagedExceptionReporting.Accept( new SentryEvent( new Exception( $"message-{i}" ) ), limiter ) );
		Assert.IsFalse( ManagedExceptionReporting.Accept( new SentryEvent( new Exception( "another message" ) ), limiter ) );
	}

	[TestMethod]
	public async Task CachedUploadAttemptsAreAlsoBounded()
	{
		var submission = new Submission();
		var transport = submission.Transport();
		using var envelope = Report();
		for ( var i = 0; i < 100; i++ ) await transport.SendEnvelopeAsync( envelope );
		Assert.AreEqual( 20, submission.Requests.Count );
	}

	[TestMethod]
	public async Task WarningsInCachedEnvelopesAreNotUploaded()
	{
		var submission = new Submission();
		using var envelope = Envelope.FromEvent( new SentryEvent { Level = SentryLevel.Warning } );
		await submission.Transport().SendEnvelopeAsync( envelope );
		Assert.AreEqual( 0, submission.Requests.Count );
	}

	[TestMethod]
	public async Task CachedReportsWaitForLoginInsteadOfUsingAnAnonymousEndpoint()
	{
		var submission = new Submission { LoggedIn = false };
		var transport = submission.Transport();
		using var envelope = Report();
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => transport.SendEnvelopeAsync( envelope ) );
		Assert.AreEqual( 0, submission.Requests.Count );
		submission.LoggedIn = true;
		await transport.SendEnvelopeAsync( envelope );
		Assert.AreEqual( 1, submission.Requests.Count );
	}
}
