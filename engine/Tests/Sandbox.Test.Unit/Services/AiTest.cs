using Sandbox.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace ServicesTests;

[TestClass]
public class AiChatTest
{
	[global::Description( "The player's quest progress." )]
	public sealed class QuestResult
	{
		[global::Description( "Whether the quest is complete." )]
		public bool IsComplete { get; set; }
		public int RemainingObjectives { get; set; }
	}

	sealed class ToolState
	{
		public List<string> Calls { get; } = [];
		public List<QuestStatusTool> Instances { get; } = [];
		public Func<QuestStatusTool, CancellationToken, Task<QuestResult>> OnRun { get; set; }
	}

	[global::Description( "Read the player's progress on a quest." )]
	public sealed class QuestStatusTool : AiTool<QuestResult>
	{
		[global::Description( "The quest's internal ID, such as missing_sailor." )]
		public string QuestId { get; set; }
		[JsonIgnore] public string DebugInfo { get; set; }
		public bool IsValid => !string.IsNullOrEmpty( QuestId );

		public override async Task<QuestResult> Run( CancellationToken cancellationToken )
		{
			var state = (ToolState)Context;
			state.Calls.Add( QuestId );
			state.Instances.Add( this );
			return state.OnRun is null
				? new QuestResult { IsComplete = true, RemainingObjectives = 0 }
				: await state.OnRun( this, cancellationToken );
		}
	}

	public enum Size { Small, Large }
	public sealed class CountTool : AiTool<int>
	{
		public int Count { get; set; }
		public Size Size { get; set; }
		public override Task<int> Run( CancellationToken cancellationToken ) => Task.FromResult( Count );
	}

#nullable enable annotations
	public sealed class NullableTool : AiTool<string>
	{
		public string? Value { get; set; }
		public override Task<string> Run( CancellationToken cancellationToken ) => Task.FromResult( Value ?? "null" );
	}
#nullable restore annotations

	static AiConfig Config( ToolState state = null ) => new AiConfig
	{
		Instructions = "Check quest progress before answering.",
		Context = state ?? new ToolState()
	}.AddTool<QuestStatusTool>();

	static AiToolCall Call( string id = "quest-1", string arguments = "{\"questId\":\"sailor\"}" )
		=> new() { Id = id, Name = nameof( QuestStatusTool ), Arguments = arguments };
	static AiChatResponse ToolReply( params AiToolCall[] calls ) => new() { IsSuccess = true, ToolCalls = calls.ToList() };
	static AiChatResponse FinalReply( string text = "Done." ) => new()
	{
		IsSuccess = true,
		Text = text,
		FinishReason = "Stop",
		CostUsd = 0.01m,
		TotalCostUsd = 0.05m
	};

	[TestMethod]
	public async Task ToolsRunAutomaticallyWithFreshInstancesAndTypedResults()
	{
		var state = new ToolState();
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( Call() ) );
		api.Replies.Enqueue( ToolReply( Call( "quest-2", "{\"questId\":\"harbour\"}" ) ) );
		api.Replies.Enqueue( FinalReply() );
		var chat = await Config( state ).CreateChat( api, default );
		var response = await chat.Send( "Check my quests." );

		CollectionAssert.AreEqual( new[] { "sailor", "harbour" }, state.Calls );
		Assert.AreNotSame( state.Instances[0], state.Instances[1] );
		Assert.AreSame( state, state.Instances[0].Context );
		Assert.AreEqual( "Done.", response.Text );
		Assert.AreSame( response, chat.LastResponse );
		Assert.AreEqual( 0.05m, chat.TotalCostUsd );
		CollectionAssert.AreEqual( new[] { "user", "assistant" }, chat.Messages.Select( x => x.Role ).ToArray() );
		Assert.IsTrue( JsonDocument.Parse( api.ChatRequests[1].ToolResults[0].Content ).RootElement.GetProperty( "isComplete" ).GetBoolean() );
		Assert.AreEqual( 3, api.ChatRequests.Select( x => x.RequestId ).Distinct().Count() );
	}

	[TestMethod]
	public async Task ToolDescriptionsIncludeInputsAndResultsButExcludeContextAndHelpers()
	{
		var api = new FakeApi();
		await Config().CreateChat( api, default );
		var definition = api.Creations.Single().Tools.Single();
		Assert.AreEqual( nameof( QuestStatusTool ), definition.Name );
		StringAssert.Contains( definition.Description, "Read the player's progress" );
		StringAssert.Contains( definition.Description, "Whether the quest is complete" );
		StringAssert.Contains( definition.Description, "remainingObjectives" );
		var properties = definition.Parameters.GetProperty( "properties" );
		Assert.AreEqual( "The quest's internal ID, such as missing_sailor.", properties.GetProperty( "questId" ).GetProperty( "description" ).GetString() );
		Assert.AreEqual( 1, properties.EnumerateObject().Count() );
		Assert.AreEqual( "questId", definition.Parameters.GetProperty( "required" )[0].GetString() );
		Assert.ThrowsException<ArgumentException>( () => Config().AddTool<QuestStatusTool>() );
	}

	[TestMethod]
	[DataRow( "not JSON" )]
	[DataRow( "null" )]
	[DataRow( "{}" )]
	[DataRow( "{\"questId\":null}" )]
	[DataRow( "{\"questId\":42}" )]
	[DataRow( "{\"questId\":\"sailor\",\"extra\":1}" )]
	public async Task InvalidArgumentsBecomeToolErrorsWithoutRunningGameCode( string arguments )
	{
		var state = new ToolState();
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( Call( arguments: arguments ) ) );
		api.Replies.Enqueue( FinalReply() );
		await (await Config( state ).CreateChat( api, default )).Send( "Check." );
		Assert.AreEqual( 0, state.Calls.Count );
		StringAssert.Contains( api.ChatRequests[1].ToolResults[0].Content, "tool_error" );
	}

	[TestMethod]
	public async Task NullableInputsAndStringEnumsDeserializeNormally()
	{
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply(
			new() { Id = "nullable", Name = nameof( NullableTool ), Arguments = "{\"value\":null}" },
			new() { Id = "count", Name = nameof( CountTool ), Arguments = "{\"count\":3,\"size\":\"Large\"}" },
			new() { Id = "bad", Name = nameof( CountTool ), Arguments = "{\"count\":3,\"size\":1}" } ) );
		api.Replies.Enqueue( FinalReply() );
		var config = new AiConfig().AddTool<NullableTool>().AddTool<CountTool>();
		await (await config.CreateChat( api, default )).Send( "Check." );
		var results = api.ChatRequests[1].ToolResults;
		Assert.AreEqual( "\"null\"", results[0].Content );
		Assert.AreEqual( "3", results[1].Content );
		StringAssert.Contains( results[2].Content, "tool_error" );
	}

	[TestMethod]
	public async Task CallbackErrorsAndUnknownToolsAreReportedToTheModel()
	{
		var state = new ToolState { OnRun = ( _, _ ) => throw new InvalidOperationException( "Quest unavailable." ) };
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( Call(), new() { Id = "unknown", Name = "unknown", Arguments = "{}" } ) );
		api.Replies.Enqueue( FinalReply() );
		await (await Config( state ).CreateChat( api, default )).Send( "Check." );
		StringAssert.Contains( api.ChatRequests[1].ToolResults[0].Content, "Quest unavailable." );
		StringAssert.Contains( api.ChatRequests[1].ToolResults[1].Content, "unknown_tool" );
	}

	[TestMethod]
	public async Task ConfigIsCapturedWhenCreatingAQueryOrChat()
	{
		var state = new ToolState();
		var config = Config( state );
		var query = config.CreateQuery( "Check." );
		var creation = new TaskCompletionSource<AiChat>( TaskCreationOptions.RunContinuationsAsynchronously );
		var api = new FakeApi { OnCreate = ( _, _ ) => creation.Task };
		var creating = config.CreateChat( api, default );
		config.Instructions = "Changed";
		config.Context = new ToolState();
		config.AddTool<CountTool>();
		creation.SetResult( new AiChat { Id = Guid.NewGuid() } );
		var chat = await creating;
		api.Replies.Enqueue( ToolReply( Call() ) );
		api.Replies.Enqueue( FinalReply() );
		await chat.Send( "Check." );
		api.OnCreate = null;
		api.Replies.Enqueue( ToolReply( Call() ) );
		api.Replies.Enqueue( FinalReply() );
		await query.Run( api, default );
		Assert.AreEqual( 2, state.Calls.Count );
		Assert.IsTrue( api.Creations.All( x => x.Instructions == "Check quest progress before answering." && x.Tools.Count == 1 ) );
	}

	[TestMethod]
	public async Task QueryToolsRetainAsyncResultsAcrossSubmissionFailure()
	{
		var done = new TaskCompletionSource<QuestResult>( TaskCreationOptions.RunContinuationsAsynchronously );
		using var cancellation = new CancellationTokenSource();
		var state = new ToolState
		{
			OnRun = ( _, token ) =>
		{
			Assert.AreEqual( cancellation.Token, token );
			return done.Task;
		}
		};
		var fail = true;
		var api = new FakeApi
		{
			OnChat = ( request, _ ) => request.ToolResults.Count == 0
			? Task.FromResult( ToolReply( Call() ) )
			: fail ? Task.FromException<AiChatResponse>( new HttpRequestException() ) : Task.FromResult( FinalReply() )
		};
		var query = Config( state ).CreateQuery( "Check." );
		var running = query.Run( api, cancellation.Token );
		Assert.IsFalse( running.IsCompleted );
		Assert.AreEqual( 1, api.ChatRequests.Count );
		done.SetResult( new QuestResult { IsComplete = true } );
		await Assert.ThrowsExceptionAsync<Ai.RequestException>( () => running );
		fail = false;
		query.Input = "Changed input must not replace the pending turn.";
		var result = await query.Run( api, default );
		Assert.AreEqual( "Done.", result.Text );
		Assert.AreEqual( 1, state.Calls.Count );
		Assert.AreEqual( 1, api.Creations.Count );
		Assert.AreEqual( api.ChatRequests[1].RequestId, api.ChatRequests[2].RequestId );
		Assert.AreEqual( api.ChatRequests[1].ToolResults[0].Content, api.ChatRequests[2].ToolResults[0].Content );
	}

	[TestMethod]
	public async Task CancelledToolIsNotRepeatedAndBatchResumesWithinRoundLimit()
	{
		using var cancellation = new CancellationTokenSource();
		var state = new ToolState
		{
			OnRun = ( _, token ) =>
		{
			cancellation.Cancel();
			return Task.FromCanceled<QuestResult>( token );
		}
		};
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( Call(), Call( "second", "{\"questId\":\"harbour\"}" ) ) );
		api.Replies.Enqueue( FinalReply() );
		var config = Config( state );
		config.MaxToolRounds = 1;
		var chat = await config.CreateChat( api, default );
		await Assert.ThrowsExceptionAsync<TaskCanceledException>( () => chat.Send( "Check.", cancellationToken: cancellation.Token ) );
		state.OnRun = null;
		await chat.Retry();
		Assert.AreEqual( 2, state.Calls.Count );
		StringAssert.Contains( api.ChatRequests[1].ToolResults[0].Content, "tool_cancelled" );
	}

	[TestMethod]
	public async Task RoundLimitStopsBeforeExecutingTheNextBatch()
	{
		var state = new ToolState();
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( Call() ) );
		api.Replies.Enqueue( ToolReply( Call( "second" ) ) );
		api.Replies.Enqueue( FinalReply() );
		var config = Config( state );
		config.MaxToolRounds = 1;
		var chat = await config.CreateChat( api, default );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => chat.Send( "Check." ) );
		Assert.AreEqual( 1, state.Calls.Count );
		chat.MaxToolRounds = 2;
		await chat.Retry();
		Assert.AreEqual( 2, state.Calls.Count );
	}

	[TestMethod]
	public async Task QueryRoundLimitDoesNotRestartOrRepeatCompletedTools()
	{
		var state = new ToolState();
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( Call() ) );
		api.Replies.Enqueue( ToolReply( Call( "second" ) ) );
		var config = Config( state );
		config.MaxToolRounds = 1;
		var query = config.CreateQuery( "Check." );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => query.Run( api, default ) );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => query.Run( api, default ) );
		Assert.AreEqual( 1, api.Creations.Count );
		Assert.AreEqual( 1, state.Calls.Count );
		Assert.AreEqual( 2, api.ChatRequests.Count );
	}

	[TestMethod]
	public async Task ResetPreservesStateOnFailureAndClearsItOnSuccess()
	{
		var api = new FakeApi();
		var chat = await Config().CreateChat( api, default );
		api.Replies.Enqueue( FinalReply() );
		await chat.Send( "Hello." );
		var oldId = chat.Id;
		api.OnCreate = ( _, _ ) => Task.FromException<AiChat>( new HttpRequestException() );
		await Assert.ThrowsExceptionAsync<Ai.RequestException>( () => chat.Reset() );
		Assert.AreEqual( oldId, chat.Id );
		Assert.AreEqual( 2, chat.Messages.Count );
		api.OnCreate = null;
		await chat.Reset();
		Assert.AreNotEqual( oldId, chat.Id );
		Assert.AreEqual( 0, chat.Messages.Count );
		Assert.IsNull( chat.LastResponse );
	}

	[TestMethod]
	public async Task FailedInitialSubmissionRetriesWithItsOriginalMessageAndImages()
	{
		var fail = true;
		var api = new FakeApi
		{
			OnChat = ( _, _ ) => fail
			? Task.FromException<AiChatResponse>( new HttpRequestException() ) : Task.FromResult( FinalReply() )
		};
		var chat = await new AiConfig().CreateChat( api, default );
		var query = new AiConfig().CreateQuery( "Original" ).AddImage( "https://example.com/first.png" );
		await Assert.ThrowsExceptionAsync<Ai.RequestException>( () => chat.Send( query ) );
		query.Input = "Changed";
		query.AddImage( "https://example.com/second.png" );
		Assert.AreEqual( 0, chat.Messages.Count );
		fail = false;
		await chat.Retry();
		Assert.AreEqual( api.ChatRequests[0].RequestId, api.ChatRequests[1].RequestId );
		Assert.AreEqual( "Original", chat.Messages[0].Text );
		Assert.AreEqual( 1, chat.Messages[0].Images.Count );
	}

	[TestMethod]
	public async Task ConcurrentOperationsAreRejectedAndRefusalsDoNotRunTools()
	{
		var done = new TaskCompletionSource<AiChatResponse>( TaskCreationOptions.RunContinuationsAsynchronously );
		var state = new ToolState();
		var api = new FakeApi { OnChat = ( _, _ ) => done.Task };
		var chat = await Config( state ).CreateChat( api, default );
		var send = chat.Send( "First" );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => chat.Send( "Second" ) );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => chat.Retry() );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => chat.Reset() );
		var refused = ToolReply( Call() );
		refused.Refusal = "Unable to help.";
		done.SetResult( refused );
		await send;
		Assert.AreEqual( 0, state.Calls.Count );
	}

	[TestMethod]
	public async Task StructuredToolQueryDescribesItsAnswerSchemaAndReadsTypedData()
	{
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( Call() ) );
		api.Replies.Enqueue( FinalReply( "{\"isComplete\":true,\"remainingObjectives\":0}" ) );
		var response = await Config().CreateQuery( "Check." ).Run<QuestResult>( api, default );
		Assert.IsTrue( response.HasData );
		Assert.IsTrue( response.Data.IsComplete );
		StringAssert.Contains( api.Creations.Single().Instructions, "Return the final answer as JSON" );
		Assert.AreEqual( 0, api.Queries.Count );
	}

	[TestMethod]
	[DataRow( "```json\n{\"isComplete\":true}\n```" )]
	[DataRow( "{\"isComplete\":\"yes\"}" )]
	[DataRow( "null" )]
	[DataRow( null )]
	public async Task InvalidStructuredAnswersPreserveTheResponseAndAccounting( string text )
	{
		var state = new ToolState();
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( Call() ) );
		var final = FinalReply( text );
		final.InputTokens = 42;
		api.Replies.Enqueue( final );
		var response = await Config( state ).CreateQuery( "Check." ).Run<QuestResult>( api, default );
		Assert.IsFalse( response.HasData );
		Assert.IsNull( response.Data );
		Assert.AreEqual( text, response.Text );
		Assert.AreEqual( 42, response.InputTokens );
		Assert.AreEqual( 0.01m, response.CostUsd );
		Assert.AreEqual( 0.05m, response.TotalCostUsd );
		Assert.AreEqual( 1, state.Calls.Count );
	}

	[TestMethod]
	[DataRow( "Stop" )]
	[DataRow( "Length" )]
	public void UnsuccessfulStructuredResponsesDoNotDeserialize( string reason )
	{
		var response = new Ai.Response<QuestResult>( new AiQueryResponse { IsSuccess = false, FinishReason = reason, Text = "not JSON" } );
		response.ReadData();
		Assert.IsFalse( response.HasData );
	}

	[TestMethod]
	[DataRow( "{\"code\":\"chat_busy\",\"error\":\"Busy\"}", "chat_busy" )]
	[DataRow( "not JSON", "http_error" )]
	public async Task BackendErrorsExposeCodesWithoutChangingHistory( string body, string expectedCode )
	{
		using var request = new HttpRequestMessage( HttpMethod.Post, "https://example.com/ai/chat" );
		using var response = new HttpResponseMessage( HttpStatusCode.Conflict ) { Content = new StringContent( body ) };
		var failure = await Refit.ApiException.Create( request, HttpMethod.Post, response, new Refit.RefitSettings() );
		var api = new FakeApi { OnChat = ( _, _ ) => Task.FromException<AiChatResponse>( failure ) };
		var chat = await new AiConfig().CreateChat( api, default );
		var error = await Assert.ThrowsExceptionAsync<Ai.RequestException>( () => chat.Send( "Hello" ) );
		Assert.AreEqual( 409, error.StatusCode );
		Assert.AreEqual( expectedCode, error.Code );
		Assert.AreEqual( 0, chat.Messages.Count );
	}

	[TestMethod]
	public void GameApiDoesNotExposeManualToolsOrOldFactories()
	{
		Assert.IsNull( typeof( Ai ).GetNestedType( "Tool" ) );
		Assert.IsFalse( typeof( Ai ).GetMethods().Any( x => x.Name == "CreateChat" ) );
		Assert.IsFalse( typeof( Ai.Chat ).GetMethods().Any( x => x.Name == "Query" ) );
		Assert.IsNull( typeof( Ai.Response ).GetProperty( "ToolCalls" ) );
		Assert.AreEqual( 0, typeof( AiQuery ).GetConstructors().Length );
	}

	sealed class FakeApi : ServiceApi.IAiApi
	{
		public List<AiChatInput> ChatRequests { get; } = [];
		public List<AiChatCreate> Creations { get; } = [];
		public List<AiQueryRequest> Queries { get; } = [];
		public Queue<AiChatResponse> Replies { get; } = [];
		public Func<AiChatInput, CancellationToken, Task<AiChatResponse>> OnChat { get; set; }
		public Func<AiChatCreate, CancellationToken, Task<AiChat>> OnCreate { get; set; }
		public Task<AiChat> CreateChat( AiChatCreate input, CancellationToken cancellationToken = default )
		{
			Creations.Add( input );
			return OnCreate?.Invoke( input, cancellationToken ) ?? Task.FromResult( new AiChat { Id = Guid.NewGuid(), TotalCostUsd = 0m } );
		}
		public async Task<AiChatResponse> QueryChat( AiChatInput input, CancellationToken cancellationToken = default )
		{
			ChatRequests.Add( input );
			var reply = OnChat is null ? Replies.Dequeue() : await OnChat( input, cancellationToken );
			reply.RequestId = input.RequestId;
			return reply;
		}
		public Task<AiQueryResponse> Query( AiQueryRequest input, CancellationToken cancellationToken = default )
		{
			Queries.Add( input );
			return Task.FromResult( new AiQueryResponse { IsSuccess = true, FinishReason = "Stop", Text = "Done." } );
		}
	}
}

[TestClass]
public class AiQueryTest
{
	record Description( string Name );

	[TestMethod]
	public async Task StructuredQuerySendsTheBuiltRequestAndReadsItsResult()
	{
		using var cancellation = new CancellationTokenSource();
		var api = new FakeApi { Reply = new() { IsSuccess = true, FinishReason = "Stop", Text = "{\"name\":\"Table\"}", InputTokens = 42 } };
		var query = new AiConfig { Instructions = "Describe furniture." }.CreateQuery( "Describe this." )
			.AddImage( "https://example.com/table.png" );

		var response = await query.Run<Description>( api, cancellation.Token );

		Assert.IsTrue( response.HasData );
		Assert.AreEqual( "Table", response.Data.Name );
		Assert.AreEqual( 42, response.InputTokens );
		Assert.AreEqual( cancellation.Token, api.Token );
		var request = api.Requests.Single();
		Assert.AreEqual( query.Input, request.Input );
		Assert.AreEqual( "Describe furniture.", request.Instructions );
		Assert.AreEqual( "https://example.com/table.png", request.Images.Single() );
		Assert.IsTrue( request.JsonSchema.Value.GetProperty( "properties" ).TryGetProperty( "name", out _ ) );
		Assert.IsFalse( request.JsonSchema.Value.GetProperty( "additionalProperties" ).GetBoolean() );
	}

	[TestMethod]
	public async Task PlainQueryDoesNotRequireASchema()
	{
		var api = new FakeApi { Reply = new() { IsSuccess = true, Text = "A table." } };
		var response = await new AiConfig().CreateQuery( "Describe a table." ).Run( api, default );
		Assert.AreEqual( "A table.", response.Text );
		Assert.IsNull( api.Requests.Single().JsonSchema );
	}

	[TestMethod]
	[DataRow( 4096, 1024, 2048, 512 )]
	[DataRow( 1024, 4096, 512, 2048 )]
	[DataRow( 32, 16, 32, 16 )]
	public async Task BitmapIsCopiedResizedAndRemainsUsableAfterDisposal( int width, int height, int expectedWidth, int expectedHeight )
	{
		var query = new AiConfig().CreateQuery();
		using ( var bitmap = new Bitmap( width, height ) )
		{
			bitmap.Clear( Color.Red );
			Assert.AreSame( query, query.AddImage( bitmap, lossless: true ) );
			bitmap.Clear( Color.Green );
		}

		var images = await query.PrepareImages( default );
		var url = images.Single();
		Assert.IsTrue( url.StartsWith( "data:image/png;base64," ) );
		using var decoded = Bitmap.CreateFromBytes( Convert.FromBase64String( url[(url.IndexOf( ',' ) + 1)..] ) );
		Assert.AreEqual( expectedWidth, decoded.Width );
		Assert.AreEqual( expectedHeight, decoded.Height );
		Assert.AreEqual( Color.Red, decoded.GetPixel( 0, 0 ) );
		images.Clear();
		Assert.AreEqual( url, (await query.PrepareImages( default )).Single() );
	}

	[TestMethod]
	[DataRow( "relative.png" )]
	[DataRow( "file:///private/image.png" )]
	[DataRow( "data:image/svg+xml;base64,PHN2Zz4=" )]
	[DataRow( "data:image/png;base64,broken!" )]
	public void InvalidImageSourcesAreRejected( string url )
	{
		Assert.ThrowsException<ArgumentException>( () => new AiConfig().CreateQuery().AddImage( url ) );
	}

	[TestMethod]
	public async Task ImageBudgetsAreCheckedBeforeTheBackendIsCalled()
	{
		const int imageBytes = 3 * 1024 * 1024;
		var oversized = "data:image/png;base64," + Convert.ToBase64String( new byte[imageBytes + 1] );
		Assert.ThrowsException<ArgumentException>( () => new AiConfig().CreateQuery().AddImage( oversized ) );
		var image = "data:image/png;base64," + Convert.ToBase64String( new byte[imageBytes] );
		var query = new AiConfig().CreateQuery().AddImage( image ).AddImage( image ).AddImage( image ).AddImage( image );
		var api = new FakeApi();
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => query.Run( api, default ) );
		Assert.AreEqual( 0, api.Requests.Count );
	}

	[TestMethod]
	public async Task EmptyAndCancelledQueriesDoNotReachTheBackend()
	{
		var api = new FakeApi();
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => new AiConfig().CreateQuery().Run( api, default ) );
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsExceptionAsync<OperationCanceledException>( () => new AiConfig().CreateQuery( "Hello" ).Run<Description>( api, cancellation.Token ) );
		Assert.AreEqual( 0, api.Requests.Count );
	}

	sealed class FakeApi : ServiceApi.IAiApi
	{
		public List<AiQueryRequest> Requests { get; } = [];
		public AiQueryResponse Reply { get; init; }
		public CancellationToken Token { get; private set; }

		public Task<AiQueryResponse> Query( AiQueryRequest input, CancellationToken cancellationToken = default )
		{
			Requests.Add( input );
			Token = cancellationToken;
			return Task.FromResult( Reply );
		}

		public Task<AiChat> CreateChat( AiChatCreate options, CancellationToken cancellationToken = default ) => throw new NotSupportedException();
		public Task<AiChatResponse> QueryChat( AiChatInput input, CancellationToken cancellationToken = default ) => throw new NotSupportedException();
	}
}
