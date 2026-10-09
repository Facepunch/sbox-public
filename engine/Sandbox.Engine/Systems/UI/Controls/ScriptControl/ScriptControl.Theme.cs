using Microsoft.AspNetCore.Components;
using System;

namespace Sandbox.UI;

public partial class ScriptControl
{
	ThemeDefinition _theme = ThemeDefinition.Default;
	StyleSheet _themeStyleSheet;

	/// <summary>
	/// Colors used by this editor. Assigning a new theme updates existing text and popups
	/// without changing the document, selection or undo history.
	/// </summary>
	[Parameter]
	public ThemeDefinition Theme
	{
		get => _theme;
		set
		{
			ArgumentNullException.ThrowIfNull( value );
			if ( _theme == value )
				return;

			_theme = value;
			ApplyTheme();
		}
	}

	void ApplyTheme()
	{
		// Keep state selectors in the style system, including completion hover and error status.
		var colors = $$"""
		.script-control
		{
			background-color: {{Theme.Background.Rgba}};
			color: {{Theme.Foreground.Rgba}};
			.line-gutter { background-color: {{Theme.GutterBackground.Rgba}}; }
			.script-entry { caret-color: {{Theme.Caret.Rgba}}; }
			.editor-status
			{
				background-color: {{Theme.StatusBackground.Rgba}};
				color: {{Theme.StatusForeground.Rgba}};
				&.has-error { background-color: {{Theme.StatusErrorBackground.Rgba}}; }
			}
		}
		.script-control-popup
		{
			&.intellisense
			{
				color: {{Theme.Foreground.Rgba}};
				background-color: {{Theme.PopupBackground.Rgba}};
				border-color: {{Theme.PopupBorder.Rgba}};
				box-shadow: 0 3px 12px {{Theme.PopupShadow.Rgba}};
			}
			.completion-row
			{
				&.selected
				{
					background-color: {{Theme.CompletionSelectedBackground.Rgba}};
					color: {{Theme.CompletionSelectedForeground.Rgba}};
				}
				&:hover { background-color: {{Theme.CompletionHoverBackground.Rgba}}; }
			}
			.symbol-icon
			{
				color: {{Theme.SymbolIcon.Rgba}};
				&.type { color: {{Theme.Type.Rgba}}; }
				&.keyword { color: {{Theme.Keyword.Rgba}}; }
			}
			.completion-footer
			{
				color: {{Theme.MutedForeground.Rgba}};
				border-top-color: {{Theme.PopupBorder.Rgba}};
			}
			.info-title { color: {{Theme.PopupTitle.Rgba}}; }
			.info-description { color: {{Theme.PopupDescription.Rgba}}; }
			.signature-counter { color: {{Theme.MutedForeground.Rgba}}; }
			.signature-text { color: {{Theme.SignatureForeground.Rgba}}; }
		}
		""";

		var previous = _themeStyleSheet;
		StyleSheet.Remove( previous );
		_themeStyleSheet = Sandbox.UI.StyleSheet.FromString( colors, "script-editor-theme" );
		StyleSheet.Add( _themeStyleSheet );
		UpdatePopupTheme( _completionPopup, previous );
		UpdatePopupTheme( _completionDetail, previous );
		UpdatePopupTheme( _signaturePopup, previous );
		UpdatePopupTheme( _hoverPopup, previous );
		Entry.RebuildColors( resetStyles: true );
		if ( SignatureVisible )
		{
			RenderSignature();
		}
	}
}
