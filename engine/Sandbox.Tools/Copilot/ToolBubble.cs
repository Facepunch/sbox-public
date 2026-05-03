using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Editor.Copilot;

/// <summary>
/// Visual representation of one agent tool invocation, mirroring VS Code's
/// "Searched for / Read file" collapsible chips.
///
/// Lifecycle:
///   1. Created with <c>Pending</c> state showing "Tool wants to run X".
///      For a mutating tool the user must click Approve / Reject.
///   2. On approval (or auto-approve) the bubble flips to <c>Running</c>.
///   3. When complete it shows <c>Success</c> or <c>Error</c> with the result
///      collapsed inside an expandable detail row.
/// </summary>
public class ToolBubble : Widget
{
	public enum State { Pending, Running, Success, Error, Rejected }

	private readonly TaskCompletionSource<bool> _approval = new();
	private readonly bool                       _requiresApproval;

	private Label  _statusLabel;
	private Label  _summaryLabel;
	private Widget _detailContainer;
	private Label  _detailText;
	private Button _expandButton;
	private Button _approveButton;
	private Button _rejectButton;

	private bool _expanded;

	public string ToolName  { get; }
	public string Arguments { get; }
	public AgentTools.Tool Tool { get; }

	/// <summary>Awaitable; resolves true if the user (or auto-approve) lets the call run.</summary>
	public Task<bool> ApprovalTask => _approval.Task;

	public ToolBubble( AgentTools.Tool tool, string toolName, string argumentsJson, bool requiresApproval, Widget parent )
		: base( parent )
	{
		Tool              = tool;
		ToolName          = toolName;
		Arguments         = argumentsJson;
		_requiresApproval = requiresApproval;

		Layout = Layout.Column();
		Layout.Margin  = new Margin( 8, 4, 8, 4 );
		Layout.Spacing = 4;
		SetStyles( "background: rgba(255,255,255,0.04); border-radius: 4px; border-left: 3px solid #888;" );

		// ── header row (icon + tool name + status + expand) ──────────────────
		var header = Layout.AddRow();
		header.Spacing = 6;

		var icon = SafetyIcon( tool );
		var iconLabel = new Label( icon, this );
		iconLabel.SetStyles( "font-size: 12px;" );
		header.Add( iconLabel );

		var nameLabel = new Label( toolName, this );
		nameLabel.SetStyles( "font-family: Consolas, monospace; font-size: 11px; color: #d4d4d4;" );
		header.Add( nameLabel );

		_statusLabel = new Label( "", this );
		_statusLabel.SetStyles( "font-size: 11px; color: rgba(255,255,255,0.6);" );
		header.Add( _statusLabel );

		header.AddStretchCell();

		_expandButton = new Button( "▸", this )
		{
			ToolTip = "Show details",
			Clicked = ToggleExpand
		};
		_expandButton.FixedHeight = 18;
		_expandButton.SetStyles( "min-width: 22px; padding: 0 4px;" );
		header.Add( _expandButton );

		// ── summary line ─────────────────────────────────────────────────────
		_summaryLabel = new Label( CompactSummary( argumentsJson ), this );
		_summaryLabel.WordWrap = true;
		_summaryLabel.SetStyles( "font-size: 10px; color: rgba(255,255,255,0.5); padding-left: 18px;" );
		Layout.Add( _summaryLabel );

		// ── approval row (only shown when needed) ────────────────────────────
		if ( requiresApproval )
		{
			var approvalRow = Layout.AddRow();
			approvalRow.Spacing = 4;

			_approveButton = new Button( "✓ Approve", this )
			{
				ToolTip = "Run this tool",
				Clicked = () =>
				{
					HideApprovalButtons();
					SetState( State.Running, "running…" );
					_approval.TrySetResult( true );
				}
			};
			_approveButton.SetStyles( "background:#1f6feb; color:white; padding:2px 10px; border-radius:3px;" );
			_approveButton.FixedHeight = 22;
			approvalRow.Add( _approveButton );

			_rejectButton = new Button( "✕ Skip", this )
			{
				ToolTip = "Decline this tool",
				Clicked = () =>
				{
					HideApprovalButtons();
					SetState( State.Rejected, "skipped by user" );
					_approval.TrySetResult( false );
				}
			};
			_rejectButton.FixedHeight = 22;
			approvalRow.Add( _rejectButton );

			approvalRow.AddStretchCell();
			SetState( State.Pending, "needs approval" );
		}
		else
		{
			SetState( State.Running, "running…" );
			_approval.TrySetResult( true );
		}

		// ── detail container (hidden until expanded) ─────────────────────────
		_detailContainer = new Widget( this );
		_detailContainer.Layout = Layout.Column();
		_detailContainer.Layout.Margin = new Margin( 18, 4, 0, 0 );
		_detailContainer.Hidden = true;

		_detailText = new Label( "", _detailContainer );
		_detailText.WordWrap       = true;
		_detailText.TextSelectable = true;
		_detailText.SetStyles(
			"font-family: Consolas, monospace; font-size: 10px; " +
			"background: #1e1e1e; color: #cccccc; padding: 6px; border-radius: 3px;" );
		_detailContainer.Layout.Add( _detailText );

		Layout.Add( _detailContainer );
		UpdateDetailText();
	}

	// ── public state transitions ──────────────────────────────────────────────

	public void SetResult( string resultJson, bool ok )
	{
		SetState( ok ? State.Success : State.Error, ok ? "✓ done" : "✗ failed" );
		UpdateDetailText( resultJson );
	}

	public void SetRejected()
	{
		SetState( State.Rejected, "rejected" );
	}

	private void SetState( State state, string status )
	{
		_statusLabel.Text = status;

		var (border, statusColor) = state switch
		{
			State.Pending  => ( "#e3b341", "#e3b341" ),
			State.Running  => ( "#58a6ff", "#58a6ff" ),
			State.Success  => ( "#3fb950", "#3fb950" ),
			State.Error    => ( "#fb5a5a", "#fb5a5a" ),
			State.Rejected => ( "#888888", "#888888" ),
			_              => ( "#888888", "#cccccc" ),
		};
		SetStyles( $"background: rgba(255,255,255,0.04); border-radius: 4px; border-left: 3px solid {border};" );
		_statusLabel.SetStyles( $"font-size: 11px; color: {statusColor};" );
	}

	private void HideApprovalButtons()
	{
		if ( _approveButton != null ) _approveButton.Hidden = true;
		if ( _rejectButton  != null ) _rejectButton.Hidden  = true;
	}

	private void ToggleExpand()
	{
		_expanded = !_expanded;
		_detailContainer.Hidden = !_expanded;
		_expandButton.Text      = _expanded ? "▾" : "▸";
	}

	private void UpdateDetailText( string resultJson = null )
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine( "Arguments:" );
		sb.AppendLine( PrettyJson( Arguments ) );
		if ( resultJson != null )
		{
			sb.AppendLine();
			sb.AppendLine( "Result:" );
			sb.AppendLine( PrettyJson( resultJson ) );
		}
		_detailText.Text = sb.ToString();
	}

	// ── helpers ───────────────────────────────────────────────────────────────

	private static string SafetyIcon( AgentTools.Tool tool )
		=> tool == null ? "❓" : tool.Safety == AgentTools.ToolSafety.ReadOnly ? "🔍" : "⚙";

	private static string CompactSummary( string argumentsJson )
	{
		if ( string.IsNullOrEmpty( argumentsJson ) ) return "(no arguments)";
		var s = argumentsJson.Replace( "\n", " " ).Replace( "\r", " " );
		return s.Length > 140 ? s[..140] + "…" : s;
	}

	private static string PrettyJson( string raw )
	{
		if ( string.IsNullOrEmpty( raw ) ) return "(empty)";
		try
		{
			using var doc = JsonDocument.Parse( raw );
			return JsonSerializer.Serialize( doc.RootElement, new JsonSerializerOptions { WriteIndented = true } );
		}
		catch
		{
			return raw;
		}
	}
}
