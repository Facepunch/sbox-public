using System.Text.Json;
using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// Shared instructions, tool types and game context for AI chats and queries.
/// </summary>
public sealed class AiConfig
{
	readonly List<AiToolRegistration> tools = [];

	/// <summary>Persistent instructions describing the assistant's role and behaviour.</summary>
	public string Instructions { get; set; }

	/// <summary>Game-owned context supplied to tools. This object is not sent to the AI.</summary>
	public object Context { get; set; }

	/// <summary>Maximum tool batches per turn, including retries. Defaults to 8.</summary>
	public int MaxToolRounds { get; set; } = 8;

	/// <summary>
	/// Registers a tool class. Its name identifies the tool; XML summaries on the class, inputs and
	/// result describe it to the AI. Each invocation deserializes a fresh instance before calling Run.
	/// </summary>
	public AiConfig AddTool<TTool>() where TTool : AiTool, new()
	{
		var tool = AiToolRegistration.Create<TTool>();
		if ( tools.Any( x => x.Definition.Name == tool.Definition.Name ) )
			throw new ArgumentException( $"A tool named '{tool.Definition.Name}' is already registered." );
		tools.Add( tool );
		return this;
	}

	/// <summary>Builds a query using a snapshot of this configuration. Call Run to submit it.</summary>
	public AiQuery CreateQuery( string input = null ) => new( Snapshot() ) { Input = input };

	/// <summary>Creates a persistent conversation using a snapshot of this configuration.</summary>
	public Task<Ai.Chat> CreateChat( CancellationToken cancellationToken = default )
		=> CreateChat( Backend.Ai, cancellationToken );

	internal bool HasTools => tools.Count > 0;

	internal AiConfig Snapshot()
	{
		var copy = new AiConfig { Instructions = Instructions, Context = Context, MaxToolRounds = MaxToolRounds };
		copy.tools.AddRange( tools );
		return copy;
	}

	internal async Task<Ai.Chat> CreateChat( ServiceApi.IAiApi api, CancellationToken cancellationToken, JsonElement? outputSchema = null )
	{
		cancellationToken.ThrowIfCancellationRequested();
		if ( MaxToolRounds < 1 ) throw new InvalidOperationException( "MaxToolRounds must be positive." );
		var context = Context;
		var registered = tools.ToArray();
		var rounds = MaxToolRounds;
		var options = new AiChatCreate
		{
			GamePackageIdent = Application.GamePackage is { IsRemote: true } package ? package.FullIdent : null,
			Instructions = outputSchema is null ? Instructions : $"{Instructions}\nReturn the final answer as JSON matching this schema, without markdown: {outputSchema.Value.GetRawText()}",
			Profile = AiProfile.Agent,
			Tools = registered.Select( x => x.Definition ).ToList()
		};
		var chat = await Ai.Send( () => api.CreateChat( options, cancellationToken ) );
		return new Ai.Chat( chat.Id, api, chat.TotalCostUsd, registered, context, options ) { MaxToolRounds = rounds };
	}
}
