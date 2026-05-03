using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Editor.Copilot;

/// <summary>
/// AI Copilot chat panel — game-development focused, with full agent capability.
/// Mirrors VS Code Copilot Chat: streaming markdown, code blocks with copy/insert,
/// per-message actions, multi-line input, slash commands, file/scene context refs,
/// model picker (Claude Opus 4.7 default), approval modes, and live tool calls
/// against the running editor.
/// </summary>
[Dock( "Editor", "AI Copilot", "smart_toy" )]
internal class CopilotChatWidget : Widget
{
	// ── conversation state ────────────────────────────────────────────────────

	private readonly List<CopilotService.ChatMessage> _history = new();

	// ── UI references ─────────────────────────────────────────────────────────

	private Widget       _headerBar;
	private Label        _userLabel;
	private Button       _signButton;

	private Widget       _authPanel;
	private Widget       _chatPanel;

	private ScrollArea   _scrollArea;
	private Widget       _messagesCanvas;
	private Layout       _messagesLayout;

	private ChatInputBox _inputField;
	private Button       _sendButton;
	private Button       _stopButton;
	private Button       _slashButton;
	private Button       _modelButton;
	private Button       _approvalButton;
	private Button       _agentToggleButton;

	// streaming state
	private MessageBubble           _streamingBubble;
	private CancellationTokenSource _streamCts;
	private bool                    _isStreaming;

	private bool                    _authInProgress;
	private CancellationTokenSource _authCts;

	// ── constructor ───────────────────────────────────────────────────────────

	public CopilotChatWidget( Widget parent ) : base( parent )
	{
		DeleteOnClose = true;
		WindowTitle   = "AI Copilot";
		SetWindowIcon( "smart_toy" );
		Name          = "AI Copilot";
		MinimumSize   = new( 360, 280 );
		Size          = new( 480, 760 );

		Layout = Layout.Column();
		Layout.Margin  = 0;
		Layout.Spacing = 0;

		BuildHeader();
		Layout.AddSeparator();

		_authPanel = BuildAuthPanel();
		Layout.Add( _authPanel, 1 );

		_chatPanel = BuildChatPanel();
		Layout.Add( _chatPanel, 1 );

		RefreshAuthState();
	}

	// ── header ────────────────────────────────────────────────────────────────

	private void BuildHeader()
	{
		_headerBar        = new Widget( this );
		_headerBar.Layout = Layout.Row();
		_headerBar.Layout.Margin  = new Margin( 8, 6, 8, 6 );
		_headerBar.Layout.Spacing = 6;
		_headerBar.FixedHeight    = 36;

		var title = new Label( "🤖 AI Copilot", _headerBar );
		title.SetStyles( "font-weight: bold; font-size: 13px;" );
		_headerBar.Layout.Add( title );

		_headerBar.Layout.AddStretchCell();

		_userLabel = new Label( "", _headerBar );
		_userLabel.SetStyles( "color: rgba(255,255,255,0.5); font-size: 11px;" );
		_headerBar.Layout.Add( _userLabel );

		var newChat = new Button( "", _headerBar )
		{
			Icon    = "add_comment",
			ToolTip = "New chat (clears history)",
			Clicked = ClearHistory
		};
		newChat.FixedHeight = 24;
		_headerBar.Layout.Add( newChat );

		_signButton = new Button( "Sign in", _headerBar ) { Clicked = OnSignButtonClicked };
		_signButton.FixedHeight = 24;
		_headerBar.Layout.Add( _signButton );

		Layout.Add( _headerBar );
	}

	// ── auth panel ────────────────────────────────────────────────────────────

	private Widget BuildAuthPanel()
	{
		var panel    = new Widget( this );
		panel.Layout = Layout.Column();
		panel.Layout.Margin  = new Margin( 24, 16, 24, 16 );
		panel.Layout.Spacing = 12;

		var title = new Label( "Sign in to GitHub to enable AI Copilot", panel );
		title.WordWrap = true;
		title.SetStyles( "font-size: 14px; font-weight: bold;" );
		panel.Layout.Add( title );

		var desc = new Label(
			"AI Copilot uses your GitHub Copilot subscription (Pro/Business/Enterprise) to call " +
			"Claude, GPT and Gemini models. Once signed in you can chat, run slash commands, and " +
			"let the agent directly inspect and modify the running editor.",
			panel );
		desc.WordWrap = true;
		desc.SetStyles( "color: rgba(255,255,255,0.6); font-size: 12px;" );
		panel.Layout.Add( desc );

		panel.Layout.AddSeparator();

		var clientLabel = new Label( "GitHub OAuth App Client ID:", panel );
		clientLabel.SetStyles( "font-size: 11px;" );
		panel.Layout.Add( clientLabel );

		var clientEdit = new LineEdit( panel )
		{
			Text            = CopilotPreferences.OAuthClientId,
			PlaceholderText = "Iv1.xxxxxxxxxxxxxxxx"
		};
		clientEdit.TextEdited += v => CopilotPreferences.OAuthClientId = v;
		panel.Layout.Add( clientEdit );

		var hint = new Label(
			"Create one at github.com/settings/applications/new   |   Scope: copilot",
			panel );
		hint.WordWrap = true;
		hint.SetStyles( "color: rgba(255,255,255,0.4); font-size: 10px;" );
		panel.Layout.Add( hint );

		panel.Layout.AddStretchCell();

		var startBtn = new Button( "🔑  Sign in with GitHub", panel ) { Clicked = OnSignButtonClicked };
		startBtn.SetStyles( "padding: 8px; font-size: 13px;" );
		panel.Layout.Add( startBtn );

		return panel;
	}

	// ── chat panel ────────────────────────────────────────────────────────────

	private Widget BuildChatPanel()
	{
		var panel    = new Widget( this );
		panel.Layout = Layout.Column();
		panel.Layout.Margin  = 0;
		panel.Layout.Spacing = 0;

		// scroll area
		_scrollArea = new ScrollArea( panel );
		_scrollArea.HorizontalScrollbar.Hidden = true;

		_messagesCanvas         = new Widget( _scrollArea );
		_messagesLayout         = Layout.Column();
		_messagesLayout.Margin  = new Margin( 8, 8, 8, 8 );
		_messagesLayout.Spacing = 8;
		_messagesCanvas.Layout  = _messagesLayout;
		_messagesLayout.AddStretchCell();
		_scrollArea.Canvas      = _messagesCanvas;

		panel.Layout.Add( _scrollArea, 1 );

		AddWelcomeBubble();

		panel.Layout.AddSeparator();

		// input area
		var inputContainer = new Widget( panel );
		inputContainer.Layout         = Layout.Column();
		inputContainer.Layout.Margin  = new Margin( 8, 6, 8, 8 );
		inputContainer.Layout.Spacing = 4;

		_inputField = new ChatInputBox( inputContainer )
		{
			OnSubmit = SubmitMessage
		};
		inputContainer.Layout.Add( _inputField );

		// bottom row 1: model + approvals + agent toggle
		var statusRow = inputContainer.Layout.AddRow();
		statusRow.Spacing = 4;

		_modelButton = new Button( CopilotPreferences.SelectedModelDisplayName + "  ▾", inputContainer )
		{
			ToolTip = "Choose AI model",
			Clicked = ShowModelMenu
		};
		_modelButton.FixedHeight = 22;
		_modelButton.SetStyles( "padding: 0 8px; font-size: 11px;" );
		statusRow.Add( _modelButton );

		_approvalButton = new Button( CopilotPreferences.ApprovalDisplayName + "  ▾", inputContainer )
		{
			ToolTip = "How tool calls are gated",
			Clicked = ShowApprovalMenu
		};
		_approvalButton.FixedHeight = 22;
		_approvalButton.SetStyles( "padding: 0 8px; font-size: 11px;" );
		statusRow.Add( _approvalButton );

		_agentToggleButton = new Button( AgentToggleLabel(), inputContainer )
		{
			ToolTip = "Enable/disable engine tool-calling",
			Clicked = ToggleAgent
		};
		_agentToggleButton.FixedHeight = 22;
		_agentToggleButton.SetStyles( "padding: 0 8px; font-size: 11px;" );
		statusRow.Add( _agentToggleButton );

		statusRow.AddStretchCell();

		// bottom row 2: commands + send/stop
		var actionRow = inputContainer.Layout.AddRow();
		actionRow.Spacing = 4;

		_slashButton = new Button( "⚡ Commands", inputContainer )
		{
			ToolTip = "Slash commands & context references",
			Clicked = ShowSlashMenu
		};
		_slashButton.FixedHeight = 22;
		actionRow.Add( _slashButton );

		actionRow.AddStretchCell();

		_stopButton = new Button( "⏹ Stop", inputContainer )
		{
			ToolTip = "Stop generating",
			Clicked = StopStreaming
		};
		_stopButton.FixedHeight = 22;
		_stopButton.Hidden      = true;
		actionRow.Add( _stopButton );

		_sendButton = new Button( "Send  ➤", inputContainer )
		{
			ToolTip = "Send message (Enter)",
			Clicked = () => SubmitMessage( _inputField.PlainText )
		};
		_sendButton.FixedHeight = 22;
		_sendButton.SetStyles( "background: #1f6feb; color: white; padding: 0 12px; border-radius: 3px;" );
		actionRow.Add( _sendButton );

		panel.Layout.Add( inputContainer );

		return panel;
	}

	private void AddWelcomeBubble()
	{
		var welcome = new MessageBubble( MessageBubble.Sender.System, _messagesCanvas );
		welcome.SetMarkdown(
			"### Welcome to s&box AI Copilot 👋\n\n" +
			"I can **inspect your scene, move objects, add components, edit files, enter play mode**, and more — " +
			"powered by **" + CopilotPreferences.SelectedModelDisplayName + "**.\n\n" +
			"**Try asking:**\n" +
			"- *what's in my scene?*\n" +
			"- *create a red cube at 0,0,100*\n" +
			"- *add a Rigidbody to the player*\n" +
			"- *explain* `#file:code/Player.cs`\n\n" +
			"**Tips:** type `/` for slash commands  •  `#file:path`, `#selection`, `#scene` for context  •  Shift+Enter for newline." );
		InsertBubble( welcome );
	}

	// ── auth state ────────────────────────────────────────────────────────────

	private void RefreshAuthState()
	{
		var signedIn = CopilotPreferences.IsSignedIn;

		_authPanel.Hidden = signedIn;
		_chatPanel.Hidden = !signedIn;

		if ( signedIn )
		{
			_userLabel.Text  = CopilotPreferences.GitHubUsername;
			_signButton.Text = "Sign out";
		}
		else
		{
			_userLabel.Text  = "";
			_signButton.Text = "Sign in";
		}
	}

	// ── sign in / sign out ────────────────────────────────────────────────────

	private async void OnSignButtonClicked()
	{
		if ( CopilotPreferences.IsSignedIn ) { SignOut(); return; }

		if ( _authInProgress )
		{
			_authCts?.Cancel();
			_authInProgress  = false;
			RefreshAuthState();
			return;
		}

		var clientId = CopilotPreferences.OAuthClientId;
		if ( string.IsNullOrWhiteSpace( clientId ) )
		{
			ShowNotification( "Please enter your GitHub OAuth App Client ID first.", isError: true );
			return;
		}

		DeviceCodeResult result;
		try { result = await GitHubOAuth.RequestDeviceCodeAsync( clientId ); }
		catch ( Exception ex )
		{
			ShowNotification( $"Could not start sign-in: {ex.Message}", isError: true );
			return;
		}

		ShowDeviceCodeDialog( result );

		_authInProgress  = true;
		_authCts         = new CancellationTokenSource();
		_signButton.Text = "Cancel";

		await GitHubOAuth.PollForTokenAsync(
			clientId,
			result.DeviceCode,
			result.Interval,
			onTokenReceived: async token =>
			{
				_authInProgress = false;
				CopilotPreferences.GitHubAccessToken = token;
				var username = await GitHubOAuth.FetchUsernameAsync( token );
				CopilotPreferences.GitHubUsername = username;
				MainThread.Queue( () =>
				{
					RefreshAuthState();
					ShowNotification( $"Signed in as {username} ✓" );
				} );
			},
			onError: msg =>
			{
				_authInProgress = false;
				MainThread.Queue( () =>
				{
					RefreshAuthState();
					ShowNotification( msg, isError: true );
				} );
			},
			cancellation: _authCts.Token );
	}

	private void SignOut()
	{
		_authCts?.Cancel();
		_streamCts?.Cancel();
		_authInProgress = false;
		_isStreaming    = false;
		CopilotService.InvalidateToken();
		CopilotPreferences.ClearAuth();
		_history.Clear();
		ClearMessageWidgets();
		RefreshAuthState();
	}

	private void ShowDeviceCodeDialog( DeviceCodeResult result )
	{
		var popup = new PopupDialogWidget( "🔑" );
		popup.FixedWidth        = 480;
		popup.WindowTitle       = "Sign in to GitHub";
		popup.MessageLabel.Text =
			$"<b>Step 1:</b> open <a href='{result.VerificationUri}'>{result.VerificationUri}</a><br><br>" +
			$"<b>Step 2:</b> enter this code:<br><br>" +
			$"<span style='font-size:24px; font-family:Consolas; letter-spacing:2px;'>{result.UserCode}</span><br><br>" +
			$"<i>Waiting for approval…</i>";

		popup.ButtonLayout.Add( new Button( "Open Browser" )
		{
			Clicked = () => System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo
			{
				FileName        = result.VerificationUri,
				UseShellExecute = true
			} )
		} );
		popup.ButtonLayout.Add( new Button( "Copy Code" )
		{
			Clicked = () => EditorUtility.Clipboard.Copy( result.UserCode )
		} );
		popup.ButtonLayout.Add( new Button( "OK" ) { Clicked = popup.Destroy } );
		popup.SetModal( false, false );
		popup.Show();
	}

	// ── model menu ────────────────────────────────────────────────────────────

	private void ShowModelMenu()
	{
		var menu = new Menu( _modelButton );
		string lastFamily = null;
		foreach ( var m in CopilotPreferences.AvailableModels )
		{
			if ( m.Family != lastFamily )
			{
				if ( lastFamily != null ) menu.AddSeparator();
				lastFamily = m.Family;
			}

			var captured = m;
			var prefix   = captured.Id == CopilotPreferences.SelectedModel ? "● " : "  ";
			menu.AddOption( prefix + captured.DisplayName + "    (" + captured.Family + ")", null, () =>
			{
				CopilotPreferences.SelectedModel = captured.Id;
				_modelButton.Text = captured.DisplayName + "  ▾";
				ShowNotification( $"Model: {captured.DisplayName}" );
			} );
		}
		menu.OpenAtCursor( false );
	}

	private void ShowApprovalMenu()
	{
		var menu = new Menu( _approvalButton );

		void Add( CopilotPreferences.ToolApproval mode, string label, string desc )
		{
			var prefix = mode == CopilotPreferences.ApprovalMode ? "● " : "  ";
			menu.AddOption( prefix + label + "    —    " + desc, null, () =>
			{
				CopilotPreferences.ApprovalMode = mode;
				_approvalButton.Text = CopilotPreferences.ApprovalDisplayName + "  ▾";
				ShowNotification( "Approval mode: " + label );
			} );
		}

		Add( CopilotPreferences.ToolApproval.Default,        "Default Approvals", "read-only auto, mutating asks" );
		Add( CopilotPreferences.ToolApproval.AutoApproveAll, "Auto-Approve All",  "YOLO — agent runs everything" );
		Add( CopilotPreferences.ToolApproval.AskAlways,      "Ask Always",        "every tool waits for approval" );
		Add( CopilotPreferences.ToolApproval.ReadOnly,       "Read-Only",         "block all mutating tools" );
		menu.OpenAtCursor( false );
	}

	private void ToggleAgent()
	{
		CopilotPreferences.AgentEnabled = !CopilotPreferences.AgentEnabled;
		_agentToggleButton.Text = AgentToggleLabel();
		ShowNotification( CopilotPreferences.AgentEnabled ? "Engine tools enabled" : "Engine tools disabled (chat-only)" );
	}

	private static string AgentToggleLabel()
		=> CopilotPreferences.AgentEnabled ? "🤖 Agent: ON" : "💬 Agent: OFF";

	// ── slash menu ────────────────────────────────────────────────────────────

	private void ShowSlashMenu()
	{
		var menu = new Menu( _slashButton );

		foreach ( var cmd in GameDevPrompts.Commands )
		{
			var captured = cmd;
			menu.AddOption( $"{captured.Name}    —    {captured.Description}", null, () =>
			{
				var existing = _inputField.PlainText ?? "";
				_inputField.PlainText = existing.TrimEnd() + " " + captured.Name + " ";
				_inputField.Focus();
			} );
		}

		menu.AddSeparator();
		menu.AddOption( "#file:<path>     — include a project file as context",     null, () => InsertAtCursor( "#file:" ) );
		menu.AddOption( "#selection       — include the current scene selection",    null, () => InsertAtCursor( "#selection " ) );
		menu.AddOption( "#scene           — include a summary of the current scene", null, () => InsertAtCursor( "#scene " ) );

		menu.OpenAtCursor( false );
	}

	private void InsertAtCursor( string text )
	{
		var existing = _inputField.PlainText ?? "";
		_inputField.PlainText = existing.TrimEnd() + " " + text;
		_inputField.Focus();
	}

	// ── messaging ─────────────────────────────────────────────────────────────

	private void SubmitMessage( string raw )
	{
		if ( _isStreaming ) return;
		if ( string.IsNullOrWhiteSpace( raw ) ) return;

		_inputField.PlainText = "";

		var parsed = GameDevPrompts.BuildPrompt( raw );

		// User bubble (display text)
		var userBubble = new MessageBubble( MessageBubble.Sender.User, _messagesCanvas );
		userBubble.SetPlainText( parsed.DisplayText );
		InsertBubble( userBubble );

		if ( parsed.ContextRefs.Count > 0 )
		{
			var chipsBubble = new MessageBubble( MessageBubble.Sender.System, _messagesCanvas );
			chipsBubble.SetMarkdown( "**Context:** " + string.Join( "  ", parsed.ContextRefs ) );
			InsertBubble( chipsBubble );
		}

		_streamingBubble = new MessageBubble( MessageBubble.Sender.Assistant, _messagesCanvas, RegenerateLast );
		_streamingBubble.SetMarkdown( "▍" );
		InsertBubble( _streamingBubble );

		BeginAgentTurn( parsed.ExpandedPrompt );
	}

	private void RegenerateLast( MessageBubble bubble )
	{
		if ( _isStreaming ) return;
		if ( _history.Count == 0 ) return;

		// Drop trailing assistant + tool messages until we get back to a user turn
		while ( _history.Count > 0 && _history[^1].Role != "user" )
			_history.RemoveAt( _history.Count - 1 );
		if ( _history.Count == 0 ) return;

		var lastUser = _history[^1];
		_history.RemoveAt( _history.Count - 1 );

		_streamingBubble = bubble;
		bubble.SetMarkdown( "▍" );
		BeginAgentTurn( lastUser.Content );
	}

	private void BeginAgentTurn( string expandedPrompt )
	{
		_isStreaming        = true;
		_sendButton.Enabled = false;
		_inputField.Enabled = false;
		_stopButton.Hidden  = false;
		_streamCts          = new CancellationTokenSource();

		var assistantText = new System.Text.StringBuilder();
		var turnStartCount = _history.Count; // any tool calls add to history; we render bubbles separately

		var callbacks = new CopilotService.StreamCallbacks
		{
			OnTextDelta = delta =>
			{
				if ( delta == null ) return;
				assistantText.Append( delta );
				if ( _streamingBubble != null && _streamingBubble.IsValid )
					_streamingBubble.SetMarkdown( assistantText.ToString() + " ▍" );
				ScrollToBottom();
			},

			OnToolCalls = HandleToolCalls,

			OnComplete = () =>
			{
				FinishStream();
				if ( _streamingBubble != null && _streamingBubble.IsValid )
					_streamingBubble.SetMarkdown( assistantText.ToString().TrimEnd() );
				while ( _history.Count > CopilotPreferences.MaxChatHistory )
					_history.RemoveAt( 0 );
				_streamingBubble = null;
			},

			OnError = msg =>
			{
				FinishStream();
				if ( _streamingBubble != null && _streamingBubble.IsValid )
					_streamingBubble.SetMarkdown( $"⚠ **Error:** {msg}" );
				_streamingBubble = null;
			},
		};

		_ = CopilotService.RunAgentAsync( _history, expandedPrompt, callbacks, _streamCts.Token );
	}

	// ── tool-call handler ─────────────────────────────────────────────────────

	private async Task<List<string>> HandleToolCalls( List<CopilotService.ToolCall> calls )
	{
		var results = new List<string>( calls.Count );

		foreach ( var call in calls )
		{
			var name = call.Function?.Name ?? "unknown";
			var tool = AgentTools.Find( name );
			var args = call.Function?.Arguments ?? "{}";

			if ( tool == null )
			{
				var bubble = new ToolBubble( null, name, args, requiresApproval: false, _messagesCanvas );
				InsertWidget( bubble );
				bubble.SetResult( "{\"ok\":false,\"error\":\"unknown tool\"}", ok: false );
				results.Add( "{\"ok\":false,\"error\":\"unknown tool '" + name + "'\"}" );
				continue;
			}

			// Approval logic
			var mode             = CopilotPreferences.ApprovalMode;
			bool isReadOnly      = tool.Safety == AgentTools.ToolSafety.ReadOnly;
			bool needsApproval   = mode switch
			{
				CopilotPreferences.ToolApproval.AutoApproveAll => false,
				CopilotPreferences.ToolApproval.AskAlways      => true,
				CopilotPreferences.ToolApproval.ReadOnly       => !isReadOnly, // mutating: auto-block via reject below
				CopilotPreferences.ToolApproval.Default        => !isReadOnly,
				_                                              => !isReadOnly,
			};

			// Read-only mode hard-blocks mutating tools
			if ( mode == CopilotPreferences.ToolApproval.ReadOnly && !isReadOnly )
			{
				var bubble = new ToolBubble( tool, name, args, requiresApproval: false, _messagesCanvas );
				InsertWidget( bubble );
				bubble.SetResult( "{\"ok\":false,\"error\":\"read-only mode blocked mutating tool\"}", ok: false );
				results.Add( "{\"ok\":false,\"error\":\"blocked by Read-Only approval mode\"}" );
				continue;
			}

			var ui = new ToolBubble( tool, name, args, needsApproval, _messagesCanvas );
			InsertWidget( ui );

			bool approved = await ui.ApprovalTask;
			if ( !approved )
			{
				results.Add( "{\"ok\":false,\"error\":\"user declined\"}" );
				continue;
			}

			// Parse arguments and run handler
			JsonElement parsedArgs;
			try
			{
				using var doc = JsonDocument.Parse( string.IsNullOrEmpty( args ) ? "{}" : args );
				parsedArgs    = doc.RootElement.Clone();
			}
			catch ( Exception ex )
			{
				ui.SetResult( "{\"ok\":false,\"error\":\"bad arguments json\"}", ok: false );
				results.Add( "{\"ok\":false,\"error\":\"argument JSON parse error: " + ex.Message + "\"}" );
				continue;
			}

			string resultJson;
			try   { resultJson = AgentTools.ExecuteSync( tool, parsedArgs ); }
			catch ( Exception ex ) { resultJson = "{\"ok\":false,\"error\":\"" + ex.Message.Replace( "\"", "\\\"" ) + "\"}"; }

			bool ok = !resultJson.Contains( "\"ok\":false" );
			ui.SetResult( resultJson, ok );
			results.Add( resultJson );
		}

		return results;
	}

	// ── streaming control ─────────────────────────────────────────────────────

	private void StopStreaming() => _streamCts?.Cancel();

	private void FinishStream()
	{
		_isStreaming        = false;
		_sendButton.Enabled = true;
		_inputField.Enabled = true;
		_stopButton.Hidden  = true;
		_inputField.Focus();
	}

	private void ClearHistory()
	{
		_streamCts?.Cancel();
		_isStreaming        = false;
		_sendButton.Enabled = true;
		_inputField.Enabled = true;
		_stopButton.Hidden  = true;
		_streamingBubble    = null;
		_history.Clear();
		ClearMessageWidgets();
		AddWelcomeBubble();
	}

	private void ClearMessageWidgets()
	{
		_messagesLayout?.Clear( true );
		_messagesLayout?.AddStretchCell();
	}

	// ── helpers ───────────────────────────────────────────────────────────────

	private void InsertBubble( MessageBubble bubble ) => InsertWidget( bubble );

	private void InsertWidget( Widget w )
	{
		_messagesLayout.Clear( false );
		_messagesLayout.Add( w );
		_messagesLayout.AddStretchCell();
		ScrollToBottom();
	}

	private void ScrollToBottom()
	{
		var sb = _scrollArea?.VerticalScrollbar;
		if ( sb != null ) sb.Value = sb.Maximum;
	}

	private void ShowNotification( string message, bool isError = false )
	{
		var banner = new Label( message, this );
		banner.WordWrap = true;
		banner.SetStyles( isError
			? "background:#5a1a1a; color:#fb5a5a; padding:6px; font-size:11px;"
			: "background:#1a3a1a; color:#3fb950; padding:6px; font-size:11px;" );
		Layout.Add( banner );

		_ = Task.Delay( 5000 ).ContinueWith( _ =>
			MainThread.Queue( () => { if ( banner.IsValid ) banner.Destroy(); } ) );
	}
}
