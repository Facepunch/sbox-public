using System.Net;
using System.IO;
using System.Text.Json;
using Editor.ProjectSettingPages;
using static Editor.ProjectPublisher.PackageManifest;

namespace Editor;

/// <summary>The single place to edit include rules, including suggestions from warnings.</summary>
internal sealed class PublishResourceRulesWidget : PublishSection
{
	readonly WildcardPathWidget rules;
	readonly Widget listEditor;
	readonly RuleList list;
	readonly LineEdit input;
	readonly Button apply;
	readonly Button cancel;
	readonly Button textMode;
	readonly Button agent;
	readonly Project project;
	readonly Button includeMode;
	readonly Button excludeMode;
	bool excluding;
	readonly Button cleanup;
	readonly Label.Small count;
	readonly Label.Small validation;
	readonly Label.Body empty;
	readonly WarningBox wildcardWarning;
	int? editingLine;
	public Action ValueChanged { get; set; }
	public string Value { get => rules.Value; set => rules.Value = value; }

	// The publisher always copies fonts/* as loose files. A rule can still select registered
	// assets and their dependencies, so only remove these includes when the folder has no assets.
	internal static ResourceRuleCleanup CleanPublishingRules( string value, IEnumerable<string> assetPaths )
	{
		if ( assetPaths.Any( path => path.Replace( '\\', '/' ).TrimStart( '/' ).StartsWith( "fonts/", StringComparison.OrdinalIgnoreCase ) ) )
			return CleanResourceRules( value );

		var automatic = (value ?? "").Split( '\n' ).Select( ( text, line ) => new RuleEntry( line, text.Trim() ) )
			.Where( rule => !rule.IsComment && !rule.IsExclusion
				&& rule.Text.Replace( '\\', '/' ).TrimStart( '/' ).StartsWith( "fonts/", StringComparison.OrdinalIgnoreCase )
				&& !rule.Text.Replace( '\\', '/' ).Split( '/' ).Contains( ".." ) )
			.Select( rule => rule.Line ).ToHashSet();
		var withoutAutomatic = automatic.Count == 0 ? value
			: string.Join( "\n", value.Split( '\n' ).Where( ( _, index ) => !automatic.Contains( index ) ) );
		var result = CleanResourceRules( withoutAutomatic );
		return result with { Removed = result.Removed + automatic.Count };
	}

	public PublishResourceRulesWidget( Widget parent, Project project, string value, Layout actions, Layout agentText, Layout agentActions ) : base( parent, "Resource Files", Theme.Text, "folder" )
	{
		this.project = project;
		textMode = Header.Add( new PublishButton( "Edit as text", "code" ) );
		Layout.Add( new Label.Body( "Include or exclude files using the buttons below. Example: include scenes/*.scene, then exclude scenes/dev*.scene. Exclusions are saved with !; the last matching rule wins. Configured scenes, Resources folders and required dependencies still ship." ) ).SetStyles( "margin-bottom: 0px;" );
		var toolbar = Layout.AddRow();
		toolbar.Spacing = 6;
		agentText.Add( new Label( "AI is good at figuring out which assets your game needs to run. Paste this prompt into your preferred AI tool with access to your project. Or use the suggestions and file lists below to adjust what’s included yourself." ) { Color = Theme.TextLight, WordWrap = true, Alignment = TextFlag.LeftTop } );
		agent = agentActions.Add( new PublishButton( "Copy agent prompt", "smart_toy", primary: true ) );
		agent.ToolTip = "Copy a request to review these publishing rules with your coding agent. Includes the current rules, including unsaved edits.";
		agent.Clicked = () => _ = CopyAgentPrompt();
		cleanup = actions.Add( new PublishButton( "Clean up rules", "cleaning_services" ) );
		cleanup.ToolTip = "Remove duplicate rules, rules covered by another include, and redundant includes for automatically packaged font files. Keeps exclusions and rules that may select asset dependencies.";
		cleanup.Clicked = () => rules.Value = CleanPublishingRules( Value, AssetSystem.All.Select( x => x.Path ) ).Rules;
		toolbar.AddStretchCell();
		count = toolbar.Add( new Label.Small( "" ) { Alignment = TextFlag.RightCenter } );

		listEditor = Layout.Add( new Widget( this ) { Layout = Layout.Column() } );
		listEditor.Layout.Spacing = 6;
		list = listEditor.Layout.Add( new RuleList( listEditor, BeginEdit, RemoveRule, MoveRule ) );
		empty = listEditor.Layout.Add( new Label.Body( "No extra include rules. Add a path below, or use + in the preview." ) );
		var entry = listEditor.Layout.AddRow();
		entry.Spacing = 6;
		includeMode = entry.Add( new PublishButton( "Include", "add_circle_outline", primary: true ) );
		excludeMode = entry.Add( new PublishButton( "Exclude", "remove_circle_outline" ) );
		includeMode.ToolTip = "Add matching files and their dependencies.";
		excludeMode.ToolTip = "Exclude matches from earlier rules. For example, scenes/dev*.scene is stored as !scenes/dev*.scene. Required dependencies still ship.";
		includeMode.Clicked = () => SelectRuleMode( false );
		excludeMode.Clicked = () => SelectRuleMode( true );
		input = entry.Add( new LineEdit { PlaceholderText = "Path or wildcard, e.g. scenes/*.scene" }, 1 );
		apply = entry.Add( new PublishButton( "Add include", "add" ) { Enabled = false } );
		cancel = entry.Add( new PublishButton( "Cancel" ) { Visible = false } );
		validation = listEditor.Layout.Add( new Label.Small( "" ) { Color = Theme.Yellow, Visible = false } );
		input.TextChanged += _ =>
		{
			apply.Enabled = !string.IsNullOrWhiteSpace( input.Text );
			if ( input.Text.TrimStart().StartsWith( '!' ) ) SetRuleMode( true );
			validation.Visible = false;
		};
		input.ReturnPressed += ApplyRule;
		apply.Clicked = ApplyRule;
		cancel.Clicked = CancelEdit;

		// Keep the original representation for bulk edits and preserve comments and blank lines.
		rules = Layout.Add( new WildcardPathWidget( this, showListView: false ) { HideAssets = false, FixedHeight = 204, Visible = false } );
		rules.Value = value;
		wildcardWarning = Layout.Add( new WarningBox( "", this ) );
		rules.ValueChanged = () =>
		{
			CancelEdit();
			UpdateRules();
			ValueChanged?.Invoke();
		};
		textMode.Clicked = () => SetTextMode( !rules.Visible );
		UpdateRules();
	}

	async Task CopyAgentPrompt()
	{
		var cli = Path.GetFullPath( Path.Combine( Environment.CurrentDirectory, "bin", "managed", "sbox-cli.exe" ) );
		var arguments = JsonSerializer.Serialize( new { resources = Value ?? "", publishUsedAssetsOnly = true } );
		EditorUtility.Clipboard.Copy( $"""
			Check that everything needed by this s&box project is included when publishing: {project.ConfigFilePath}
			Read the publishing skill with: "{cli}" --skill sbox-publishing
			Start from these publishing settings, including unsaved changes:
			{arguments}

			Preview proposed changes and flag unusually large assets, even if referenced.
			Ask for my confirmation and for Project Settings to be closed before saving the agreed changes directly to the .sbproj. Keep the editor open.
			Don't delete assets or publish.
			""" );
		agent.Text = "Prompt copied";
		await Task.Delay( 1500 );
		if ( IsValid ) agent.Text = "Copy agent prompt";
	}

	public void StartRule( bool exclude )
	{
		SetTextMode( false );
		SelectRuleMode( exclude );
	}

	void SetTextMode( bool text )
	{
		CancelEdit();
		rules.Visible = text;
		listEditor.Visible = !text;
		textMode.Text = text ? "Use list" : "Edit as text";
		textMode.Icon = text ? "list" : "code";
	}

	void SelectRuleMode( bool exclude )
	{
		input.Text = input.Text.TrimStart().TrimStart( '!' ).TrimStart();
		SetRuleMode( exclude );
		input.Focus();
	}

	void SetRuleMode( bool exclude )
	{
		excluding = exclude;
		includeMode.Tint = exclude ? PublishButton.SecondaryTint : Theme.Primary;
		excludeMode.Tint = exclude ? Theme.Yellow : PublishButton.SecondaryTint;
		input.PlaceholderText = exclude ? "Exclude pattern, e.g. scenes/dev*.scene" : "Path or wildcard, e.g. scenes/*.scene";
		apply.Text = editingLine is not null ? "Save rule" : exclude ? "Add exclusion" : "Add include";
	}

	void BeginEdit( RuleEntry rule )
	{
		editingLine = rule.Line;
		input.Text = rule.IsExclusion ? rule.Text[1..].TrimStart() : rule.Text;
		SetRuleMode( rule.IsExclusion );
		apply.Icon = "check";
		cancel.Visible = true;
		input.Focus();
	}

	void CancelEdit()
	{
		editingLine = null;
		input.Text = "";
		SetRuleMode( false );
		apply.Icon = "add";
		cancel.Visible = false;
		validation.Visible = false;
	}

	void ApplyRule()
	{
		var text = input.Text.Trim();
		if ( text.Length == 0 ) return;
		if ( excluding && !text.StartsWith( '!' ) && !text.StartsWith( '#' ) && !text.StartsWith( "//" ) ) text = "!" + text;
		var path = text.StartsWith( '!' ) ? text[1..].TrimStart() : text;
		if ( text.IndexOfAny( ['\r', '\n'] ) >= 0 || (!(text.StartsWith( "//" ) || text.StartsWith( '#' ))
			&& (path.Length == 0 || path.IndexOfAny( [':', '"'] ) >= 0 || path.Replace( '\\', '/' ).Split( '/' ).Contains( ".." ))) )
		{
			validation.Text = "Enter one relative path or wildcard, optionally prefixed with !. Use Edit as text to paste multiple rules.";
			validation.Visible = true;
			return;
		}
		if ( editingLine is { } line )
		{
			var lines = (Value ?? "").Split( '\n' );
			var ending = lines[line].EndsWith( '\r' ) ? "\r" : "";
			lines[line] = text + ending;
			rules.Value = string.Join( "\n", lines );
		}
		else
		{
			var separator = (Value ?? "").Contains( "\r\n" ) ? "\r\n" : "\n";
			rules.Value = string.IsNullOrEmpty( Value ) ? text : Value + (Value.EndsWith( '\n' ) ? "" : separator) + text;
		}
		CancelEdit();
	}

	void RemoveRule( RuleEntry rule ) => rules.Value = string.Join( "\n", (Value ?? "").Split( '\n' ).Where( ( _, i ) => i != rule.Line ) );

	void MoveRule( RuleEntry rule, int direction )
	{
		var lines = (Value ?? "").Split( '\n' );
		var target = rule.Line + direction;
		while ( target >= 0 && target < lines.Length && string.IsNullOrWhiteSpace( lines[target] ) ) target += direction;
		if ( target < 0 || target >= lines.Length ) return;
		var from = lines[rule.Line].TrimEnd( '\r' );
		var to = lines[target].TrimEnd( '\r' );
		lines[rule.Line] = to + (lines[rule.Line].EndsWith( '\r' ) ? "\r" : "");
		lines[target] = from + (lines[target].EndsWith( '\r' ) ? "\r" : "");
		rules.Value = string.Join( "\n", lines );
	}

	void UpdateRules()
	{
		var entries = (Value ?? "").Split( '\n' ).Select( ( text, line ) => new RuleEntry( line, text.Trim() ) )
			.Where( x => x.Text.Length > 0 ).ToArray();
		list.SetItems( entries );
		list.FixedHeight = Math.Clamp( entries.Length, 1, 6 ) * 28 + 4;
		list.Visible = entries.Length > 0;
		empty.Visible = entries.Length == 0;
		count.Text = $"{entries.Count( x => !x.IsComment && !x.IsExclusion ):n0} includes, {entries.Count( x => x.IsExclusion ):n0} exclusions";
		var redundant = CleanPublishingRules( Value, AssetSystem.All.Select( x => x.Path ) ).Removed;
		cleanup.Enabled = redundant > 0;
		cleanup.Text = $"Clean up rules ({redundant:n0})";
		var blanket = entries.Where( x => !x.IsComment && !x.IsExclusion ).Select( x => x.Text ).Where( x =>
		{
			var wildcard = x.IndexOfAny( ['*', '?'] );
			return wildcard >= 0 && !x[..wildcard].Any( char.IsLetterOrDigit );
		} ).ToArray();
		wildcardWarning.Visible = blanket.Length > 0;
		wildcardWarning.Label.Text = $"Broad rules ({WebUtility.HtmlEncode( string.Join( ", ", blanket ) )}) include matching files unless a later exclusion applies, even when unused. Prefer specific paths when you only need some of them.";
	}

	sealed record RuleEntry( int Line, string Text )
	{
		public bool IsComment => Text.StartsWith( "//" ) || Text.StartsWith( '#' );
		public bool IsExclusion => Text.StartsWith( '!' );
		public bool IsPattern => Text.IndexOfAny( ['*', '?'] ) >= 0;
	}

	sealed class RuleList : ListView
	{
		readonly Action<RuleEntry> edit;
		readonly Action<RuleEntry> remove;
		readonly Action<RuleEntry, int> move;
		public RuleList( Widget parent, Action<RuleEntry> edit, Action<RuleEntry> remove, Action<RuleEntry, int> move ) : base( parent )
		{
			this.edit = edit;
			this.remove = remove;
			this.move = move;
			ItemSize = new Vector2( 0, 28 );
			ItemActivated = item => edit( (RuleEntry)item );
		}

		static Rect ButtonRect( VirtualWidget item, int offset )
		{
			var rect = item.Rect.Shrink( 3 );
			rect.Right -= offset * 26;
			rect.Left = rect.Right - 22;
			return rect;
		}

		protected override void PaintItem( VirtualWidget item )
		{
			var rule = (RuleEntry)item.Object;
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.WithAlpha( item.Hovered ? 0.6f : 0.3f ) );
			Paint.DrawRect( item.Rect.Shrink( 0, 1 ), 3 );
			var icon = item.Rect.Shrink( 6, 0 );
			icon.Width = 18;
			Paint.SetPen( rule.IsComment ? Theme.TextLight : rule.IsExclusion ? Theme.Red : rule.IsPattern ? Theme.Yellow : Theme.Primary );
			Paint.DrawIcon( icon, rule.IsComment ? "notes" : rule.IsExclusion ? "remove_circle_outline" : rule.IsPattern ? "filter_alt" : "insert_drive_file", 16 );
			var text = item.Rect.Shrink( 30, 0, 110, 0 );
			Paint.SetPen( rule.IsComment ? Theme.TextLight : Theme.TextControl );
			Paint.DrawText( text, Paint.GetElidedText( rule.IsComment ? rule.Text : $"{(rule.IsExclusion ? "Exclude" : "Include")}: {rule.Text.TrimStart( '!' )}", text.Width ), TextFlag.LeftCenter | TextFlag.SingleLine );
			Paint.SetPen( Theme.TextLight );
			Paint.DrawIcon( ButtonRect( item, 3 ), "arrow_upward", 15 );
			Paint.DrawIcon( ButtonRect( item, 2 ), "arrow_downward", 15 );
			Paint.DrawIcon( ButtonRect( item, 1 ), "edit", 15 );
			Paint.DrawIcon( ButtonRect( item, 0 ), "close", 16 );
		}

		protected override bool OnItemPressed( VirtualWidget item, MouseEvent e )
		{
			if ( ButtonRect( item, 0 ).IsInside( e.LocalPosition ) ) remove( (RuleEntry)item.Object );
			else if ( ButtonRect( item, 1 ).IsInside( e.LocalPosition ) ) edit( (RuleEntry)item.Object );
			else if ( ButtonRect( item, 2 ).IsInside( e.LocalPosition ) ) move( (RuleEntry)item.Object, 1 );
			else if ( ButtonRect( item, 3 ).IsInside( e.LocalPosition ) ) move( (RuleEntry)item.Object, -1 );
			else return base.OnItemPressed( item, e );
			e.Accepted = true;
			return false;
		}

		protected override string GetTooltip( object item ) => WebUtility.HtmlEncode( $"{((RuleEntry)item).Text}\nLast match wins. Use the arrows to reorder, pencil to edit, or × to remove. Removing a rule does not delete any files." );
	}
}
