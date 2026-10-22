using Sandbox.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Text.Json;

namespace ServicesTests;

/// <summary>
/// Checks local AI chat history and accounting without contacting a model.
/// </summary>
[TestClass]
public class AiChatTest
{
	record Plan( string Name, int Balls, Layout Layout, PlanSize? Size );
	record Layout( int Width, int Height );
	enum PlanSize { Small, Large }
	const string PlanJson = """{"name":"Pool","balls":16,"layout":{"width":100,"height":50},"size":"Large"}""";

	/// <summary>
	/// Typed queries generate strict nested schemas and deserialize their final answers without losing metadata.
	/// </summary>
	[TestMethod]
	public async Task StructuredChatOutput()
	{
		var api = new FakeApi();
		api.Replies.Enqueue( new AiChatResponse { IsSuccess = true, FinishReason = "Stop", Text = PlanJson, CostUsd = 0.01m, InputTokens = 42 } );
		var chat = new Ai.Chat( Guid.NewGuid(), api );
		var response = await chat.Query<Plan>( "Plan a table" );
		Assert.IsTrue( response.HasData );
		Assert.AreEqual( "Pool", response.Data.Name );
		Assert.AreEqual( 16, response.Data.Balls );
		Assert.AreEqual( 100, response.Data.Layout.Width );
		Assert.AreEqual( PlanSize.Large, response.Data.Size );
		Assert.AreEqual( 42, response.InputTokens );
		Assert.AreEqual( 0.01m, chat.TotalCostUsd );
		Assert.AreSame( response, chat.LastResponse );
		Assert.AreSame( response, chat.Messages[1].Response );
		var schema = api.Requests[0].JsonSchema.Value;
		Assert.IsFalse( schema.GetProperty( "additionalProperties" ).GetBoolean() );
		Assert.AreEqual( 4, schema.GetProperty( "required" ).GetArrayLength() );
		var layout = schema.GetProperty( "properties" ).GetProperty( "layout" );
		Assert.IsFalse( layout.GetProperty( "additionalProperties" ).GetBoolean() );
		Assert.AreEqual( 2, layout.GetProperty( "required" ).GetArrayLength() );
	}

	/// <summary>
	/// Intermediate tool replies do not deserialize prose, and typed tool continuations supply the schema again.
	/// </summary>
	[TestMethod]
	public async Task StructuredChatTools()
	{
		var api = new FakeApi();
		api.Replies.Enqueue( new AiChatResponse
		{
			IsSuccess = true,
			FinishReason = "ToolCalls",
			Text = "Inspecting the scene.",
			CostUsd = 0.01m,
			ToolCalls = [new() { Id = "tool-1", Name = "FindObjects", Arguments = "{}" }]
		} );
		api.Replies.Enqueue( new AiChatResponse { IsSuccess = true, FinishReason = "Stop", Text = PlanJson, CostUsd = 0.02m } );
		var chat = new Ai.Chat( Guid.NewGuid(), api );
		var first = await chat.Query<Plan>( "Create a table" );
		Assert.IsFalse( first.HasData );
		Assert.IsNull( first.Data );
		Assert.AreEqual( "FindObjects", first.ToolCalls[0].Name );
		var final = await chat.Query<Plan>( toolResults: [new( "tool-1", "Found a table" )] );
		Assert.IsTrue( final.HasData );
		Assert.AreEqual( "Pool", final.Data.Name );
		Assert.AreEqual( 0.03m, chat.TotalCostUsd );
		Assert.IsTrue( JsonElement.DeepEquals( api.Requests[0].JsonSchema.Value, api.Requests[1].JsonSchema.Value ) );
	}

	/// <summary>
	/// Refused or incomplete responses preserve their status without attempting JSON deserialization.
	/// </summary>
	[TestMethod]
	[DataRow( "Stop", "Unable to help." )]
	[DataRow( "Length", null )]
	public async Task UnsuccessfulStructuredResponse( string finish, string refusal )
	{
		var response = new Ai.Response<Plan>( new AiQueryResponse { Text = "Not JSON", IsSuccess = false, FinishReason = finish, Refusal = refusal } );
		response.ReadData();
		Assert.IsFalse( response.HasData );
		Assert.IsNull( response.Data );
		Assert.AreEqual( refusal, response.Refusal );
	}

	/// <summary>
	/// Local conversion failures retain acknowledged history and costs for inspection and safe replay.
	/// </summary>
	[TestMethod]
	public async Task InvalidStructuredDataRetainsAccounting()
	{
		var api = new FakeApi();
		api.Replies.Enqueue( new AiChatResponse { IsSuccess = true, FinishReason = "Stop", Text = "{\"balls\":\"invalid\"}", CostUsd = 0.01m } );
		var chat = new Ai.Chat( Guid.NewGuid(), api );
		await Assert.ThrowsExceptionAsync<JsonException>( () => chat.Query<Plan>( "Plan a table" ) );
		Assert.AreEqual( 0.01m, chat.TotalCostUsd );
		Assert.AreEqual( 2, chat.Messages.Count );
		Assert.IsNotNull( chat.LastResponse );
	}

	/// <summary>
	/// A chat supplies its own backend ID and retains images, tool calls and tool results in order.
	/// </summary>
	[TestMethod]
	public async Task HistoryAndUsage()
	{
		var api = new FakeApi();
		api.Replies.Enqueue( new AiChatResponse
		{
			IsSuccess = true,
			Text = "Inspecting the scene.",
			CostUsd = 0.01m,
			ToolCalls = [new() { Id = "tool-1", Name = "FindObjects", Arguments = "{}" }]
		} );
		api.Replies.Enqueue( new AiChatResponse { IsSuccess = true, Text = "Done.", CostUsd = 0.02m } );
		var chat = new Ai.Chat( Guid.NewGuid(), api );
		var images = new List<string> { "https://example.com/table.png" };
		var first = await chat.Query( "Create this table", images: images );
		images.Clear();
		var last = await chat.Query( toolResults: [new( "tool-1", "Found a table" )] );

		Assert.AreEqual( chat.Id, api.Requests[0].Id );
		Assert.AreEqual( chat.Id, api.Requests[1].Id );
		Assert.AreNotEqual( Guid.Empty, api.Requests[0].RequestId );
		Assert.AreNotEqual( api.Requests[0].RequestId, api.Requests[1].RequestId );
		CollectionAssert.AreEqual( new[] { "user", "assistant", "tool", "assistant" }, chat.Messages.Select( x => x.Role ).ToArray() );
		Assert.AreEqual( "Create this table", chat.Messages[0].Text );
		Assert.AreEqual( "https://example.com/table.png", chat.Messages[0].Images[0] );
		Assert.AreSame( first, chat.Messages[1].Response );
		Assert.AreEqual( "FindObjects", chat.Messages[1].Response.ToolCalls[0].Name );
		Assert.AreEqual( "tool-1", chat.Messages[2].ToolCallId );
		Assert.AreEqual( "Found a table", chat.Messages[2].Text );
		Assert.AreSame( last, chat.LastResponse );
		Assert.AreEqual( 0.03m, chat.TotalCostUsd );
	}

	/// <summary>
	/// Failed requests leave local state intact, while retries and response replays count only once.
	/// </summary>
	[TestMethod]
	public async Task RetryDoesNotDuplicateHistoryOrCost()
	{
		var api = new FakeApi { FailRequest = true };
		var chat = new Ai.Chat( Guid.NewGuid(), api );
		var requestId = Guid.NewGuid();
		var error = await Assert.ThrowsExceptionAsync<Ai.RequestException>( () => chat.Query( "Hello", requestId: requestId ) );
		Assert.AreEqual( "network_error", error.Code );
		Assert.AreEqual( 0, chat.Messages.Count );
		Assert.AreEqual( 0m, chat.TotalCostUsd );
		Assert.IsNull( chat.LastResponse );

		api.FailRequest = false;
		var response = new AiChatResponse { IsSuccess = true, Text = "Hello!", CostUsd = 0.01m };
		api.Replies.Enqueue( response );
		api.Replies.Enqueue( response );
		var first = await chat.Query( "Hello", requestId: requestId );
		await chat.Query( "Hello", requestId: requestId );
		Assert.AreEqual( 2, chat.Messages.Count );
		Assert.AreEqual( 0.01m, chat.TotalCostUsd );
		Assert.AreSame( first, chat.LastResponse );
		Assert.IsTrue( api.Requests.All( x => x.RequestId == requestId ) );
	}

	/// <summary>
	/// Missing provider costs remain unknown instead of presenting an incomplete sum as the total.
	/// </summary>
	[TestMethod]
	public async Task MissingCostsRemainUnknown()
	{
		var api = new FakeApi();
		api.Replies.Enqueue( new AiChatResponse { IsSuccess = true, CostUsd = null } );
		api.Replies.Enqueue( new AiChatResponse { IsSuccess = true, CostUsd = 0.01m } );
		var chat = new Ai.Chat( Guid.NewGuid(), api );
		await chat.Query( "Hello" );
		await chat.Query( "Continue" );
		Assert.IsNull( chat.TotalCostUsd );
		Assert.AreEqual( 0.01m, chat.LastResponse.CostUsd );
	}

	sealed class FakeApi : ServiceApi.IAiApi
	{
		public List<AiChatInput> Requests { get; } = [];
		public Queue<AiChatResponse> Replies { get; } = [];
		public bool FailRequest { get; set; }

		public Task<AiChatResponse> QueryChat( AiChatInput input, CancellationToken cancellationToken = default )
		{
			Requests.Add( input );
			if ( FailRequest ) throw new HttpRequestException( "Connection lost" );
			var response = Replies.Dequeue();
			response.RequestId = input.RequestId;
			return Task.FromResult( response );
		}

		public Task<AiChat> CreateChat( AiChatCreate options, CancellationToken cancellationToken = default ) => throw new NotSupportedException();
		public Task<AiQueryResponse> Query( AiQueryRequest input, CancellationToken cancellationToken = default ) => throw new NotSupportedException();
	}
}
