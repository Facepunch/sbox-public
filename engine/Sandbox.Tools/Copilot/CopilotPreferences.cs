namespace Editor.Copilot;

/// <summary>
/// Persisted settings for the s&amp;box AI Copilot.
/// Tokens are stored in the editor cookie so they survive restarts.
///
/// To use it you need:
///   1. A GitHub account with an active Copilot subscription (any tier — Pro/Business/Enterprise
///      — gives access to Claude models if your org has enabled them).
///   2. A GitHub OAuth App (https://github.com/settings/applications/new). Set "Application
///      callback URL" to http://localhost — it is never called; the Device Flow is used instead.
///   3. Paste the OAuth App's Client ID into the AI Copilot settings panel.
/// </summary>
public static class CopilotPreferences
{
	// ── auth ──────────────────────────────────────────────────────────────────

	public static string OAuthClientId
	{
		get => EditorCookie.GetString( "Copilot.OAuthClientId", "" );
		set => EditorCookie.SetString( "Copilot.OAuthClientId", value );
	}

	public static string GitHubAccessToken
	{
		get => EditorCookie.GetString( "Copilot.GitHubToken", "" );
		set => EditorCookie.SetString( "Copilot.GitHubToken", value );
	}

	public static string GitHubUsername
	{
		get => EditorCookie.GetString( "Copilot.Username", "" );
		set => EditorCookie.SetString( "Copilot.Username", value );
	}

	public static bool IsSignedIn => !string.IsNullOrWhiteSpace( GitHubAccessToken );

	public static int MaxChatHistory
	{
		get => EditorCookie.Get( "Copilot.MaxChatHistory", 50 );
		set => EditorCookie.Set( "Copilot.MaxChatHistory", value );
	}

	// ── model selection ───────────────────────────────────────────────────────

	/// <summary>
	/// Catalog of models exposed through the GitHub Copilot endpoint.
	/// Claude leads the list — the agent loop is tuned for Claude's tool-calling.
	/// </summary>
	public static readonly (string Id, string DisplayName, string Family)[] AvailableModels =
	{
		( "claude-opus-4.7",   "Claude Opus 4.7",   "Anthropic" ),
		( "claude-opus-4",     "Claude Opus 4",     "Anthropic" ),
		( "claude-sonnet-4.5", "Claude Sonnet 4.5", "Anthropic" ),
		( "claude-sonnet-4",   "Claude Sonnet 4",   "Anthropic" ),
		( "claude-3.7-sonnet", "Claude 3.7 Sonnet", "Anthropic" ),
		( "claude-3.5-sonnet", "Claude 3.5 Sonnet", "Anthropic" ),
		( "gpt-5",             "GPT-5",             "OpenAI"    ),
		( "gpt-4.1",           "GPT-4.1",           "OpenAI"    ),
		( "gpt-4o",            "GPT-4o",            "OpenAI"    ),
		( "o4-mini",           "o4-mini (fast)",    "OpenAI"    ),
		( "gemini-2.5-pro",    "Gemini 2.5 Pro",    "Google"    ),
	};

	public const string DefaultModel = "claude-opus-4.7";

	public static string SelectedModel
	{
		get => EditorCookie.GetString( "Copilot.SelectedModel", DefaultModel );
		set => EditorCookie.SetString( "Copilot.SelectedModel", value );
	}

	public static string SelectedModelDisplayName
	{
		get
		{
			foreach ( var m in AvailableModels )
				if ( m.Id == SelectedModel ) return m.DisplayName;
			return SelectedModel;
		}
	}

	// ── agent / tool approval mode ────────────────────────────────────────────

	public enum ToolApproval
	{
		/// <summary>Read-only tools auto-run; mutating tools require click-to-confirm.</summary>
		Default,
		/// <summary>All tools auto-run, including destructive ones. YOLO mode.</summary>
		AutoApproveAll,
		/// <summary>Every tool call requires confirmation, even read-only ones.</summary>
		AskAlways,
		/// <summary>Read-only tools auto-run, mutating tools are blocked entirely.</summary>
		ReadOnly,
	}

	public static ToolApproval ApprovalMode
	{
		get => (ToolApproval)EditorCookie.Get( "Copilot.ApprovalMode", (int)ToolApproval.Default );
		set => EditorCookie.Set( "Copilot.ApprovalMode", (int)value );
	}

	public static string ApprovalDisplayName => ApprovalMode switch
	{
		ToolApproval.Default        => "Default Approvals",
		ToolApproval.AutoApproveAll => "Auto-Approve All",
		ToolApproval.AskAlways      => "Ask Always",
		ToolApproval.ReadOnly       => "Read-Only",
		_                           => "Default",
	};

	/// <summary>
	/// Advertise agent tools to the model. Disable to revert to pure-chat.
	/// </summary>
	public static bool AgentEnabled
	{
		get => EditorCookie.Get( "Copilot.AgentEnabled", true );
		set => EditorCookie.Set( "Copilot.AgentEnabled", value );
	}

	// ── helpers ───────────────────────────────────────────────────────────────

	public static void ClearAuth()
	{
		GitHubAccessToken = "";
		GitHubUsername    = "";
	}
}
