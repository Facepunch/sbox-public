namespace Sandbox.UI;

public partial class ScriptControl
{
	/// <summary>
	/// Immutable colors for a ScriptControl. Create a theme or copy one with a with expression,
	/// then assign it to ScriptControl.Theme. Unspecified colors use the default dark palette.
	/// </summary>
	public sealed record ThemeDefinition
	{
		/// <summary>
		/// The shared default dark palette.
		/// </summary>
		public static ThemeDefinition Default { get; } = new();

		/// <summary>
		/// Default code text and punctuation.
		/// </summary>
		public Color Foreground { get; init; } = Color.Parse( "#d4d4d4" ).Value;

		/// <summary>
		/// Language keywords.
		/// </summary>
		public Color Keyword { get; init; } = Color.Parse( "#569cd6" ).Value;

		/// <summary>
		/// Numeric literals.
		/// </summary>
		public Color Number { get; init; } = Color.Parse( "#b5cea8" ).Value;

		/// <summary>
		/// String and character literals.
		/// </summary>
		public Color String { get; init; } = Color.Parse( "#ce9178" ).Value;

		/// <summary>
		/// Comments.
		/// </summary>
		public Color Comment { get; init; } = Color.Parse( "#6a9955" ).Value;

		/// <summary>
		/// Identifiers without a more specific symbol kind.
		/// </summary>
		public Color Identifier { get; init; } = Color.Parse( "#9cdcfe" ).Value;

		/// <summary>
		/// Type names and type completion icons.
		/// </summary>
		public Color Type { get; init; } = Color.Parse( "#4ec9b0" ).Value;

		/// <summary>
		/// Member names.
		/// </summary>
		public Color Member { get; init; } = Color.Parse( "#dcdcaa" ).Value;

		/// <summary>
		/// Control-flow keywords.
		/// </summary>
		public Color ControlFlow { get; init; } = Color.Parse( "#c586c0" ).Value;

		/// <summary>
		/// Text selection background.
		/// </summary>
		public Color Selection { get; init; } = Color.Parse( "#264f78" ).Value;

		/// <summary>
		/// Current line background.
		/// </summary>
		public Color CurrentLine { get; init; } = Color.Parse( "#282828" ).Value;

		/// <summary>
		/// Error squiggles.
		/// </summary>
		public Color Error { get; init; } = Color.Parse( "#f14c4c" ).Value;

		/// <summary>
		/// Warning squiggles.
		/// </summary>
		public Color Warning { get; init; } = Color.Parse( "#cca700" ).Value;

		/// <summary>
		/// Line numbers outside the current line.
		/// </summary>
		public Color LineNumber { get; init; } = Color.Parse( "#858585" ).Value;

		/// <summary>
		/// The current line number.
		/// </summary>
		public Color ActiveLineNumber { get; init; } = Color.Parse( "#c6c6c6" ).Value;

		/// <summary>
		/// The active parameter in signature help.
		/// </summary>
		public Color ActiveParameter { get; init; } = Color.Parse( "#ffffff" ).Value;

		/// <summary>
		/// Editor background.
		/// </summary>
		public Color Background { get; init; } = Color.Parse( "#1e1e1e" ).Value;

		/// <summary>
		/// Line-number gutter background.
		/// </summary>
		public Color GutterBackground { get; init; } = Color.Parse( "#1e1e1e" ).Value;

		/// <summary>
		/// Text insertion caret.
		/// </summary>
		public Color Caret { get; init; } = Color.Parse( "#aeafad" ).Value;

		/// <summary>
		/// Status bar background when there is no error.
		/// </summary>
		public Color StatusBackground { get; init; } = Color.Parse( "#007acc" ).Value;

		/// <summary>
		/// Status bar text.
		/// </summary>
		public Color StatusForeground { get; init; } = Color.Parse( "#ffffff" ).Value;

		/// <summary>
		/// Status bar background when an error is shown.
		/// </summary>
		public Color StatusErrorBackground { get; init; } = Color.Parse( "#9c3535" ).Value;

		/// <summary>
		/// Completion, hover and signature popup backgrounds.
		/// </summary>
		public Color PopupBackground { get; init; } = Color.Parse( "#252526" ).Value;

		/// <summary>
		/// Popup borders and separators.
		/// </summary>
		public Color PopupBorder { get; init; } = Color.Parse( "#454545" ).Value;

		/// <summary>
		/// Popup drop shadow, including its opacity.
		/// </summary>
		public Color PopupShadow { get; init; } = Color.Parse( "#00000088" ).Value;

		/// <summary>
		/// Selected completion row background.
		/// </summary>
		public Color CompletionSelectedBackground { get; init; } = Color.Parse( "#094771" ).Value;

		/// <summary>
		/// Selected completion row text.
		/// </summary>
		public Color CompletionSelectedForeground { get; init; } = Color.Parse( "#ffffff" ).Value;

		/// <summary>
		/// Completion row background under the mouse.
		/// </summary>
		public Color CompletionHoverBackground { get; init; } = Color.Parse( "#37373d" ).Value;

		/// <summary>
		/// Completion icons other than types and keywords.
		/// </summary>
		public Color SymbolIcon { get; init; } = Color.Parse( "#b180d7" ).Value;

		/// <summary>
		/// Hover and completion detail titles.
		/// </summary>
		public Color PopupTitle { get; init; } = Color.Parse( "#4ec9b0" ).Value;

		/// <summary>
		/// Hover and completion descriptions.
		/// </summary>
		public Color PopupDescription { get; init; } = Color.Parse( "#d4d4d4" ).Value;

		/// <summary>
		/// Completion footer and signature counter text.
		/// </summary>
		public Color MutedForeground { get; init; } = Color.Parse( "#999999" ).Value;

		/// <summary>
		/// Signature text outside the active parameter.
		/// </summary>
		public Color SignatureForeground { get; init; } = Color.Parse( "#b0b0b0" ).Value;
	}
}
