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
			public Message UserMessage;
			public Response Response;
			public int CompletedTools;
			public int ToolRounds;
		}

		/// <summary>
		/// Maximum batches of tools executed during one Send, including its retries. Defaults to 30.
		/// If the limit is reached, increase it before Retry to continue the pending turn.
		/// </summary>
		public int MaxToolRounds { get; set; } = 30;

		/// <summary>
		/// Sends a message and executes registered tool callbacks until the assistant finishes or refuses.
		/// Messages contains user messages and final replies. CreditsSpent describes the final reply;
		/// TotalCreditsSpent includes the chat's tool rounds. Only one operation may run on a chat at a time.
		/// After a request failure or cancellation, use Retry to resume the retained submission.
		/// </summary>
		/// <param name="input">The next user message and current game context. May be omitted when images are supplied.</param>
		/// <param name="images">Optional HTTP(S) image URLs or base64 image data URLs. Use the Query overload for textures and bitmaps.</param>
		/// <param name="cancellationToken">Cancels network requests and is passed to asynchronous tool callbacks for this turn.</param>
		/// <returns>The final assistant response after automatic tool execution. Check IsSuccess and Refusal before using its text.</returns>
		/// <exception cref="InvalidOperationException">Another operation or interrupted turn is pending, or MaxToolRounds is reached.</exception>
		public Task<Response> Send( string input = null, IEnumerable<string> images = null, CancellationToken cancellationToken = default )
		{
			var query = new Query { Input = input };
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
		public async Task<Response> Send( Query query, CancellationToken cancellationToken = default )
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
				conversation.Messages.Add( AiChat.CreateUserMessage( input, imageList ) );
				pendingTurn = new PendingTurn
				{
					UserMessage = new Message { Role = "user", Text = input, Images = imageList.AsReadOnly() }
				};
				return await ContinueTurn( cancellationToken );
			}
			finally
			{
				Volatile.Write( ref operationInProgress, 0 );
			}
		}

		/// <summary>
		/// Resumes an interrupted Send using its retained conversation history. Completed tool callbacks
		/// are not rerun. A callback cancelled during execution is retained as a tool error rather than rerun;
		/// cancellation does not undo game actions already performed by a callback or server processing.
		/// A response lost in transit may be generated and charged again on retry.
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
		/// Starts a fresh local conversation with the same instructions and tool callbacks. Clears
		/// history, pending requests and usage. Use this to recover from a permanently
		/// rejected request. Game actions already performed by tools are not undone.
		/// </summary>
		public void Reset()
		{
			EnterOperation();
			try
			{
				conversation.Id = Guid.NewGuid();
				conversation.Messages.Clear();
				pendingTurn = null;
				messages.Clear();
				LastResponse = null;
				conversation.TotalCreditsSpent = 0m;
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
				if ( turn.Response is null )
				{
					turn.Response = await QueryRequest( cancellationToken );
					turn.CompletedTools = 0;
					if ( turn.UserMessage is not null )
					{
						messages.Add( turn.UserMessage );
						turn.UserMessage = null;
					}
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

				while ( turn.CompletedTools < response.ToolCalls.Count )
				{
					cancellationToken.ThrowIfCancellationRequested();
					var call = response.ToolCalls[turn.CompletedTools];

					AiToolResult result;
					if ( call.Name?.StartsWith( "sb_", StringComparison.Ordinal ) == true )
					{
						var reply = await Ai.Send( () => api.ExecuteChatTool( new AiChatToolInput
						{
							Id = Id,
							Options = conversation.Options,
							Call = call
						}, cancellationToken ) );
						conversation.TotalCreditsSpent = reply.TotalCreditsSpent;
						conversation.Messages.Add( AiChat.CreateToolMessage( reply.Result ) );
						turn.CompletedTools++;
						continue;
					}

					try
					{
						if ( call.Name is null || !tools.TryGetValue( call.Name, out var tool ) )
						{
							result = ToolError( call.Id, "unknown_tool", "No tool is registered with that name." );
						}
						else
						{
							result = await tool.Invoke( call.Arguments, context, cancellationToken );
							result.ToolCallId = call.Id;
						}
					}
					catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
					{
						result = ToolError( call.Id, "tool_cancelled", "The callback was cancelled; its game actions may be incomplete." );
						conversation.Messages.Add( AiChat.CreateToolMessage( result ) );
						turn.CompletedTools++;
						throw;
					}
					catch ( Exception exception )
					{
						result = ToolError( call.Id, "tool_error", exception.Message );
					}
					conversation.Messages.Add( AiChat.CreateToolMessage( result ) );
					turn.CompletedTools++;
				}

				turn.ToolRounds++;
				turn.Response = null;
			}
		}

		static AiToolResult ToolError( string id, string code, string message ) => new()
		{
			ToolCallId = id,
			Content = JsonSerializer.Serialize( new { error = new { code, message } } )
		};
	}
}
