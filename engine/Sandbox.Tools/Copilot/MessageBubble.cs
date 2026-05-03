using System;
using System.Collections.Generic;

namespace Editor.Copilot;

/// <summary>
/// One conversation turn — user or assistant — rendered like a VS Code
/// Copilot chat bubble.
///
/// Layout:
///  ┌─────────────────────────────────────────────────────────────┐
///  │ 🤖 Copilot                              [Copy] [Regenerate] │   ← header
///  ├─────────────────────────────────────────────────────────────┤
///  │ here is some prose…                                          │
///  │ ┌────────────────────────────────────────────────┐          │
///  │ │ csharp                          [Copy][Insert] │          │
///  │ │   public void Foo() {}                         │          │
///  │ └────────────────────────────────────────────────┘          │
///  │ more prose…                                                 │
///  └─────────────────────────────────────────────────────────────┘
///
/// Calling <see cref="SetMarkdown"/> repeatedly during streaming will
/// efficiently re-use existing prose / code segments and only create new
/// widgets when the document grows.
/// </summary>
public class MessageBubble : Widget
{
	public enum Sender { User, Assistant, Error, System }

	private readonly Sender                 _sender;
	private readonly Layout                 _bodyLayout;
	private readonly Action<MessageBubble>  _onRegenerate;

	private string _rawMarkdown = "";
	private readonly List<Widget> _segmentWidgets = new();
	private readonly List<MarkdownParser.Segment> _lastSegments = new();

	public string RawText => _rawMarkdown;

	public MessageBubble( Sender sender, Widget parent, Action<MessageBubble> onRegenerate = null ) : base( parent )
	{
		_sender       = sender;
		_onRegenerate = onRegenerate;

		Layout = Layout.Column();
		Layout.Margin  = new Sandbox.UI.Margin( 8, 6, 8, 6 );
		Layout.Spacing = 4;

		// Background tint based on sender
		var bg = sender switch
		{
			Sender.User      => "rgba(88,166,255,0.06)",
			Sender.Assistant => "rgba(255,255,255,0.03)",
			Sender.Error     => "rgba(251,90,90,0.10)",
			Sender.System    => "rgba(255,255,255,0.05)",
			_                => "transparent"
		};
		SetStyles( $"background: {bg}; border-radius: 6px;" );

		// ── header ────────────────────────────────────────────────────────────
		var header = Layout.AddRow();
		header.Spacing = 6;

		var (icon, name, color) = sender switch
		{
			Sender.User      => ("👤", "You",         "#58a6ff"),
			Sender.Assistant => ("🤖", "Copilot",      "#3fb950"),
			Sender.Error     => ("⚠",  "Error",        "#fb5a5a"),
			Sender.System    => ("ℹ",  "System",       "#cccdcd"),
			_                => ("",   "",             "#cccdcd")
		};

		var senderLabel = new Label( $"{icon} {name}", this );
		senderLabel.SetStyles( $"font-weight: bold; color: {color}; font-size: 11px;" );
		header.Add( senderLabel );

		header.AddStretchCell();

		if ( sender == Sender.Assistant )
		{
			var copyBtn = new Button( "", this )
			{
				Icon    = "content_copy",
				ToolTip = "Copy entire response",
				Clicked = () => EditorUtility.Clipboard.Copy( _rawMarkdown )
			};
			copyBtn.FixedHeight = 20;
			header.Add( copyBtn );

			if ( onRegenerate != null )
			{
				var regenBtn = new Button( "", this )
				{
					Icon    = "refresh",
					ToolTip = "Regenerate response",
					Clicked = () => _onRegenerate?.Invoke( this )
				};
				regenBtn.FixedHeight = 20;
				header.Add( regenBtn );
			}
		}

		// ── body ──────────────────────────────────────────────────────────────
		var bodyContainer = new Widget( this );
		bodyContainer.Layout = Layout.Column();
		bodyContainer.Layout.Margin  = 0;
		bodyContainer.Layout.Spacing = 6;
		_bodyLayout = bodyContainer.Layout;
		Layout.Add( bodyContainer );
	}

	/// <summary>
	/// Set the bubble's markdown content. Safe to call many times during streaming.
	/// </summary>
	public void SetMarkdown( string markdown )
	{
		_rawMarkdown = markdown ?? "";

		var newSegments = MarkdownParser.Parse( _rawMarkdown );

		// If segment count or kinds changed, fully rebuild — otherwise just update text
		bool needsRebuild = newSegments.Count != _lastSegments.Count;
		if ( !needsRebuild )
		{
			for ( int i = 0; i < newSegments.Count; i++ )
			{
				if ( newSegments[i].Kind != _lastSegments[i].Kind )
				{
					needsRebuild = true;
					break;
				}
			}
		}

		if ( needsRebuild )
		{
			RebuildSegmentWidgets( newSegments );
		}
		else
		{
			// Update text in-place — much smoother during streaming
			for ( int i = 0; i < newSegments.Count; i++ )
			{
				var seg    = newSegments[i];
				var widget = _segmentWidgets[i];

				if ( seg.Kind == MarkdownParser.SegmentKind.Prose && widget is Label label )
				{
					label.Text = MarkdownParser.ProseToHtml( seg.Text );
				}
				else if ( seg.Kind == MarkdownParser.SegmentKind.Code && widget is CodeBlockWidget codeBlock )
				{
					codeBlock.SetLanguage( seg.Language );
					codeBlock.SetCode( seg.Text );
				}
			}
		}

		_lastSegments.Clear();
		_lastSegments.AddRange( newSegments );
	}

	/// <summary>
	/// Set as a plain text bubble (used for user messages — no markdown rendering).
	/// </summary>
	public void SetPlainText( string text )
	{
		_rawMarkdown = text ?? "";

		// Single Label, HTML-escaped, line-break preserving
		_segmentWidgets.Clear();
		_lastSegments.Clear();
		_bodyLayout.Clear( true );

		var label = new Label( "", this );
		label.WordWrap       = true;
		label.TextSelectable = true;
		label.SetStyles( "font-size: 12px;" );
		label.Text = System.Net.WebUtility.HtmlEncode( _rawMarkdown ).Replace( "\n", "<br>" );
		_bodyLayout.Add( label );
		_segmentWidgets.Add( label );
	}

	private void RebuildSegmentWidgets( List<MarkdownParser.Segment> segments )
	{
		_segmentWidgets.Clear();
		_bodyLayout.Clear( true );

		foreach ( var seg in segments )
		{
			if ( seg.Kind == MarkdownParser.SegmentKind.Prose )
			{
				var label = new Label( "", this );
				label.WordWrap          = true;
				label.TextSelectable    = true;
				label.OpenExternalLinks = true;
				label.SetStyles( "font-size: 12px;" );
				label.Text = MarkdownParser.ProseToHtml( seg.Text );
				_bodyLayout.Add( label );
				_segmentWidgets.Add( label );
			}
			else
			{
				var code = new CodeBlockWidget( seg.Language, this );
				code.SetCode( seg.Text );
				_bodyLayout.Add( code );
				_segmentWidgets.Add( code );
			}
		}
	}
}
