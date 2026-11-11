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
	public sealed class QuestStatusTool : Ai.BaseTool<QuestResult>
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
	public sealed class CountTool : Ai.BaseTool<int>
	{
		public int Count { get; set; }
		public Size Size { get; set; }
		public override Task<int> Run( CancellationToken cancellationToken ) => Task.FromResult( Count );
	}

	sealed class ImageToolState
	{
		internal Ai.Query Result { get; set; }
		internal int Calls { get; set; }
	}

	/// <summary>
	/// Returns a bitmap directly from the game-owned context.
	/// </summary>
	public sealed class BitmapTool : Ai.BaseTool<Bitmap>
	{
		/// <inheritdoc />
		public override Task<Bitmap> Run( CancellationToken cancellationToken ) => Task.FromResult( (Bitmap)Context );
	}

	/// <summary>
	/// Returns a texture directly from the game-owned context.
	/// </summary>
	public sealed class TextureTool : Ai.BaseTool<Texture>
	{
		/// <inheritdoc />
		public override Task<Texture> Run( CancellationToken cancellationToken ) => Task.FromResult( (Texture)Context );
	}

	/// <summary>
	/// Direct bitmap results become image attachments and survive disposal before a delivery retry.
	/// </summary>
	[TestMethod]
	public async Task BitmapToolResultsAreEncodedAndRetainedForRetry()
	{
		using var bitmap = new Bitmap( 32, 16 );
		bitmap.Clear( Color.Red );
		var api = new FakeApi
		{
			OnChat = ( request, _ ) => ToolResults( request ).Count == 0
				? Task.FromResult( ToolReply( new AiToolCall { Id = "bitmap", Name = nameof( BitmapTool ), Arguments = "{}" } ) )
				: Task.FromException<AiChatResponse>( new InvalidOperationException( "Submission failed" ) )
		};
		var chat = new Ai.Config { Context = bitmap }.AddTool<BitmapTool>().CreateChat( api );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => chat.Send( "Look." ) );

		var result = ToolResults( api.ChatRequests[1] ).Single();
		Assert.AreEqual( "bitmap", result.ToolCallId );
		Assert.IsNull( result.Content );
		var image = result.Images.Single();
		Assert.IsTrue( image.StartsWith( "data:image/jpeg;base64," ) );
		using var decoded = Bitmap.CreateFromBytes( Convert.FromBase64String( image[(image.IndexOf( ',' ) + 1)..] ) );
		Assert.AreEqual( 32, decoded.Width );
		Assert.AreEqual( 16, decoded.Height );
		Assert.IsTrue( bitmap.IsValid );
		bitmap.Dispose();

		api.OnChat = ( _, _ ) => Task.FromResult( FinalReply() );
		await chat.Retry();
		Assert.AreEqual( image, ToolResults( api.ChatRequests[2] ).Single().Images.Single() );
		Assert.AreEqual( 0, api.Queries.Count );
		StringAssert.Contains( api.ChatRequests.First().Options.Tools.Single().Description, "Returns text and/or images." );
	}

	/// <summary>
	/// Image types bypass JSON schema generation, and missing images become recoverable tool errors.
	/// </summary>
	[TestMethod]
	public async Task NullBitmapAndTextureResultsAreReportedAsErrors()
	{
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply(
			new AiToolCall { Id = "bitmap", Name = nameof( BitmapTool ), Arguments = "{}" },
			new AiToolCall { Id = "texture", Name = nameof( TextureTool ), Arguments = "{}" } ) );
		api.Replies.Enqueue( FinalReply() );
		var chat = new Ai.Config().AddTool<BitmapTool>().AddTool<TextureTool>().CreateChat( api );
		await chat.Send( "Look." );

		Assert.AreEqual( 2, ToolResults( api.ChatRequests[1] ).Count );
		foreach ( var result in ToolResults( api.ChatRequests[1] ) )
		{
			StringAssert.Contains( result.Content, "The tool returned no result." );
			Assert.IsNull( result.Images );
		}

		foreach ( var tool in api.ChatRequests.First().Options.Tools )
		{
			StringAssert.Contains( tool.Description, "Returns text and/or images." );
		}
	}

	/// <summary>
	/// Returns a prepared image payload without making another AI request.
	/// </summary>
	public sealed class ImageTool : Ai.BaseTool<Ai.Query>
	{
		/// <inheritdoc />
		public override Task<Ai.Query> Run( CancellationToken cancellationToken )
		{
			var state = (ImageToolState)Context;
			state.Calls++;
			return Task.FromResult( state.Result );
		}
	}

	/// <summary>
	/// Rich callbacks produce image attachments, not JSON strings containing image data.
	/// </summary>
	[TestMethod]
	[DataRow( "The player's view." )]
	[DataRow( null )]
	public async Task ToolImagesReachTheMatchingToolResult( string text )
	{
		var state = new ImageToolState { Result = new Ai.Query { Input = text }.AddImage( "https://example.com/view.png" ) };
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( new AiToolCall { Id = "screenshot", Name = nameof( ImageTool ), Arguments = "{}" } ) );
		api.Replies.Enqueue( FinalReply() );
		var chat = new Ai.Config { Context = state }.AddTool<ImageTool>().CreateChat( api );
		await chat.Send( "Check the view." );

		var result = ToolResults( api.ChatRequests[1] ).Single();
		Assert.AreEqual( "screenshot", result.ToolCallId );
		Assert.AreEqual( text, result.Content );
		Assert.AreEqual( "https://example.com/view.png", result.Images.Single() );
		Assert.AreEqual( 0, api.Queries.Count );
		StringAssert.Contains( api.ChatRequests.First().Options.Tools.Single().Description, "Returns text and/or images." );
	}

	/// <summary>
	/// Empty rich results become ordinary tool errors so the model can recover.
	/// </summary>
	[TestMethod]
	public async Task EmptyImageToolResultIsReportedAsAnError()
	{
		var state = new ImageToolState { Result = new Ai.Query() };
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( new AiToolCall { Id = "empty", Name = nameof( ImageTool ), Arguments = "{}" } ) );
		api.Replies.Enqueue( FinalReply() );
		await new Ai.Config { Context = state }.AddTool<ImageTool>().CreateChat( api ).Send( "Check." );
		StringAssert.Contains( ToolResults( api.ChatRequests[1] ).Single().Content, "Supply tool text or an image." );
	}

	/// <summary>
	/// Retrying delivery retains the original image and does not recapture a changed scene.
	/// </summary>
	[TestMethod]
	public async Task ImageToolResultsAreRetainedAcrossSubmissionFailure()
	{
		var state = new ImageToolState { Result = new Ai.Query { Input = "Original view" }.AddImage( "https://example.com/original.png" ) };
		var api = new FakeApi
		{
			OnChat = ( request, _ ) => ToolResults( request ).Count == 0
				? Task.FromResult( ToolReply( new AiToolCall { Id = "capture", Name = nameof( ImageTool ), Arguments = "{}" } ) )
				: Task.FromException<AiChatResponse>( new InvalidOperationException( "Submission failed" ) )
		};
		var chat = new Ai.Config { Context = state }.AddTool<ImageTool>().CreateChat( api );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => chat.Send( "Look." ) );
		state.Result.Input = "Changed view";
		state.Result.AddImage( "https://example.com/later.png" );
		api.OnChat = ( _, _ ) => Task.FromResult( FinalReply() );
		await chat.Retry();

		Assert.AreEqual( 1, state.Calls );
		CollectionAssert.AreEqual( api.ChatRequests[1].Messages.Select( x => x.GetRawText() ).ToArray(), api.ChatRequests[2].Messages.Select( x => x.GetRawText() ).ToArray() );
		var result = ToolResults( api.ChatRequests[2] ).Single();
		Assert.AreEqual( "Original view", result.Content );
		Assert.AreEqual( "https://example.com/original.png", result.Images.Single() );
	}

#nullable enable annotations
	public sealed class NullableTool : Ai.BaseTool<string>
	{
		public string? Value { get; set; }
		public override Task<string> Run( CancellationToken cancellationToken ) => Task.FromResult( Value ?? "null" );
	}
#nullable restore annotations

	static Ai.Config Config( ToolState state = null ) => new Ai.Config
	{
		Instructions = "Check quest progress before answering.",
		Context = state ?? new ToolState()
	}.AddTool<QuestStatusTool>();

	static AiToolCall Call( string id = "quest-1", string arguments = "{\"questId\":\"sailor\"}" )
		=> new() { Id = id, Name = nameof( QuestStatusTool ), Arguments = arguments };
	static AiChatResponse ToolReply( params AiToolCall[] calls ) => new()
	{
		IsSuccess = true,
		ToolCalls = calls.ToList(),
		Message = JsonSerializer.SerializeToElement( new
		{
			role = "assistant",
			tool_calls = calls.Select( call => new { id = call.Id, type = "function", function = new { name = call.Name, arguments = call.Arguments } } ),
			reasoning_details = new[] { new { type = "reasoning.encrypted", data = "preserve-me" } }
		} )
	};
	static AiChatResponse FinalReply( string text = "Done." ) => new()
	{
		IsSuccess = true,
		Text = text,
		Message = JsonSerializer.SerializeToElement( new { role = "assistant", content = text } ),
		FinishReason = "Stop",
		CreditsSpent = 1m,
		TotalCreditsSpent = 5m
	};

	[TestMethod]
	public async Task ToolsRunAutomaticallyWithFreshInstancesAndTypedResults()
	{
		var state = new ToolState();
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( Call() ) );
		api.Replies.Enqueue( ToolReply( Call( "quest-2", "{\"questId\":\"harbour\"}" ) ) );
		api.Replies.Enqueue( FinalReply() );
		var chat = Config( state ).CreateChat( api );
		var response = await chat.Send( "Check my quests." );

		CollectionAssert.AreEqual( new[] { "sailor", "harbour" }, state.Calls );
		Assert.AreNotSame( state.Instances[0], state.Instances[1] );
		Assert.AreSame( state, state.Instances[0].Context );
		Assert.AreEqual( "Done.", response.Text );
		Assert.AreSame( response, chat.LastResponse );
		Assert.AreEqual( 5m, chat.TotalCreditsSpent );
		CollectionAssert.AreEqual( new[] { "user", "assistant" }, chat.Messages.Select( x => x.Role ).ToArray() );
		Assert.IsTrue( JsonDocument.Parse( ToolResults( api.ChatRequests[1] )[0].Content ).RootElement.GetProperty( "isComplete" ).GetBoolean() );
		Assert.AreEqual( 3, api.ChatRequests.Select( x => x.Messages.Count ).Distinct().Count() );
	}

	[TestMethod]
	public async Task ToolDescriptionsIncludeInputsAndResultsButExcludeContextAndHelpers()
	{
		var api = new FakeApi();
		api.Replies.Enqueue( FinalReply() );
		await Config().CreateChat( api ).Send( "Hello" );
		var definition = api.ChatRequests.First().Options.Tools.Single();
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

	/// <summary>
	/// A tool whose constructor must not run while discovering its input and result schemas.
	/// </summary>
	public sealed class ConstructorProbeTool : Ai.BaseTool<int>
	{
		/// <summary>
		/// Detects attempts to construct a tool during registration.
		/// </summary>
		public ConstructorProbeTool() => throw new InvalidOperationException( "Tool was constructed." );

		/// <summary>
		/// Supplies an integer result type for schema discovery.
		/// </summary>
		public override Task<int> Run( CancellationToken cancellationToken ) => Task.FromResult( 1 );
	}

	/// <summary>
	/// Registering a tool discovers its schemas without running its constructor.
	/// </summary>
	[TestMethod]
	public async Task ToolRegistrationDoesNotRunConstructors()
	{
		var api = new FakeApi();
		api.Replies.Enqueue( FinalReply() );

		await new Ai.Config().AddTool<ConstructorProbeTool>().CreateChat( api ).Send( "Hello" );

		var definition = api.ChatRequests.Single().Options.Tools.Single();
		Assert.AreEqual( nameof( ConstructorProbeTool ), definition.Name );
		StringAssert.Contains( definition.Description, "\"type\":\"integer\"" );
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
		await Config( state ).CreateChat( api ).Send( "Check." );
		Assert.AreEqual( 0, state.Calls.Count );
		StringAssert.Contains( ToolResults( api.ChatRequests[1] )[0].Content, "tool_error" );
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
		var config = new Ai.Config().AddTool<NullableTool>().AddTool<CountTool>();
		await config.CreateChat( api ).Send( "Check." );
		var results = ToolResults( api.ChatRequests[1] );
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
		await Config( state ).CreateChat( api ).Send( "Check." );
		StringAssert.Contains( ToolResults( api.ChatRequests[1] )[0].Content, "Quest unavailable." );
		StringAssert.Contains( ToolResults( api.ChatRequests[1] )[1].Content, "unknown_tool" );
	}

	[TestMethod]
	public async Task ConfigIsCapturedWhenCreatingAQueryOrChat()
	{
		var state = new ToolState();
		var config = Config( state );
		var query = config.CreateQuery( "Check." );
		var api = new FakeApi();
		var chat = config.CreateChat( api );
		config.Instructions = "Changed";
		config.Context = new ToolState();
		config.AddTool<CountTool>();
		api.Replies.Enqueue( ToolReply( Call() ) );
		api.Replies.Enqueue( FinalReply() );
		await chat.Send( "Check." );
		api.Replies.Enqueue( ToolReply( Call() ) );
		api.Replies.Enqueue( FinalReply() );
		await query.Run( api, default );
		Assert.AreEqual( 2, state.Calls.Count );
		Assert.IsTrue( api.ChatRequests.Select( x => x.Options ).All( x => x.Instructions == "Check quest progress before answering." && x.Tools.Count == 1 ) );
	}

	/// <summary>
	/// A failure before submission must not retain the output schema for a later attempt.
	/// </summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public async Task QueryPreparationFailureDoesNotRetainTheOutputSchema( bool cancelled )
	{
		var api = new FakeApi();
		var query = Config().CreateQuery( cancelled ? "Check." : null );
		using var cancellation = new CancellationTokenSource();

		if ( cancelled )
		{
			cancellation.Cancel();
			await Assert.ThrowsExceptionAsync<OperationCanceledException>( () => query.Run<int>( api, cancellation.Token ) );
		}
		else
		{
			await Assert.ThrowsExceptionAsync<ArgumentException>( () => query.Run<int>( api, default ) );
		}

		Assert.AreEqual( 0, api.ChatRequests.Count );

		query.Input = "Check.";
		api.Replies.Enqueue( FinalReply( "{\"isComplete\":true,\"remainingObjectives\":0}" ) );
		var response = await query.Run<QuestResult>( api, default );

		Assert.IsTrue( response.HasData );
		StringAssert.Contains( api.ChatRequests.Single().Options.Instructions, "remainingObjectives" );
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
			OnChat = ( request, _ ) => ToolResults( request ).Count == 0
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
		Assert.AreEqual( 1, api.ChatRequests.Select( x => x.Id ).Distinct().Count() );
		CollectionAssert.AreEqual( api.ChatRequests[1].Messages.Select( x => x.GetRawText() ).ToArray(), api.ChatRequests[2].Messages.Select( x => x.GetRawText() ).ToArray() );
		Assert.AreEqual( ToolResults( api.ChatRequests[1] )[0].Content, ToolResults( api.ChatRequests[2] )[0].Content );
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
		var chat = config.CreateChat( api );
		await Assert.ThrowsExceptionAsync<TaskCanceledException>( () => chat.Send( "Check.", cancellationToken: cancellation.Token ) );
		state.OnRun = null;
		await chat.Retry();
		Assert.AreEqual( 2, state.Calls.Count );
		StringAssert.Contains( ToolResults( api.ChatRequests[1] )[0].Content, "tool_cancelled" );
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
		var chat = config.CreateChat( api );
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
		Assert.AreEqual( 1, api.ChatRequests.Select( x => x.Id ).Distinct().Count() );
		Assert.AreEqual( 1, state.Calls.Count );
		Assert.AreEqual( 2, api.ChatRequests.Count );
	}

	/// <summary>
	/// Reset clears local history and accounting without making a backend request.
	/// </summary>
	[TestMethod]
	public async Task ResetClearsLocalConversation()
	{
		var api = new FakeApi();
		var chat = Config().CreateChat( api );
		api.Replies.Enqueue( FinalReply() );
		await chat.Send( "Hello." );
		var oldId = chat.Id;
		chat.Reset();
		Assert.AreNotEqual( oldId, chat.Id );
		Assert.AreEqual( 0, chat.Messages.Count );
		Assert.IsNull( chat.LastResponse );
		Assert.AreEqual( 0m, chat.TotalCreditsSpent );
		Assert.AreEqual( 1, api.ChatRequests.Count );
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
		var chat = new Ai.Config().CreateChat( api );
		var query = new Ai.Config().CreateQuery( "Original" ).AddImage( "https://example.com/first.png" );
		await Assert.ThrowsExceptionAsync<Ai.RequestException>( () => chat.Send( query ) );
		query.Input = "Changed";
		query.AddImage( "https://example.com/second.png" );
		Assert.AreEqual( 0, chat.Messages.Count );
		fail = false;
		await chat.Retry();
		CollectionAssert.AreEqual( api.ChatRequests[0].Messages.Select( x => x.GetRawText() ).ToArray(), api.ChatRequests[1].Messages.Select( x => x.GetRawText() ).ToArray() );
		Assert.AreEqual( "Original", chat.Messages[0].Text );
		Assert.AreEqual( 1, chat.Messages[0].Images.Count );
	}

	[TestMethod]
	public async Task ConcurrentOperationsAreRejectedAndRefusalsDoNotRunTools()
	{
		var done = new TaskCompletionSource<AiChatResponse>( TaskCreationOptions.RunContinuationsAsynchronously );
		var state = new ToolState();
		var api = new FakeApi { OnChat = ( _, _ ) => done.Task };
		var chat = Config( state ).CreateChat( api );
		var send = chat.Send( "First" );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => chat.Send( "Second" ) );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => chat.Retry() );
		Assert.ThrowsException<InvalidOperationException>( () => chat.Reset() );
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
		StringAssert.Contains( api.ChatRequests.First().Options.Instructions, "Return the final answer as JSON" );
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
		final.CreditsSpent = 42m;
		api.Replies.Enqueue( final );
		var response = await Config( state ).CreateQuery( "Check." ).Run<QuestResult>( api, default );
		Assert.IsFalse( response.HasData );
		Assert.IsNull( response.Data );
		Assert.AreEqual( text, response.Text );
		Assert.AreEqual( 42, response.CreditsSpent );
		Assert.AreEqual( 42m, response.CreditsSpent );
		Assert.AreEqual( 5m, response.TotalCreditsSpent );
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
		var chat = new Ai.Config().CreateChat( api );
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
		Assert.AreEqual( 0, typeof( Ai.Query ).GetConstructors().Length );
		foreach ( var name in new[] { "CostUsd", "TotalCostUsd", "InputTokens", "OutputTokens", "CachedInputTokens", "CacheWriteTokens" } )
			Assert.IsNull( typeof( Ai.Response ).GetProperty( name ) );
	}

	/// <summary>
	/// Server tools use their own endpoint and retries retain local results and original provider reasoning.
	/// </summary>
	[TestMethod]
	public async Task ServerToolsRetryWithoutRepeatingCompletedGameTools()
	{
		var state = new ToolState();
		var api = new FakeApi();
		api.Replies.Enqueue( ToolReply( Call(), new AiToolCall { Id = "web", Name = "sb_web_search", Arguments = "{}" } ) );
		api.Replies.Enqueue( FinalReply() );
		api.OnTool = ( _, _ ) => Task.FromException<AiChatToolResponse>( new HttpRequestException() );
		var chat = Config( state ).CreateChat( api );
		Assert.AreEqual( 0, api.ChatRequests.Count );
		await Assert.ThrowsExceptionAsync<Ai.RequestException>( () => chat.Send( "Check the web." ) );
		Assert.AreEqual( 1, state.Calls.Count );
		api.OnTool = ( request, _ ) => Task.FromResult( new AiChatToolResponse
		{
			Result = new AiToolResult { ToolCallId = request.Call.Id, Content = "Web result" },
			TotalCreditsSpent = 3m
		} );

		await chat.Retry();

		Assert.AreEqual( 1, state.Calls.Count );
		Assert.AreEqual( 2, api.ToolRequests.Count );
		Assert.AreEqual( chat.Id, api.ToolRequests[0].Id );
		Assert.AreEqual( "Check quest progress before answering.", api.ToolRequests[0].Options.Instructions );
		var history = api.ChatRequests[1];
		Assert.AreEqual( 2, ToolResults( history ).Count );
		Assert.AreEqual( "Web result", ToolResults( history )[1].Content );
		Assert.AreEqual( "preserve-me", history.Messages[1].GetProperty( "reasoning_details" )[0].GetProperty( "data" ).GetString() );
		Assert.AreEqual( 3m, history.TotalCreditsSpent );
	}

	/// <summary>
	/// Incomplete assistant tool calls do not run or leave unanswered calls in the next request's history.
	/// </summary>
	[TestMethod]
	public async Task IncompleteToolCallsDoNotPoisonTheNextTurn()
	{
		var state = new ToolState();
		var api = new FakeApi();
		var incomplete = ToolReply( Call() );
		incomplete.IsSuccess = false;
		incomplete.Text = "Partial answer";
		api.Replies.Enqueue( incomplete );
		api.Replies.Enqueue( FinalReply() );
		var chat = Config( state ).CreateChat( api );
		await chat.Send( "First" );
		await chat.Send( "Continue" );

		Assert.AreEqual( 0, state.Calls.Count );
		var history = api.ChatRequests[1].Messages;
		Assert.AreEqual( 3, history.Count );
		Assert.AreEqual( "Partial answer", history[1].GetProperty( "content" ).GetString() );
		Assert.IsFalse( history[1].TryGetProperty( "tool_calls", out _ ) );
	}

	/// <summary>
	/// Repeated screenshots share a history budget with user images without losing tool results or retry state.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task ImageHistoryRetainsNewestImagesAcrossRoundsRetriesAndTurns( bool serverTool )
	{
		var image = "data:image/png;base64," + Convert.ToBase64String( new byte[3 * 1024 * 1024] );
		var state = new ImageToolState { Result = new Ai.Config().CreateQuery( "Screenshot text" ).AddImage( image ) };
		var api = new FakeApi();
		api.OnChat = ( request, _ ) =>
		{
			var count = ToolResults( request ).Count;
			return count < 4
				? Task.FromResult( ToolReply( new AiToolCall
				{
					Id = $"screenshot-{count}",
					Name = serverTool ? "sb_screenshot" : nameof( ImageTool ),
					Arguments = "{}"
				} ) )
				: Task.FromException<AiChatResponse>( new HttpRequestException() );
		};
		api.OnTool = ( request, _ ) => Task.FromResult( new AiChatToolResponse
		{
			Result = new AiToolResult { ToolCallId = request.Call.Id, Content = "Screenshot text", Images = [image] }
		} );
		var chat = new Ai.Config { Context = state }.AddTool<ImageTool>().CreateChat( api );

		await Assert.ThrowsExceptionAsync<Ai.RequestException>( () => chat.Send( "Inspect this.", [image] ) );

		var failed = api.ChatRequests.Last();
		Assert.AreEqual( 9, failed.Messages.Count );
		Assert.AreEqual( "Inspect this.", failed.Messages[0].GetProperty( "content" )[0].GetProperty( "text" ).GetString() );
		StringAssert.Contains( failed.Messages[0].GetProperty( "content" )[1].GetProperty( "text" ).GetString(), "image omitted" );
		var results = ToolResults( failed );
		Assert.AreEqual( 0, results[0].Images.Count );
		for ( var i = 0; i < results.Count; i++ )
		{
			Assert.AreEqual( $"screenshot-{i}", results[i].ToolCallId );
			Assert.AreEqual( "Screenshot text", results[i].Content );
			if ( i > 0 )
				Assert.AreEqual( image, results[i].Images.Single() );

			Assert.AreEqual( "preserve-me", failed.Messages[1 + i * 2].GetProperty( "reasoning_details" )[0].GetProperty( "data" ).GetString() );
		}

		api.OnChat = ( _, _ ) => Task.FromResult( FinalReply() );
		await chat.Retry();
		Assert.AreEqual( JsonSerializer.Serialize( failed.Messages ), JsonSerializer.Serialize( api.ChatRequests.Last().Messages ) );
		Assert.AreEqual( 4, serverTool ? api.ToolRequests.Count : state.Calls );

		await chat.Send( "Another image.", [image] );
		var next = api.ChatRequests.Last();
		Assert.AreEqual( 0, ToolResults( next )[1].Images.Count );
		Assert.AreEqual( image, next.Messages.Last().GetProperty( "content" )[1].GetProperty( "image_url" ).GetProperty( "url" ).GetString() );
		Assert.AreEqual( 4, chat.Messages.Count );

		foreach ( var request in api.ChatRequests )
		{
			var imageBytes = request.Messages
				.Where( message => message.TryGetProperty( "content", out var content ) && content.ValueKind == JsonValueKind.Array )
				.SelectMany( message => message.GetProperty( "content" ).EnumerateArray() )
				.Where( part => part.GetProperty( "type" ).GetString() == "image_url" )
				.Sum( part => System.Text.Encoding.UTF8.GetByteCount( part.GetRawText() ) );
			Assert.IsTrue( imageBytes <= Ai.Query.MaxImagePayloadBytes );
		}
	}

	static List<AiToolResult> ToolResults( AiChat chat ) => chat.Messages
		.Where( message => message.GetProperty( "role" ).GetString() == "tool" )
		.Select( message =>
		{
			var content = message.GetProperty( "content" );
			return new AiToolResult
			{
				ToolCallId = message.GetProperty( "tool_call_id" ).GetString(),
				Content = content.ValueKind == JsonValueKind.String ? content.GetString()
					: content.EnumerateArray().Where( part => part.GetProperty( "type" ).GetString() == "text" ).Select( part => part.GetProperty( "text" ).GetString() ).FirstOrDefault(),
				Images = content.ValueKind == JsonValueKind.Array
					? content.EnumerateArray().Where( part => part.GetProperty( "type" ).GetString() == "image_url" ).Select( part => part.GetProperty( "image_url" ).GetProperty( "url" ).GetString() ).ToList() : null
			};
		} ).ToList();

	sealed class FakeApi : ServiceApi.IAiApi
	{
		public List<AiChat> ChatRequests { get; } = [];
		public List<AiQueryRequest> Queries { get; } = [];
		public Queue<AiChatResponse> Replies { get; } = [];
		public Func<AiChat, CancellationToken, Task<AiChatResponse>> OnChat { get; set; }
		public Func<AiChatToolInput, CancellationToken, Task<AiChatToolResponse>> OnTool { get; set; }
		public List<AiChatToolInput> ToolRequests { get; } = [];
		public Task<ServiceApi.CreditBalance> GetCredits( CancellationToken cancellationToken = default ) => throw new NotSupportedException();
		public Task<AiChatToolResponse> ExecuteChatTool( AiChatToolInput input, CancellationToken cancellationToken = default )
		{
			ToolRequests.Add( input );
			return OnTool( input, cancellationToken );
		}
		public async Task<AiChatResponse> QueryChat( AiChat input, CancellationToken cancellationToken = default )
		{
			input = JsonSerializer.Deserialize<AiChat>( JsonSerializer.Serialize( input ) );
			ChatRequests.Add( input );
			var reply = OnChat is null ? Replies.Dequeue() : await OnChat( input, cancellationToken );
			reply.RequestId = Guid.NewGuid();
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
		var api = new FakeApi { Reply = new() { IsSuccess = true, FinishReason = "Stop", Text = "{\"name\":\"Table\"}", CreditsSpent = 42m } };
		var query = new Ai.Config { Instructions = "Describe furniture." }.CreateQuery( "Describe this." )
			.AddImage( "https://example.com/table.png" );

		var response = await query.Run<Description>( api, cancellation.Token );

		Assert.IsTrue( response.HasData );
		Assert.AreEqual( "Table", response.Data.Name );
		Assert.AreEqual( 42, response.CreditsSpent );
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
		var response = await new Ai.Config().CreateQuery( "Describe a table." ).Run( api, default );
		Assert.AreEqual( "A table.", response.Text );
		Assert.IsNull( api.Requests.Single().JsonSchema );
	}

	[TestMethod]
	[DataRow( 4096, 1024, 2048, 512 )]
	[DataRow( 1024, 4096, 512, 2048 )]
	[DataRow( 32, 16, 32, 16 )]
	public async Task BitmapIsCopiedResizedAndRemainsUsableAfterDisposal( int width, int height, int expectedWidth, int expectedHeight )
	{
		var query = new Ai.Config().CreateQuery();
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
		Assert.ThrowsException<ArgumentException>( () => new Ai.Config().CreateQuery().AddImage( url ) );
	}

	[TestMethod]
	public async Task ImageBudgetsAreCheckedBeforeTheBackendIsCalled()
	{
		const int imageBytes = 3 * 1024 * 1024;
		var oversized = "data:image/png;base64," + Convert.ToBase64String( new byte[imageBytes + 1] );
		Assert.ThrowsException<ArgumentException>( () => new Ai.Config().CreateQuery().AddImage( oversized ) );
		var image = "data:image/png;base64," + Convert.ToBase64String( new byte[imageBytes] );
		var query = new Ai.Config().CreateQuery().AddImage( image ).AddImage( image ).AddImage( image ).AddImage( image );
		var api = new FakeApi();
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => query.Run( api, default ) );
		Assert.AreEqual( 0, api.Requests.Count );
	}

	[TestMethod]
	public async Task EmptyAndCancelledQueriesDoNotReachTheBackend()
	{
		var api = new FakeApi();
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => new Ai.Config().CreateQuery().Run( api, default ) );
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsExceptionAsync<OperationCanceledException>( () => new Ai.Config().CreateQuery( "Hello" ).Run<Description>( api, cancellation.Token ) );
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

		public Task<ServiceApi.CreditBalance> GetCredits( CancellationToken cancellationToken = default ) => throw new NotSupportedException();
		public Task<AiChatToolResponse> ExecuteChatTool( AiChatToolInput input, CancellationToken cancellationToken = default ) => throw new NotSupportedException();
		public Task<AiChatResponse> QueryChat( AiChat input, CancellationToken cancellationToken = default ) => throw new NotSupportedException();
	}
}
