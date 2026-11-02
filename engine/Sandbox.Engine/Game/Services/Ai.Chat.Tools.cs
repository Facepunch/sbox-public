using System.Text.Json;
using System.Threading;

namespace Sandbox.Services;

public static partial class Ai
{
	public sealed partial class Chat
	{
		int operationInProgress;
		PendingTurn pendingTurn;

		sealed class PendingTurn
		{
			public AiChatInput Request;
			public Response Response;
			public readonly List<AiToolResult> Results = [];
			public int ToolRounds;
		}

		/// <summary>
		/// Maximum batches of tools executed during one Send, including its retries. Defaults to 8.
		/// If the limit is reached, increase it before Retry to continue the pending turn.
		/// </summary>
		public int MaxToolRounds { get; set; } = 8;

		/// <summary>
		/// Sends a message and executes registered tool callbacks until the assistant finishes or refuses.
		/// Messages contains user messages and final replies. CostUsd describes the final reply;
		/// TotalCostUsd includes the chat's tool rounds. Only one operation may run on a chat at a time.
		/// After a request failure or cancellation, use Retry to resume the retained submission.
		/// </summary>
		/// <param name="input">The next user message and current game context. May be omitted when images are supplied.</param>
		/// <param name="images">Optional HTTP(S) image URLs or base64 image data URLs. Use the AiQuery overload for textures and bitmaps.</param>
		/// <param name="cancellationToken">Cancels network requests and is passed to asynchronous tool callbacks for this turn.</param>
		/// <returns>The final assistant response after automatic tool execution. Check IsSuccess and Refusal before using its text.</returns>
		/// <exception cref="InvalidOperationException">Another operation or interrupted turn is pending, or MaxToolRounds is reached.</exception>
		public Task<Response> Send( string input = null, IEnumerable<string> images = null, CancellationToken cancellationToken = default )
		{
			var query = new AiQuery { Input = input };
			foreach ( var image in images ?? [] ) query.AddImage( image );
			return Send( query, cancellationToken );
		}

		/// <summary>
		/// Sends a prepared query's input and images, executing registered tool callbacks automatically.
		/// Uses the query's input and images; the chat retains its own configuration.
		/// </summary>
		/// <param name="query">A message builder with Input and/or images. Its contents are captured before the turn begins.</param>
		/// <param name="cancellationToken">Cancels image preparation, network requests and asynchronous tool callbacks for this turn.</param>
		/// <returns>The final assistant response after all automatically handled tool rounds.</returns>
		public async Task<Response> Send( AiQuery query, CancellationToken cancellationToken = default )
		{
			ArgumentNullException.ThrowIfNull( query );
			EnterOperation();
			try
			{
				if ( pendingTurn is not null ) throw new InvalidOperationException( "Use Retry to finish the pending turn, or Reset to start a new conversation." );
				if ( MaxToolRounds < 1 ) throw new InvalidOperationException( "MaxToolRounds must be positive." );
				var input = query.Input;
				var imageList = await query.PrepareImages( cancellationToken );
				if ( string.IsNullOrWhiteSpace( input ) && imageList.Count == 0 )
					throw new ArgumentException( "Supply a message or an image.", nameof( query ) );
				cancellationToken.ThrowIfCancellationRequested();
				pendingTurn = new PendingTurn
				{
					Request = new AiChatInput { Id = Id, RequestId = Guid.NewGuid(), Input = input, Images = imageList }
				};
				return await ContinueTurn( cancellationToken );
			}
			finally
			{
				Volatile.Write( ref operationInProgress, 0 );
			}
		}

		/// <summary>
		/// Resumes an interrupted Send using its retained request ID and payload. Completed tool callbacks
		/// are not rerun. A callback cancelled during execution is retained as a tool error rather than rerun;
		/// cancellation does not undo game actions already performed by a callback or server processing.
		/// </summary>
		/// <param name="cancellationToken">A token for the resumed operation, usually a fresh token after cancellation.</param>
		/// <returns>The final response for the original interrupted turn.</returns>
		/// <exception cref="InvalidOperationException">No interrupted turn exists, another operation is running, or MaxToolRounds is reached.</exception>
		public async Task<Response> Retry( CancellationToken cancellationToken = default )
		{
			EnterOperation();
			try
			{
				if ( pendingTurn is null ) throw new InvalidOperationException( "There is no pending Send operation." );
				return await ContinueTurn( cancellationToken );
			}
			finally
			{
				Volatile.Write( ref operationInProgress, 0 );
			}
		}

		/// <summary>
		/// Starts a fresh backend conversation with the same instructions and tool callbacks. Clears local
		/// history, pending requests and usage after creation succeeds. Use this to recover from a permanently
		/// rejected request. Game actions already performed by tools are not undone.
		/// </summary>
		/// <param name="cancellationToken">Cancels creation of the new conversation. Failure leaves the existing chat state intact.</param>
		public async Task Reset( CancellationToken cancellationToken = default )
		{
			EnterOperation();
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				var chat = await Ai.Send( () => api.CreateChat( createOptions, cancellationToken ) );
				Id = chat.Id;
				pendingTurn = null;
				messages.Clear();
				LastResponse = null;
				TotalCostUsd = chat.TotalCostUsd;
			}
			finally
			{
				Volatile.Write( ref operationInProgress, 0 );
			}
		}

		void EnterOperation()
		{
			if ( Interlocked.CompareExchange( ref operationInProgress, 1, 0 ) != 0 )
				throw new InvalidOperationException( "A chat operation is already in progress." );
		}

		async Task<Response> ContinueTurn( CancellationToken cancellationToken )
		{
			var turn = pendingTurn;
			while ( true )
			{
				cancellationToken.ThrowIfCancellationRequested();
				if ( turn.Request is not null )
				{
					turn.Response = await QueryRequest( turn.Request, cancellationToken );
					turn.Request = null;
					turn.Results.Clear();
				}

				var response = turn.Response;
				if ( !response.IsSuccess || !string.IsNullOrEmpty( response.Refusal ) || response.ToolCalls.Count == 0 )
				{
					messages.Add( new Message { Role = "assistant", Text = response.Text, Response = response } );
					LastResponse = response;
					pendingTurn = null;
					return response;
				}

				if ( turn.ToolRounds >= MaxToolRounds )
					throw new InvalidOperationException( "The AI turn reached MaxToolRounds." );

				while ( turn.Results.Count < response.ToolCalls.Count )
				{
					cancellationToken.ThrowIfCancellationRequested();
					var call = response.ToolCalls[turn.Results.Count];

					AiToolResult result;
					try
					{
						if ( call.Name is null || !tools.TryGetValue( call.Name, out var tool ) )
							result = ToolError( call.Id, "unknown_tool", "No tool is registered with that name." );
						else
						{
							var value = await tool.Invoke( call.Arguments, context, cancellationToken );
							result = new AiToolResult { ToolCallId = call.Id, Content = value.GetRawText() };
						}
					}
					catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
					{
						result = ToolError( call.Id, "tool_cancelled", "The callback was cancelled; its game actions may be incomplete." );
						turn.Results.Add( result );
						throw;
					}
					catch ( Exception exception )
					{
						result = ToolError( call.Id, "tool_error", exception.Message );
					}
					turn.Results.Add( result );
				}

				turn.ToolRounds++;
				turn.Request = new AiChatInput { Id = Id, RequestId = Guid.NewGuid(), ToolResults = turn.Results.ToList() };
			}
		}

		static AiToolResult ToolError( string id, string code, string message ) => new()
		{
			ToolCallId = id,
			Content = JsonSerializer.Serialize( new { error = new { code, message } } )
		};
	}
}
