using System.Text.Json;
using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// An AI request created by AiConfig.CreateQuery. Add input and images, then Run it independently
/// or send its message to a chat. Build and submit on the calling game context.
/// </summary>
public sealed partial class AiQuery
{
	readonly AiConfig config;
	Ai.Chat pendingChat;

	internal AiQuery( AiConfig config = null ) => this.config = config ?? new AiConfig();

	/// <summary>Text to send. May be omitted when images are supplied.</summary>
	public string Input { get; set; }

	/// <summary>
	/// Runs the query and any configured tools. After an interrupted tool turn, Run again on this
	/// query to resume the retained submission without rerunning completed tools. Reaching MaxToolRounds
	/// is terminal for this query; create a new query with a higher configured limit if needed.
	/// </summary>
	public Task<Ai.Response> Run( CancellationToken cancellationToken = default ) => Run( Backend.Ai, cancellationToken );

	/// <summary>
	/// Runs the query and deserializes its final JSON answer as T. Tool-enabled queries describe
	/// the output schema in their instructions; other queries use the backend's strict output schema.
	/// Check HasData before reading Data. Invalid final JSON leaves HasData false and preserves the text and usage.
	/// Reaching MaxToolRounds is terminal for this query, just as for the untyped Run overload.
	/// </summary>
	public Task<Ai.Response<T>> Run<T>( CancellationToken cancellationToken = default ) => Run<T>( Backend.Ai, cancellationToken );

	internal async Task<Ai.Response> Run( ServiceApi.IAiApi api, CancellationToken cancellationToken )
		=> new( await Execute( api, null, cancellationToken ) );

	internal async Task<Ai.Response<T>> Run<T>( ServiceApi.IAiApi api, CancellationToken cancellationToken )
	{
		var response = new Ai.Response<T>( await Execute( api, Ai.CreateSchema<T>(), cancellationToken ) );
		response.ReadData();
		return response;
	}

	async Task<AiQueryResponse> Execute( ServiceApi.IAiApi api, JsonElement? schema, CancellationToken cancellationToken )
	{
		if ( pendingChat?.HasPendingTurn == true )
		{
			var resumed = await pendingChat.Retry( cancellationToken );
			pendingChat = null;
			return resumed.Source;
		}

		var input = Input;
		var images = await PrepareImages( cancellationToken );
		if ( string.IsNullOrWhiteSpace( input ) && images.Count == 0 )
			throw new InvalidOperationException( "Supply Input or an image before running the query." );

		if ( config.HasTools )
		{
			pendingChat ??= await config.CreateChat( api, cancellationToken, schema );
			var result = await pendingChat.Send( input, images, cancellationToken );
			pendingChat = null;
			return result.Source;
		}

		return await Ai.Send( () => api.Query( new AiQueryRequest
		{
			GamePackageIdent = Application.GamePackage is { IsRemote: true } package ? package.FullIdent : null,
			Input = input,
			Instructions = config.Instructions,
			Images = images,
			Profile = AiProfile.Capable,
			JsonSchema = schema
		}, cancellationToken ) );
	}
}
