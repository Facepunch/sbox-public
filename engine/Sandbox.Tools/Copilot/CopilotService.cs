using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Editor.Copilot;

/// <summary>
/// Manages communication with the GitHub Copilot Chat API (which exposes
/// Claude, GPT, and Gemini models) and runs the agent tool-call loop.
///
/// The streaming format is OpenAI-compatible. Tool calls are accumulated
/// across SSE delta chunks (each delta carries a partial JSON arguments
/// fragment for one tool_call slot keyed by its index).
///
/// Token lifecycle:
///   GitHub access token  → device flow, stored in CopilotPreferences
///   Copilot bearer token → short-lived JWT, refreshed automatically
/// </summary>
public static class CopilotService
{
	private const string CopilotTokenUrl   = "https://api.github.com/copilot_internal/v2/token";
	private const string ChatCompletionUrl = "https://api.githubcopilot.com/chat/completions";

	private static string SystemPrompt => GameDevPrompts.SystemPrompt
		+ "\n\nYou have direct access to the running s&box editor through tools. "
		+ "When the user asks for a change, prefer to *do* it via the tools rather than just describing it. "
		+ "Always call list_scene_objects (or get_gameobject) to inspect state before mutating it. "
		+ "After multiple successful tool calls, summarise what you did in one short paragraph.";

	// ── cached Copilot token ──────────────────────────────────────────────────

	private static string   _copilotToken;
	private static DateTime _tokenExpiry = DateTime.MinValue;

	private static readonly HttpClient _http = new()
	{
		DefaultRequestHeaders =
		{
			UserAgent = { new ProductInfoHeaderValue( "sbox-copilot", "1.0" ) },
			Accept    = { new MediaTypeWithQualityHeaderValue( "application/json" ) }
		},
		Timeout = TimeSpan.FromMinutes( 10 )
	};

	// ── public message types ──────────────────────────────────────────────────

	public class ChatMessage
	{
		[JsonPropertyName( "role" )]                                        public string         Role         { get; set; }
		[JsonPropertyName( "content" )]                                     public string         Content      { get; set; }
		[JsonPropertyName( "name" ),         JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] public string Name      { get; set; }
		[JsonPropertyName( "tool_call_id" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] public string ToolCallId { get; set; }
		[JsonPropertyName( "tool_calls" ),   JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] public List<ToolCall> ToolCalls { get; set; }

		public static ChatMessage User     ( string text ) => new() { Role = "user",      Content = text };
		public static ChatMessage Assistant( string text ) => new() { Role = "assistant", Content = text };
		public static ChatMessage System   ( string text ) => new() { Role = "system",    Content = text };
		public static ChatMessage Tool     ( string callId, string name, string content ) =>
			new() { Role = "tool", Content = content, ToolCallId = callId, Name = name };
	}

	public class ToolCall
	{
		[JsonPropertyName( "id" )]       public string         Id       { get; set; }
		[JsonPropertyName( "type" )]     public string         Type     { get; set; } = "function";
		[JsonPropertyName( "function" )] public ToolCallFunc   Function { get; set; }
	}

	public class ToolCallFunc
	{
		[JsonPropertyName( "name" )]      public string Name      { get; set; }
		[JsonPropertyName( "arguments" )] public string Arguments { get; set; }
	}

	// ── delta callback bag ────────────────────────────────────────────────────

	/// <summary>
	/// Callbacks fired during a streaming agent turn. All marshal to the main thread.
	/// </summary>
	public class StreamCallbacks
	{
		/// <summary>Called for each chunk of assistant text.</summary>
		public Action<string>                       OnTextDelta;

		/// <summary>
		/// Called when the agent decides to call tools. The handler must return the
		/// tool result JSON for each call, in order. The chat widget uses this to
		/// gate the call behind approval, run it on the main thread, and render
		/// a tool bubble.
		/// </summary>
		public Func<List<ToolCall>, Task<List<string>>> OnToolCalls;

		/// <summary>Called once at the end of the entire agent loop (success).</summary>
		public Action OnComplete;

		/// <summary>Called on any unrecoverable error.</summary>
		public Action<string> OnError;
	}

	// ── request / SSE shapes ──────────────────────────────────────────────────

	private class ChatRequest
	{
		[JsonPropertyName( "model" )]      public string            Model    { get; set; }
		[JsonPropertyName( "messages" )]   public List<ChatMessage> Messages { get; set; }
		[JsonPropertyName( "stream" )]     public bool              Stream   { get; set; } = true;
		[JsonPropertyName( "temperature" )]public double            Temperature { get; set; } = 0.2;
		[JsonPropertyName( "tools" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public List<object>      Tools      { get; set; }
		[JsonPropertyName( "tool_choice" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public string            ToolChoice { get; set; }
	}

	private class SseToolCallDelta
	{
		[JsonPropertyName( "index" )]    public int             Index    { get; set; }
		[JsonPropertyName( "id" )]       public string          Id       { get; set; }
		[JsonPropertyName( "type" )]     public string          Type     { get; set; }
		[JsonPropertyName( "function" )] public ToolCallFunc    Function { get; set; }
	}

	private class SseDelta
	{
		[JsonPropertyName( "content" )]    public string                 Content   { get; set; }
		[JsonPropertyName( "tool_calls" )] public List<SseToolCallDelta> ToolCalls { get; set; }
	}

	private class SseChoice
	{
		[JsonPropertyName( "delta" )]         public SseDelta Delta        { get; set; }
		[JsonPropertyName( "finish_reason" )] public string   FinishReason { get; set; }
	}

	private class SseChunk
	{
		[JsonPropertyName( "choices" )] public List<SseChoice> Choices { get; set; }
	}

	private class CopilotTokenResponse
	{
		[JsonPropertyName( "token" )]      public string Token     { get; set; }
		[JsonPropertyName( "expires_at" )] public long   ExpiresAt { get; set; }
	}

	// ── public API ────────────────────────────────────────────────────────────

	public static bool IsAvailable => CopilotPreferences.IsSignedIn;

	/// <summary>
	/// Run an agent turn with optional tool-calling. The conversation history is
	/// updated in-place (the new assistant + tool messages are appended).
	/// </summary>
	public static async Task RunAgentAsync(
		List<ChatMessage>  history,
		string             userMessage,
		StreamCallbacks    callbacks,
		CancellationToken  cancellation = default )
	{
		try
		{
			var token = await EnsureCopilotTokenAsync( cancellation );

			// Build the working messages list — we keep this and add to it across tool turns
			var messages = new List<ChatMessage> { ChatMessage.System( SystemPrompt ) };

			int historyStart = Math.Max( 0, history.Count - 20 );
			for ( int i = historyStart; i < history.Count; i++ )
				messages.Add( history[i] );

			messages.Add( ChatMessage.User( userMessage ) );
			history.Add( ChatMessage.User( userMessage ) );

			// Multi-step agent loop — caps to avoid runaway loops
			const int MaxSteps = 12;
			for ( int step = 0; step < MaxSteps; step++ )
			{
				cancellation.ThrowIfCancellationRequested();

				var (assistantText, toolCalls) = await StreamOneTurnAsync( token, messages, callbacks, cancellation );

				// Build the assistant message that produced these
				var assistantMsg = new ChatMessage
				{
					Role      = "assistant",
					Content   = string.IsNullOrEmpty( assistantText ) ? null : assistantText,
					ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
				};
				messages.Add( assistantMsg );
				history.Add( assistantMsg );

				if ( toolCalls.Count == 0 )
					break; // done — no tools requested

				if ( callbacks.OnToolCalls == null )
				{
					MainThread.Queue( () => callbacks.OnError?.Invoke( "Tool calls were issued but no handler is wired." ) );
					return;
				}

				// Run the tools (handler may throw / return errors — they're fed back as tool messages)
				var resultsTask = (Task<List<string>>)null;
				MainThread.Queue( () => resultsTask = callbacks.OnToolCalls( toolCalls ) );
				while ( resultsTask == null && !cancellation.IsCancellationRequested )
					await Task.Delay( 16, cancellation );
				var results = await resultsTask!;

				for ( int i = 0; i < toolCalls.Count; i++ )
				{
					var toolMsg = ChatMessage.Tool(
						toolCalls[i].Id,
						toolCalls[i].Function?.Name ?? "unknown",
						i < results.Count ? results[i] : "{\"ok\":false,\"error\":\"no result\"}" );
					messages.Add( toolMsg );
					history.Add( toolMsg );
				}
			}

			MainThread.Queue( () => callbacks.OnComplete?.Invoke() );
		}
		catch ( OperationCanceledException )
		{
			MainThread.Queue( () => callbacks.OnComplete?.Invoke() );
		}
		catch ( Exception ex )
		{
			MainThread.Queue( () => callbacks.OnError?.Invoke( ex.Message ) );
		}
	}

	// ── one streaming turn (returns final text + accumulated tool calls) ──────

	private static async Task<(string Text, List<ToolCall> ToolCalls)> StreamOneTurnAsync(
		string             token,
		List<ChatMessage>  messages,
		StreamCallbacks    callbacks,
		CancellationToken  cancellation )
	{
		var requestBody = JsonSerializer.Serialize( new ChatRequest
		{
			Model       = CopilotPreferences.SelectedModel,
			Messages    = messages,
			Stream      = true,
			Temperature = 0.2,
			Tools       = CopilotPreferences.AgentEnabled ? AgentTools.AsApiPayload() : null,
		}, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull } );

		using var request = new HttpRequestMessage( HttpMethod.Post, ChatCompletionUrl )
		{
			Content = new StringContent( requestBody, Encoding.UTF8, "application/json" )
		};
		request.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", token );
		request.Headers.TryAddWithoutValidation( "Copilot-Integration-Id", "sbox-copilot" );
		request.Headers.TryAddWithoutValidation( "editor-version",         "sbox/1.0.0" );
		request.Headers.TryAddWithoutValidation( "editor-plugin-version",  "sbox-copilot/1.0.0" );

		using var response = await _http.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellation );
		if ( !response.IsSuccessStatusCode )
		{
			var body = await response.Content.ReadAsStringAsync( cancellation );
			throw new HttpRequestException( $"API error {(int)response.StatusCode}: {body}" );
		}

		await using var stream = await response.Content.ReadAsStreamAsync( cancellation );
		using var reader = new System.IO.StreamReader( stream );

		var text       = new StringBuilder();
		var toolBuf    = new SortedDictionary<int, ToolCall>(); // accumulated by index
		var argBuf     = new SortedDictionary<int, StringBuilder>();

		while ( !cancellation.IsCancellationRequested )
		{
			var line = await reader.ReadLineAsync( cancellation );
			if ( line is null ) break;
			if ( string.IsNullOrEmpty( line ) ) continue;
			if ( !line.StartsWith( "data: " ) ) continue;

			var data = line["data: ".Length..];
			if ( data == "[DONE]" ) break;

			SseChunk chunk;
			try   { chunk = JsonSerializer.Deserialize<SseChunk>( data ); }
			catch { continue; }

			var delta = chunk?.Choices?[0]?.Delta;
			if ( delta == null ) continue;

			if ( !string.IsNullOrEmpty( delta.Content ) )
			{
				var captured = delta.Content;
				text.Append( captured );
				MainThread.Queue( () => callbacks.OnTextDelta?.Invoke( captured ) );
			}

			if ( delta.ToolCalls != null )
			{
				foreach ( var d in delta.ToolCalls )
				{
					if ( !toolBuf.TryGetValue( d.Index, out var tc ) )
					{
						tc = new ToolCall { Type = "function", Function = new ToolCallFunc() };
						toolBuf[d.Index] = tc;
						argBuf [d.Index] = new StringBuilder();
					}
					if ( !string.IsNullOrEmpty( d.Id ) )                       tc.Id            = d.Id;
					if ( !string.IsNullOrEmpty( d.Type ) )                     tc.Type          = d.Type;
					if ( !string.IsNullOrEmpty( d.Function?.Name ) )           tc.Function.Name = d.Function.Name;
					if ( !string.IsNullOrEmpty( d.Function?.Arguments ) )      argBuf[d.Index].Append( d.Function.Arguments );
				}
			}
		}

		// Finalise tool-call argument strings
		var calls = new List<ToolCall>( toolBuf.Count );
		foreach ( var kv in toolBuf )
		{
			kv.Value.Function.Arguments = argBuf[kv.Key].ToString();
			if ( string.IsNullOrEmpty( kv.Value.Id ) )
				kv.Value.Id = "call_" + Guid.NewGuid().ToString( "N" )[..8];
			calls.Add( kv.Value );
		}

		return ( text.ToString(), calls );
	}

	// ── token management ──────────────────────────────────────────────────────

	private static async Task<string> EnsureCopilotTokenAsync( CancellationToken cancellation )
	{
		if ( _copilotToken != null && DateTime.UtcNow < _tokenExpiry - TimeSpan.FromSeconds( 60 ) )
			return _copilotToken;

		var githubToken = CopilotPreferences.GitHubAccessToken;
		if ( string.IsNullOrWhiteSpace( githubToken ) )
			throw new InvalidOperationException( "Not signed in to GitHub. Please sign in via the AI Copilot panel." );

		using var request = new HttpRequestMessage( HttpMethod.Get, CopilotTokenUrl );
		request.Headers.Authorization = new AuthenticationHeaderValue( "token", githubToken );

		var response = await _http.SendAsync( request, cancellation );
		var json     = await response.Content.ReadAsStringAsync( cancellation );

		if ( !response.IsSuccessStatusCode )
		{
			if ( response.StatusCode == System.Net.HttpStatusCode.Unauthorized )
			{
				MainThread.Queue( CopilotPreferences.ClearAuth );
				throw new UnauthorizedAccessException( "GitHub token is no longer valid. Please sign in again." );
			}
			throw new HttpRequestException( $"Failed to get Copilot token ({response.StatusCode}): {json}" );
		}

		var tokenResp = JsonSerializer.Deserialize<CopilotTokenResponse>( json );
		_copilotToken = tokenResp.Token;
		_tokenExpiry  = DateTimeOffset.FromUnixTimeSeconds( tokenResp.ExpiresAt ).UtcDateTime;
		return _copilotToken;
	}

	public static void InvalidateToken()
	{
		_copilotToken = null;
		_tokenExpiry  = DateTime.MinValue;
	}
}
