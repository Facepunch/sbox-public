using System.Text.Json;

namespace Sandbox.Services;

public static partial class Ai
{
	/// <summary>
	/// Shared instructions, tool types and game context for AI chats and queries.
	/// </summary>
	public sealed class Config
	{
		readonly List<AiToolRegistration> tools = [];

		/// <summary>
		/// Persistent instructions describing the assistant's role and behaviour.
		/// </summary>
		public string Instructions { get; set; }

		/// <summary>
		/// Game-owned context supplied to tools. This object is not sent to the AI.
		/// </summary>
		public object Context { get; set; }

		/// <summary>
		/// Maximum tool batches per turn, including retries. Defaults to 30.
		/// </summary>
		public int MaxToolRounds { get; set; } = 30;

		/// <summary>
		/// Registers a tool class. Its name identifies the tool; XML summaries on the class, inputs and
		/// result describe it to the AI. Each invocation deserializes a fresh instance before calling Run.
		/// </summary>
		public Config AddTool<TTool>() where TTool : BaseTool, new()
		{
			var tool = AiToolRegistration.Create<TTool>();
			if ( tools.Any( x => x.Definition.Name == tool.Definition.Name ) )
				throw new ArgumentException( $"A tool named '{tool.Definition.Name}' is already registered." );
			tools.Add( tool );
			return this;
		}

		/// <summary>
		/// Builds a query using a snapshot of this configuration. Call Run to submit it.
		/// </summary>
		public Query CreateQuery( string input = null ) => new( Snapshot() ) { Input = input };

		/// <summary>
		/// Creates a local conversation using a snapshot of this configuration, without a server request.
		/// </summary>
		public Ai.Chat CreateChat() => CreateChat( Backend.Ai );

		internal bool HasTools => tools.Count > 0;

		internal Config Snapshot()
		{
			var copy = new Config { Instructions = Instructions, Context = Context, MaxToolRounds = MaxToolRounds };
			copy.tools.AddRange( tools );
			return copy;
		}

		internal Ai.Chat CreateChat( ServiceApi.IAiApi api, JsonElement? outputSchema = null )
		{
			if ( MaxToolRounds < 1 ) throw new InvalidOperationException( "MaxToolRounds must be positive." );
			var context = Context;
			var registered = tools.ToArray();
			var rounds = MaxToolRounds;
			var options = new AiChatOptions
			{
				GamePackageIdent = Application.GamePackage is { IsRemote: true } package ? package.FullIdent : null,
				Instructions = outputSchema is null ? Instructions : $"{Instructions}\nReturn the final answer as JSON matching this schema, without markdown: {outputSchema.Value.GetRawText()}",
				Profile = AiProfile.Agent,
				Tools = registered.Select( x => x.Definition ).ToList()
			};
			return new Ai.Chat( api, registered, context, options ) { MaxToolRounds = rounds };
		}
	}
}
