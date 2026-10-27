using System.IO;
using System.Net;
using System.Threading;
using static Editor.ProjectPublisher.PackageManifest;

namespace Editor;

/// <summary>The same publish preview and rule suggestions in project settings and the publish wizard.</summary>
internal sealed class PublishAssetReviewWidget : Widget
{
	readonly Func<CancellationToken, Task<ProjectPublisher.PackageManifest>> buildManifest;
	readonly Func<string> getRules;
	readonly Action<string> setRules;
	readonly Project project;
	readonly SemaphoreSlim buildGate = new( 1, 1 );
	readonly Label.Small status;
	readonly Label.Small estimateNotice;
	readonly Widget content;
	readonly Widget fileContent;
	readonly PublishSection contents;
	readonly Label.Small summary;
	Button recommended;
	WarningList warningList;
	CancellationTokenSource refreshSource;
	int generation;
	bool refreshing;
	bool stale;
	string error;
	string includedFilter = "";
	string excludedFilter = "";
	int includedSort = 1;
	int excludedSort = 1;
	int warningSort = 1;
	readonly HashSet<string> expandedWarningGroups = new();
	bool addingFromPreview;
	readonly PublishRuleSelection selectedRules = new();
	CodeAssetWarning[] suggestions;
	string[] recommendedRules;
	int recommendedFileCount;
	Dictionary<string, long?> suggestionSizes;
	readonly List<Widget> actionLists = new();
	readonly HashSet<string> expandedIncludedGroups = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> expandedExcludedGroups = new( StringComparer.OrdinalIgnoreCase );

	public ProjectPublisher.PackageManifest Manifest { get; private set; }
	public bool IsCurrent => !refreshing && !stale && Manifest is not null;
	public bool IsRefreshing => refreshing;
	public Action PreviewStateChanged { get; set; }
	public Widget RulesEditor { get; }
	public Layout RuleActions { get; }
	public Action<bool> EditRule { get; set; }

	public PublishAssetReviewWidget( Widget parent, Project project, Func<CancellationToken, Task<ProjectPublisher.PackageManifest>> buildManifest, Func<string> getRules, Action<string> setRules, string footerText ) : base( parent )
	{
		this.project = project;
		this.buildManifest = buildManifest;
		this.getRules = getRules;
		this.setRules = setRules;
		Layout = Layout.Column();
		Layout.Spacing = 14;
		Layout.Alignment = TextFlag.Top;
		content = Layout.Add( new Widget( this ) { Layout = Layout.Column() } );
		contents = Layout.Add( new PublishSection( this, "Included & excluded files", Theme.Text, "folder" ) );
		RuleActions = contents.Header;
		contents.Header.Add( new PublishButton( "Edit rules", "tune" ) ).Clicked = () => ShowRules();
		summary = contents.Layout.Add( new Label.Small( "Preparing package contents…" ) );
		fileContent = contents.Layout.Add( new Widget( contents ) { Layout = Layout.Column() } );
		RulesEditor = contents.Layout.Add( new Widget( contents ) { Layout = Layout.Column(), Visible = false } );
		contents.Layout.Add( new Label.Small( "Exclusions override include rules. Files included automatically (such as Resources/ files and fonts), and assets needed by other included content, will still ship." ) );
		var footer = Layout.AddRow();
		footer.Margin = new Margin( 12, 0 );
		footer.Spacing = 20;
		footer.Add( new Label.Small( $"{footerText}\nCompiled game code and cloud packages referenced by code are added when publishing." ) { Color = Theme.TextLight, Alignment = TextFlag.LeftCenter }, 1 );
		estimateNotice = footer.Add( new Label.Small( "Estimates incomplete" ) { Visible = false, Color = Theme.TextLight, WordWrap = false } );
		status = footer.Add( new Label.Small( "Preparing preview…" ) { Color = Theme.TextLight, WordWrap = false, Alignment = TextFlag.RightCenter } );
	}

	void ShowRules( bool? exclude = null )
	{
		RulesEditor.Visible = exclude is not null || !RulesEditor.Visible;
		if ( exclude is { } value ) EditRule?.Invoke( value );
	}

	/// <summary>Refresh after typing pauses, cancelling any older preview.</summary>
	public void InvalidatePreview()
	{
		// Apply rule edits immediately, but batch the expensive analysis after a burst of clicks.
		if ( !addingFromPreview )
		{
			selectedRules.Reset();
			suggestions = null;
		}
		_ = RefreshAsync( addingFromPreview ? 900 : 350 );
	}

	public async Task RefreshAsync( int delay = 0 )
	{
		// Invalidate the old result before cancellation can run its continuation.
		var revision = ++generation;
		refreshSource?.Cancel();
		var source = new CancellationTokenSource();
		refreshSource = source;
		refreshing = true;
		stale = true;
		if ( recommended.IsValid() ) recommended.Enabled = false;
		error = null;
		content.Enabled = Manifest is not null;
		status.Text = "Updating preview…";
		status.Color = Theme.Primary;
		PreviewStateChanged?.Invoke();
		try
		{
			// Give the loading indicator a frame to paint, even for an immediate refresh.
			await Task.Delay( Math.Max( delay, 32 ), source.Token );
			// Some publisher operations cannot be cancelled; never overlap two builds.
			await buildGate.WaitAsync( source.Token );
			try
			{
				source.Token.ThrowIfCancellationRequested();
				var result = await buildManifest( source.Token );
				if ( !IsValid || revision != generation || source.IsCancellationRequested ) return;
				Manifest = result;
				stale = false;
			}
			finally
			{
				buildGate.Release();
			}
		}
		catch ( OperationCanceledException ) when ( source.IsCancellationRequested ) { }
		catch ( Exception ex )
		{
			if ( revision == generation ) error = ex.Message;
			Log.Warning( ex );
		}
		finally
		{
			if ( IsValid && revision == generation )
			{
				refreshing = false;
				Rebuild();
				PreviewStateChanged?.Invoke();
			}
			if ( refreshSource == source ) refreshSource = null;
			source.Dispose();
		}
	}

	public void CancelPreview()
	{
		generation++;
		refreshSource?.Cancel();
		refreshing = false;
		stale = true;
		PreviewStateChanged?.Invoke();
	}

	public override void OnDestroyed()
	{
		generation++;
		refreshSource?.Cancel();
		base.OnDestroyed();
	}

	public void Rebuild()
	{
		status.Text = refreshing ? "Updating preview…" : IsCurrent ? "Up to date" : "Preview unavailable";
		status.Color = refreshing ? Theme.Primary : Theme.TextLight;
		if ( refreshing ) return;

		var warningScroll = suggestions is not null && warningList.IsValid() ? warningList.VerticalScrollbar.Value : 0;
		actionLists.Clear();
		content.Layout.Clear( true );
		fileContent.Layout.Clear( true );
		content.Layout.Spacing = 12;
		content.Enabled = true;
		contents.Visible = Manifest is not null;
		if ( error is not null )
		{
			content.Layout.Add( new WarningBox( WebUtility.HtmlEncode( error ), content ) );
			content.Layout.Add( new Button( "Retry", "refresh" ) ).Clicked = () => _ = RefreshAsync();
		}
		if ( Manifest is null ) return;

		var manifest = Manifest;
		if ( suggestions is null )
		{
			suggestions = manifest.CodeAssetWarnings.ToArray();
			recommendedRules = manifest.RecommendedRules;
			recommendedFileCount = manifest.RecommendedIncludes.Length;
			suggestionSizes = suggestions.DistinctBy( x => x.Pattern, StringComparer.OrdinalIgnoreCase )
				.ToDictionary( x => x.Pattern, x => SizeForSort( manifest.GetPublishImpact( x.ExcludedPaths ) ), StringComparer.OrdinalIgnoreCase );
		}
		var estimateIssues = manifest.GetEstimateIssues();
		estimateNotice.Visible = estimateIssues.Count > 0;
		estimateNotice.Text = $"Estimates incomplete ({estimateIssues.Count:n0} paths)";
		estimateNotice.ToolTip = WebUtility.HtmlEncode( "Sizes and dependency counts show the known amounts; some content could not be measured. Excluded assets are not compiled for the preview.\n"
			+ string.Join( "\n", estimateIssues.Take( 16 ).Select( x => $"{x.Path}: {x.Reason}" ) )
			+ (estimateIssues.Count > 16 ? $"\n… and {estimateIssues.Count - 16:n0} more paths." : "") );
		var files = manifest.Assets.GroupBy( x => ReviewAssetPath( x.Name ), StringComparer.OrdinalIgnoreCase ).Select( group =>
			new FileEntry( group.Key, group.Sum( x => (long)x.Size ),
				Dependencies: manifest.GetDependencySummary( group.Select( x => x.Name ) ),
				Note: string.Join( "\n", manifest.GetInclusionReasons( group.Select( x => x.Name ) ).Concat( group.Select( x => manifest.IncludedExclusionReasons.GetValueOrDefault( x.Name.TrimStart( '/' ) ) ).Where( x => x is not null ) ).Distinct() ),
				Files: group.Select( x => x.Name ).ToArray() ) ).ToArray();
		var excludedFiles = manifest.ExcludedAssets.GroupBy( ReviewAssetPath, StringComparer.OrdinalIgnoreCase ).Select( group =>
			new FileEntry( group.Key, Impact: manifest.GetPublishImpact( group ), Dependencies: manifest.GetDependencySummary( group ),
				Note: string.Join( "\n", group.Where( manifest.ExcludedByRules.ContainsKey ).Select( x => $"Excluded by {manifest.ExcludedByRules[x]}. Click + to add an inclusion after it." ).Distinct() ),
				Files: group.ToArray() ) ).ToArray();
		AddScenes( files );
		if ( manifest.ExclusionOverrides.Count > 0 )
		{
			var kept = content.Layout.Add( new PublishSection( content, $"Required despite exclusions ({manifest.ExclusionOverrides.Count:n0})", Theme.Yellow, "info" ) );
			kept.Layout.Add( new Label.Body( "These files still ship because they are configured, in Resources, or required by included content." ) );
			var list = kept.Layout.Add( new FilePathList( kept, null, details: true ) { FixedHeight = Math.Min( manifest.ExclusionOverrides.Count, 3 ) * 40 + 4 } );
			list.SetItems( manifest.ExclusionOverrides.Select( x => new FileEntry( ReviewAssetPath( x.Path ), x.Size, Note: $"{x.Rule} overridden: {x.Reason}" ) ) );
		}

		foreach ( var message in manifest.Errors )
			content.Layout.Add( new WarningBox( WebUtility.HtmlEncode( message ), content ) );

		var includedPaths = files.Select( x => x.Path ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		var warningEntries = manifest.SceneWarnings.Where( scene => scene.Missing ).Select( scene => new WarningEntry(
			$"Missing scene: {ReviewAssetPath( scene.Path )}", "Update Game Setup or restore this configured scene.", null, 0, null, null ) )
			.Concat( suggestions.DistinctBy( x => ReviewAssetPath( x.Pattern ), StringComparer.OrdinalIgnoreCase ).Select( warning => new WarningEntry(
				WarningTitle( warning ), WarningReason( warning ),
				warning.Pattern, 2, manifest.GetPublishImpact( warning.ExcludedPaths ), manifest.GetDependencySummary( warning.ExcludedPaths ), warning,
				Included: warning.ExcludedPaths.All( x => includedPaths.Contains( ReviewAssetPath( x ) ) ) ) ) ).ToArray();
		var warningCount = warningEntries.Select( x => x.GroupKey ?? $"entry:{x.Title}" ).Distinct().Count();
		var warnings = content.Layout.Add( new PublishSection( content, "Warnings & suggestions", Theme.Text, "fact_check", count: warningCount ) );
		var recommendedCount = recommendedFileCount;
		var recommendationsAdded = selectedRules.IsSelected( recommendedRules );
		recommended = warnings.Header.Add( new PublishButton( recommendationsAdded ? "Added recommended" : recommendedCount > 0 ? $"Add recommended ({recommendedCount:n0} files)" : "Add recommended", recommendationsAdded ? "check" : "playlist_add", primary: true ) { Enabled = IsCurrent && recommendedCount > 0 } );
		recommended.ToolTip = recommendedCount == 0 ? "No recommended files to add. Resource queries still need manual review."
			: "Include exact paths found in code, using compact rules where safe. Intentional exclusions are respected. Queries and dynamic loads still need review; wildcards include future matching files.";
		if ( recommendationsAdded ) recommended.ToolTip = "Remove recommended rules added during this review. Pre-existing rules are kept.";
		recommended.Clicked = () => { if ( IsCurrent ) Include( recommendedRules ); };
		if ( warningEntries.Length == 0 )
		{
			warnings.Layout.Add( new Label.Body( "No scene or code-reference warnings. Dynamic loads may still need include rules." ) );
		}
		else
		{
			warnings.ToolTip = "Suggestions stay visible while you review. Click Added to undo an addition. Manual rule edits start a fresh review.";
			// Missing configured scenes stay visible; resource query groups start collapsed.
			var list = warningList = warnings.Layout.Add( new WarningList( warnings, Include ) { IsAdded = IsAdded, RestoreScroll = warningScroll } );
			actionLists.Add( list );
			void PopulateWarnings()
			{
				var rows = warningEntries.GroupBy( x => x.Priority ).OrderBy( x => x.Key )
					.SelectMany( group => SortItems( group, warningSort, x => x.Title, x => x.Source is null ? SizeForSort( x.Impact ) : suggestionSizes.GetValueOrDefault( x.Source.Pattern ), x => x.Dependencies ) ).ToArray();
				var visible = new List<WarningEntry>();
				foreach ( var group in rows.GroupBy( x => x.GroupKey ?? $"entry:{x.Title}" ) )
				{
					var first = group.First();
					var grouped = first.GroupKey is not null && group.Count() > 1;
					if ( grouped )
					{
						visible.Add( first with
						{
							Title = "Runtime content needs review",
							Details = $"A code query matches {group.Count():n0} asset types. Expand to choose which content should ship.",
							IncludeRule = null,
							Impact = null,
							Dependencies = null,
							Included = group.All( x => x.Included ),
							IsGroup = true
						} );
						if ( !expandedWarningGroups.Contains( first.GroupKey ) ) continue;
					}
					foreach ( var entry in group )
					{
						var paths = entry.Source?.ExcludedPaths ?? [];
						var expandable = paths.Length > 1 || paths.Length > 0 && entry.Source?.Usage?.StartsWith( "GetAll<", StringComparison.Ordinal ) == true;
						var key = $"files:{entry.Source?.SourceFile}\n{entry.Source?.Line}\n{entry.IncludeRule}";
						visible.Add( entry with { IsGroup = expandable, ExpansionKey = key, Depth = grouped ? 1 : 0 } );
						if ( !expandable || !expandedWarningGroups.Contains( key ) ) continue;
						foreach ( var path in paths.Select( ReviewAssetPath ).Distinct( StringComparer.OrdinalIgnoreCase ).Order( StringComparer.OrdinalIgnoreCase ) )
						{
							visible.Add( new WarningEntry( path, "Not included in this package", path, entry.Priority,
								manifest.GetPublishImpact( [path] ), manifest.GetDependencySummary( [path] ),
								IsAsset: true, Depth: grouped ? 2 : 1, Included: includedPaths.Contains( path ) ) );
						}
					}
				}
				list.SetItems( visible );
				list.FixedHeight = Math.Min( visible.Count, 6 ) * 52 + 4;
				list.Visible = visible.Count > 0;
			}
			list.IsGroupExpanded = expandedWarningGroups.Contains;
			list.ToggleGroup = key =>
			{
				if ( !expandedWarningGroups.Add( key ) ) expandedWarningGroups.Remove( key );
				PopulateWarnings();
			};
			if ( warningCount > 6 ) warnings.Header.Add( SortControl( warnings, warningSort, sort => { warningSort = sort; PopulateWarnings(); } ) );
			PopulateWarnings();
		}

		summary.Text = $"{files.Length:n0} included, {excludedFiles.Length:n0} excluded, {files.Sum( x => x.Size ).FormatBytes()} uncompressed (preview)";
		var columns = fileContent.Layout.AddRow();
		columns.Spacing = 12;
		columns.Add( FileList( $"Included ({files.Length:n0})", Theme.Green, "check_circle",
			"Publish size (uncompressed)", files, false ), 1 );
		columns.Add( FileList( $"Excluded ({excludedFiles.Length:n0})", Theme.TextLight, "remove_circle_outline",
			"Estimated additional publish size", excludedFiles, true ), 1 );
	}

	void AddScenes( FileEntry[] files )
	{
		var scenes = files.Where( x => x.Path.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( x => x.Path, StringComparer.OrdinalIgnoreCase ).Select( x => x with { Note = SceneNote( x ) } ).ToArray();
		var section = content.Layout.Add( new PublishSection( content, "Included scenes", Theme.Text, "image", count: scenes.Length, iconImage: AssetType.FromExtension( "scene" )?.Icon16 ) );
		section.Header.Add( new PublishButton( "Add scene", "add", primary: true ) ).Clicked = OpenScenePicker;
		if ( scenes.Length == 0 ) section.Layout.Add( new Label.Body( "No scenes in this preview. Add a scene or check Game Setup." ) );
		else
		{
			section.Layout.Add( new SceneColumns( section ) );
			var list = section.Layout.Add( new SceneList( section ) { FixedHeight = Math.Min( scenes.Length, 6 ) * 38 + 4 } );
			list.SetItems( scenes );
		}
	}

	string SceneNote( FileEntry file )
	{
		var roles = new[] { ("StartupScene", "start.scene", "Startup scene"), ("MapStartupScene", "", "Map startup scene"),
			("DedicatedServerStartupScene", "", "Dedicated server startup scene"), ("SystemScene", "", "System scene") };
		var labels = roles.Where( role => project.Config.GetMetaOrDefault( role.Item1, role.Item2 ) is { Length: > 0 } path
			&& string.Equals( ReviewAssetPath( path ), file.Path, StringComparison.OrdinalIgnoreCase ) )
			.Select( role => role.Item3 ).ToArray();
		return string.Join( "\n", labels.Append( file.Note ).Where( x => !string.IsNullOrEmpty( x ) ) );
	}

	void OpenScenePicker()
	{
		if ( !IsCurrent ) return;
		// Pick from the manifest's project/library candidates, not unrelated cloud or mounted assets.
		var candidates = Manifest.ExcludedAssets.Select( ReviewAssetPath ).Where( x => x.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) )
			.Distinct( StringComparer.OrdinalIgnoreCase ).Order( StringComparer.OrdinalIgnoreCase )
			.Select( x => new FileEntry( x, Impact: Manifest.GetPublishImpact( [x] ), Dependencies: Manifest.GetDependencySummary( [x] ) ) ).ToArray();
		var dialog = new Dialog( this );
		dialog.Window.Title = "Add scenes to publishing";
		dialog.Window.Size = new Vector2( 620, 460 );
		dialog.Window.SetModal( true );
		dialog.Layout = Layout.Column();
		dialog.Layout.Margin = 12;
		dialog.Layout.Spacing = 8;
		dialog.Layout.Add( new Label.Body( "Select scenes to include with their dependencies. This adds rules without changing Game Setup." ) );
		var search = dialog.Layout.Add( new LineEdit { PlaceholderText = "Filter scenes…" } );
		var list = dialog.Layout.Add( new FilePathList( dialog, null ) { MultiSelect = true }, 1 );
		list.SetItems( candidates );
		search.TextChanged += text => list.SetItems( candidates.Where( x => x.Path.Contains( text ?? "", StringComparison.OrdinalIgnoreCase ) ) );
		if ( candidates.Length == 0 ) dialog.Layout.Add( new Label.Body( "All available project scenes are already included." ) );
		var actions = dialog.Layout.AddRow();
		actions.AddStretchCell();
		actions.Add( new PublishButton( "Cancel" ) ).Clicked = dialog.Close;
		actions.Add( new PublishButton( "Include selected", "add", primary: true ) ).Clicked = () =>
		{
			if ( !IsValid || !IsCurrent ) return;
			var selected = list.SelectedItems.OfType<FileEntry>().Select( x => x.Path ).ToArray();
			if ( selected.Length == 0 ) return;
			Include( selected );
			dialog.Close();
		};
		dialog.Show();
	}

	Widget FileList( string title, Color accent, string icon, string description, IEnumerable<FileEntry> paths, bool excluded )
	{
		var page = new PublishSection( fileContent, title, accent, icon ) { MinimumHeight = 240 };
		var entries = paths.ToArray();
		var totalImpact = excluded ? Manifest.GetPublishImpact( entries.SelectMany( x => x.Paths ) ) : null;
		var bytes = totalImpact is null ? entries.Sum( x => x.Size ) : totalImpact.DirectBytes + totalImpact.TransitiveBytes;
		var sizeText = bytes == 0 && totalImpact is { TotalBytes: null } ? "Unavailable" : bytes.FormatBytes();
		page.Header.Add( new SizeDisplay( page, sizeText, description,
			totalImpact is null ? null : $"{DirectSize( totalImpact )} direct {TransitiveSize( totalImpact )}\n{ImpactTooltip}" ) );
		var sortRow = page.Layout.AddRow();
		sortRow.Spacing = 6;
		var filter = sortRow.Add( new LineEdit( page ) { Text = excluded ? excludedFilter : includedFilter }, 1 );
		filter.PlaceholderText = excluded ? "Filter excluded files…" : "Filter included files…";
		filter.FixedHeight = 30;
		page.Layout.Add( new FileColumns( page, excluded ) );
		var list = page.Layout.Add( new FilePathList( page, Include, showInclusion: !excluded ) { FixedHeight = 224 }, 1 );
		list.IsAdded = IsAdded;
		list.IsSelectionAdded = selectedRules.IsSelected;
		if ( !excluded ) list.Exclude = paths => Include( paths.Select( path => $"!{path}" ).ToArray() );
		if ( !excluded ) list.GetFootprint = Manifest.GetPublishFootprint;
		actionLists.Add( list );
		var groups = GroupFiles( entries );
		var expanded = excluded ? expandedExcludedGroups : expandedIncludedGroups;
		void Populate()
		{
			var rows = new List<object>();
			var query = filter.Text ?? "";
			var sort = excluded ? excludedSort : includedSort;
			var matches = groups.Select( group =>
			{
				var matches = group.Entries.Where( x => group.Title.Contains( query, StringComparison.OrdinalIgnoreCase ) || x.Path.Contains( query, StringComparison.OrdinalIgnoreCase ) ).ToArray();
				return new FileGroupHeader( group, matches, excluded ? Manifest.GetPublishImpact( matches.SelectMany( x => x.Paths ) ) : null, Manifest.GetDependencySummary( matches.SelectMany( x => x.Paths ) ), !expanded.Contains( group.Key ) );
			} )
				.Where( x => x.Matches.Length > 0 );
			foreach ( var header in SortItems( matches, sort, x => x.Group.Title, x => x.SortSize, x => x.Dependencies ) )
			{
				rows.Add( header );
				if ( header.Collapsed ) continue;
				foreach ( var entry in SortItems( header.Matches, sort, x => x.Path, x => x.SortSize, x => x.Dependencies ) ) rows.Add( entry );
			}
			list.SetItems( rows );
		}
		list.ToggleGroup = key =>
		{
			if ( !expanded.Add( key ) ) expanded.Remove( key );
			Populate();
		};
		sortRow.Add( SortControl( page, excluded ? excludedSort : includedSort, sort =>
		{
			if ( excluded ) excludedSort = sort;
			else includedSort = sort;
			Populate();
		} ) );
		filter.TextChanged += text =>
		{
			if ( excluded ) excludedFilter = text;
			else includedFilter = text;
			Populate();
		};
		Populate();
		var actions = page.Layout.AddRow();
		actions.Add( new PublishButton( excluded ? "Include rule" : "Exclude rule", "add" ) ).Clicked = () => ShowRules( !excluded );
		actions.AddStretchCell();
		return page;
	}

	static Widget SortControl( Widget parent, int current, Action<int> changed )
	{
		var control = new ComboBox( parent );
		control.FixedWidth = 110;
		control.FixedHeight = 30;
		var labels = new[] { "Name", "Largest", "Smallest", "Most deps" };
		for ( var i = 0; i < labels.Length; i++ )
		{
			var index = i;
			control.AddItem( labels[i], onSelected: () => changed( index ), selected: i == current );
		}
		control.ToolTip = $"Sort by name, size or unique transitive dependencies. Partial estimates (~) sort by their known size; unavailable sizes appear last.\n{DependencyTooltip}";
		return control;
	}

	static long? SizeForSort( PublishImpact impact ) => impact is null ? null
		: impact.TotalBytes ?? (impact.DirectBytes + impact.TransitiveBytes > 0 ? impact.DirectBytes + impact.TransitiveBytes : null);

	static IOrderedEnumerable<T> SortItems<T>( IEnumerable<T> items, int sort, Func<T, string> name, Func<T, long?> size, Func<T, DependencySummary> dependencies )
	{
		if ( sort == 0 ) return items.OrderBy( name, StringComparer.OrdinalIgnoreCase );
		if ( sort == 3 ) return items.OrderByDescending( x => dependencies( x )?.Count ?? -1 ).ThenBy( name, StringComparer.OrdinalIgnoreCase );
		var known = items.OrderBy( x => size( x ) is null );
		return (sort == 1 ? known.ThenByDescending( size ) : known.ThenBy( size )).ThenBy( name, StringComparer.OrdinalIgnoreCase );
	}

	void Include( string pattern ) => Include( [pattern] );

	bool IsAdded( string pattern ) => selectedRules.Contains( pattern );

	void Include( string[] patterns )
	{
		if ( Manifest is null ) return;
		if ( patterns.Length == 0 ) return;
		// Re-including a file excluded during this review restores its original rules.
		if ( patterns.Length == 1 && !patterns[0].StartsWith( '!' ) && selectedRules.Contains( $"!{patterns[0]}" ) )
			patterns = [$"!{patterns[0]}"];
		var revision = generation;
		addingFromPreview = true;
		try
		{
			var updated = selectedRules.Toggle( getRules(), patterns );
			foreach ( var list in actionLists ) list.Update();
			if ( updated != getRules() ) setRules( updated );
			if ( revision == generation ) InvalidatePreview();
		}
		catch ( Exception ex )
		{
			error = ex.Message;
			Rebuild();
		}
		finally
		{
			addingFromPreview = false;
		}
	}

	// Show logical assets while retaining every physical file for size and dependency calculations.
	static string ReviewAssetPath( string path )
	{
		if ( path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) path = path[..^2];
		var asset = AssetSystem.FindByPath( path );
		// Simple assets can have a canonical extension different from their actual file (e.g. JPG vs PNG).
		// Include actions must target the real file when there is no compiled output.
		if ( asset is not null && string.IsNullOrEmpty( asset.GetCompiledFile() ) && asset.GetSourceFile() is { Length: > 0 } source )
			return source;
		return asset?.Path ?? path;
	}

	sealed record FileEntry( string Path, long Size = 0, PublishImpact Impact = null, string Note = null, DependencySummary Dependencies = null, string[] Files = null )
	{
		public IEnumerable<string> Paths => Files ?? [Path];
		public long? SortSize => Impact is null ? Size : SizeForSort( Impact );
		// Be conservative: every known reason must be an explicit include rule.
		public bool CanExclude => !string.IsNullOrEmpty( Note ) && Note.Split( '\n', StringSplitOptions.RemoveEmptyEntries )
			.All( reason => reason.StartsWith( "Included by rule: ", StringComparison.Ordinal ) );
	}
	sealed record FileGroup( string Key, string Title, string[] Rules, FileEntry[] Entries, AssetType Type );

	static void DrawAssetIcon( Rect rect, string path, AssetType type = null )
	{
		type ??= AssetSystem.FindByPath( path )?.AssetType;
		if ( type?.Icon16 is { } icon )
		{
			Paint.ClearPen();
			Paint.Draw( new Rect( rect.Left, rect.Center.y - 8, 16, 16 ), icon );
		}
		else
		{
			Paint.SetPen( Theme.TextLight );
			Paint.DrawIcon( rect, "insert_drive_file", 16 );
		}
	}
	sealed record FileGroupHeader( FileGroup Group, FileEntry[] Matches, PublishImpact Impact = null, DependencySummary Dependencies = null, bool Collapsed = false )
	{
		public long Size { get; } = Matches.Sum( x => x.Size );
		public long? SortSize => Impact is null ? Size : SizeForSort( Impact );
	}

	static string InclusionSource( FileEntry file, bool categoryOnly = false )
	{
		var reasons = (file.Note ?? "").Split( '\n', StringSplitOptions.RemoveEmptyEntries );
		if ( reasons.Contains( "Included by the Resources/ folder." ) ) return "Resources/";
		if ( reasons.Contains( "Referenced by a configured scene or project setting." ) ) return "Game setup";
		// Show the reason that prevents exclusion before any redundant include rule.
		if ( reasons.Contains( "Localization files are included automatically." ) ) return "Localization";
		if ( reasons.Contains( "Fonts are included automatically." ) ) return "Fonts (automatic)";
		if ( reasons.Contains( "Project settings are included automatically." ) ) return "Project settings";
		var references = reasons.Where( x => x.StartsWith( "Referenced by " ) ).ToArray();
		if ( references.Length > 0 ) return categoryOnly ? "References" : $"Ref: {Path.GetFileName( references[0][14..] )}{(references.Length > 1 ? $" +{references.Length - 1}" : "")}";
		var runtime = reasons.FirstOrDefault( x => x.StartsWith( "Runtime file for " ) );
		if ( runtime is not null ) return categoryOnly ? "Runtime files" : $"Runtime: {Path.GetFileName( runtime[17..] )}";
		var thumbnail = reasons.FirstOrDefault( x => x.StartsWith( "Thumbnail for " ) );
		if ( thumbnail is not null ) return categoryOnly ? "Thumbnails" : $"Thumbnail: {Path.GetFileName( thumbnail[14..] )}";
		var rule = reasons.FirstOrDefault( x => x.StartsWith( "Included by rule: " ) );
		if ( rule is not null ) return categoryOnly ? "Include rules" : $"Rule: {rule[18..]}";
		return "Automatic";
	}

	static string GroupInclusionSource( FileGroupHeader group )
	{
		var sources = group.Matches.Select( x => InclusionSource( x, categoryOnly: true ) ).Distinct().Order().ToArray();
		return string.Join( ", ", sources.Take( 2 ) ) + (sources.Length > 2 ? $" +{sources.Length - 2}" : "");
	}

	sealed class FileColumns : Widget
	{
		readonly bool excluded;
		public FileColumns( Widget parent, bool excluded ) : base( parent )
		{
			this.excluded = excluded;
			FixedHeight = 22;
		}
		protected override void OnPaint()
		{
			Paint.SetDefaultFont( 9 );
			Paint.SetPen( Theme.TextLight );
			var rect = LocalRect.Shrink( 12, 0 );
			Paint.DrawText( rect, "Asset", TextFlag.LeftCenter );
			rect.Right -= 38;
			Paint.DrawText( rect, excluded ? "Additional size" : "Size", TextFlag.RightCenter );
			if ( !excluded )
			{
				rect.Left = LocalRect.Width * 0.52f;
				Paint.DrawText( rect, "Included through", TextFlag.LeftCenter );
			}
		}
	}

	// Keep the inclusion source in a stable column, with full reference chains in the tooltip.
	sealed class SceneColumns : Widget
	{
		public SceneColumns( Widget parent ) : base( parent ) => FixedHeight = 26;
		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Text.WithAlpha( 0.035f ) );
			Paint.DrawRect( LocalRect );
			Paint.SetDefaultFont( 9 );
			Paint.SetPen( Theme.TextLight );
			var rect = LocalRect.Shrink( 12, 0 );
			Paint.DrawText( rect, "Scene", TextFlag.LeftCenter );
			rect.Left = LocalRect.Width * 0.55f;
			Paint.DrawText( rect, "Included through", TextFlag.LeftCenter );
		}
	}

	sealed class SceneList : ListView
	{
		public SceneList( Widget parent ) : base( parent )
		{
			ItemSize = new Vector2( 0, 38 );
			ItemSpacing = 0;
			Margin = new Margin( 0, 2, 8, 2 );
		}

		protected override void PaintItem( VirtualWidget item )
		{
			var file = (FileEntry)item.Object;
			Paint.SetPen( Theme.Text.WithAlpha( 0.07f ) );
			Paint.DrawLine( item.Rect.BottomLeft, item.Rect.BottomRight );
			var rect = item.Rect.Shrink( 12, 0 );
			var icon = rect;
			icon.Width = 20;
			DrawAssetIcon( icon, file.Path );
			rect.Left += 32;
			rect.Right = item.Rect.Left + item.Rect.Width * 0.55f - 16;
			Paint.SetDefaultFont( 10, 500 );
			Paint.SetPen( Theme.Text );
			Paint.DrawText( rect, Paint.GetElidedText( file.Path, Math.Max( 0, rect.Width ) ), TextFlag.LeftCenter );

			var reason = file.Note?.Split( '\n' )[0] ?? "Included content";
			var configured = reason is "Startup scene" or "System scene" or "Map startup scene" or "Dedicated server startup scene";
			if ( !configured ) reason = InclusionSource( file );
			Paint.SetDefaultFont( 9 );
			var reasonRect = item.Rect;
			reasonRect.Left = item.Rect.Left + item.Rect.Width * 0.55f;
			reasonRect.Right -= 40;
			Paint.SetPen( Theme.TextLight );
			Paint.DrawText( reasonRect, Paint.GetElidedText( reason, Math.Max( 0, reasonRect.Width ) ), TextFlag.LeftCenter | TextFlag.SingleLine );
			icon = item.Rect.Shrink( 12, 0 );
			icon.Left = icon.Right - 20;
			Paint.SetPen( Theme.TextLight );
			Paint.DrawIcon( icon, configured ? "lock" : "info_outline", 16 );
		}

		protected override string GetTooltip( object item )
		{
			var file = (FileEntry)item;
			return WebUtility.HtmlEncode( $"{file.Path}\n{file.Note}\n{file.Size.FormatBytes()}\n{DependencyText( file.Dependencies )}\nConfigured scenes are managed in Game Setup. Other inclusion rules are available under Edit rules." );
		}
	}

	const string DependencyTooltip = "Sort by most unique transitive assets, including dependencies already published. Selected roots, raw source inputs and duplicate paths are not counted. Groups count shared dependencies once. The preview notice lists incomplete metadata; affected counts are lower bounds. Each asset appears once; sizes include its published files.";
	static string DependencyText( DependencySummary summary ) => summary is null ? "Transitive dependencies unknown" : $"{(summary.Incomplete ? "At least " : "")}{summary.Count:n0} transitive dependencies";

	const string ImpactTooltip = "Estimated additional uncompressed size from existing compiled files and references. Already included files are omitted; shared dependencies count once per row or group. No excluded assets are compiled. The preview notice lists paths with incomplete metadata. Values show the known amounts. Recompilation and generated thumbnails can change the final size.";
	static string ImpactSize( PublishImpact impact ) => impact.TotalBytes switch
	{
		0 => "No added size",
		{ } bytes => bytes.FormatBytes(),
		_ => impact.DirectBytes + impact.TransitiveBytes > 0 ? $"~{(impact.DirectBytes + impact.TransitiveBytes).FormatBytes()}" : "Unavailable"
	};

	static string DirectSize( PublishImpact impact ) => impact.DirectUnknown && impact.DirectBytes == 0 ? "Unavailable" : $"{(impact.DirectUnknown ? "~" : "")}{impact.DirectBytes.FormatBytes()}";
	static string TransitiveSize( PublishImpact impact ) => impact.TransitiveUnknown && impact.TransitiveBytes == 0 ? "(transitive size unavailable)" : $"(+ {(impact.TransitiveUnknown ? "~" : "")}{impact.TransitiveBytes.FormatBytes()} transitive)";

	sealed class SizeDisplay : Widget
	{
		readonly string size;
		public SizeDisplay( Widget parent, string size, string caption, string details = null ) : base( parent )
		{
			this.size = size;
			FixedSize = new Vector2( 180, 28 );
			ToolTip = $"{caption}\n{size}\n{details}";
		}
		protected override void OnPaint()
		{
			Paint.SetDefaultFont( 11, 500 );
			Paint.SetPen( Theme.TextLight );
			Paint.DrawText( LocalRect, size, TextFlag.RightCenter | TextFlag.SingleLine );
		}
	}

	static FileGroup[] GroupFiles( IEnumerable<FileEntry> files )
	{
		static string Extension( string path )
		{
			var extension = Path.GetExtension( path ).TrimStart( '.' ).ToLowerInvariant();
			return extension.EndsWith( "_c" ) ? extension[..^2] : extension;
		}

		var types = new Dictionary<string, AssetType>( StringComparer.OrdinalIgnoreCase );
		foreach ( var type in AssetType.All )
			foreach ( var extension in type.FileExtensions.Append( type.FileExtension ) )
				if ( !string.IsNullOrWhiteSpace( extension ) ) types.TryAdd( extension.TrimStart( '.' ), type );

		return files.GroupBy( file =>
		{
			var extension = Extension( file.Path );
			return types.TryGetValue( extension, out var type ) ? $"asset:{type.FileExtension}" : $"file:{extension}";
		} ).Select( group =>
		{
			var entries = group.ToArray();
			var extension = Extension( entries[0].Path );
			var type = types.GetValueOrDefault( extension );
			var title = type?.FriendlyName;
			if ( string.IsNullOrWhiteSpace( title ) ) title = extension.Length > 0 ? $"{extension.ToUpperInvariant()} files" : "Files without an extension";
			var extensions = type is not null ? type.FileExtensions.Append( type.FileExtension ) : [extension];
			var rules = extensions.Select( x => x.TrimStart( '.' ).ToLowerInvariant() ).Where( x => x.Length > 0 )
				.Select( x => $"*.{x}" ).Distinct().Order().ToArray();
			return new FileGroup( group.Key, title, rules, entries, type );
		} ).ToArray();
	}

	/// <summary>Virtual rows keep large manifests cheap, including type headers and their buttons.</summary>
	sealed class FilePathList : ListView
	{
		readonly Action<string[]> include;
		readonly bool details;
		readonly bool showInclusion;
		public Action<string> ToggleGroup { get; set; }
		public Func<string, bool> IsAdded { get; set; }
		public Func<string[], bool> IsSelectionAdded { get; set; }
		public Func<IEnumerable<string>, PublishImpact> GetFootprint { get; set; }
		public Action<string[]> Exclude { get; set; }
		bool WasAdded( object item ) => item is FileGroupHeader header ? IsSelectionAdded?.Invoke( header.Group.Rules ) == true : IsAdded?.Invoke( ((FileEntry)item).Path ) == true;

		public FilePathList( Widget parent, Action<string[]> include, bool details = false, bool showInclusion = false ) : base( parent )
		{
			this.include = include;
			this.details = details;
			this.showInclusion = showInclusion;
			ItemSize = new Vector2( 0, details ? 40 : 32 );
			ItemSpacing = 0;
			Margin = new Margin( 0, 2, 8, 2 );
		}

		bool HasInclude( object item ) => include is not null && (!showInclusion || WasAdded( item ))
			&& (item is not FileGroupHeader header || header.Group.Rules.Length > 0);
		bool HasExclude( object item ) => Exclude is not null && !HasInclude( item ) && (item is FileGroupHeader header
			? header.Matches.Length > 0 && header.Matches.All( file => file.CanExclude ) : ((FileEntry)item).CanExclude);

		static Rect IncludeRect( VirtualWidget item )
		{
			var rect = item.Rect.Shrink( 2 );
			rect.Left = rect.Right - 22;
			return rect;
		}

		protected override void PaintItem( VirtualWidget item )
		{
			var header = item.Object as FileGroupHeader;
			var file = item.Object as FileEntry;
			var rect = item.Rect.Shrink( 6, 0 );
			if ( header is not null )
			{
				Paint.SetPen( Theme.Text.WithAlpha( 0.06f ) );
				Paint.SetBrush( Theme.ControlBackground.WithAlpha( item.Hovered ? 0.6f : 0.3f ) );
				Paint.DrawRect( item.Rect.Shrink( 0, 1 ), 3 );
				var chevron = rect;
				chevron.Width = 16;
				Paint.SetPen( Theme.TextLight );
				Paint.DrawIcon( chevron, header.Collapsed ? "chevron_right" : "expand_more", 16 );
				rect.Left += 22;
				var folder = rect;
				folder.Width = 18;
				DrawAssetIcon( folder, header.Group.Entries[0].Path, header.Group.Type );
				rect.Left += 26;
			}
			else
			{
				// Child paths sit one level inside the group title, not to its left.
				rect.Left += details ? 10 : 32;
				if ( !details )
				{
					var icon = rect;
					icon.Width = 18;
					DrawAssetIcon( icon, file.Path );
					rect.Left += 26;
				}
			}

			if ( include is not null ) rect.Right = IncludeRect( item ).Left - 6;
			if ( HasInclude( item.Object ) || HasExclude( item.Object ) )
			{
				var button = IncludeRect( item );
				PublishButton.DrawBackground( button, item.Hovered && button.IsInside( FromScreen( Application.CursorPosition ) ) );
				var added = WasAdded( item.Object );
				var excluding = HasExclude( item.Object );
				Paint.SetPen( excluding ? Theme.TextLight : added || item.Hovered ? Theme.Green : Theme.TextLight );
				Paint.DrawIcon( button, excluding ? "remove" : added ? "check" : "add", 16 );
			}

			var impact = header is not null ? header.Impact : file.Impact;
			var sizeText = impact is not null ? ImpactSize( impact )
				: (header?.Size ?? file.Size).FormatBytes();
			var sizeRect = rect;
			var sizeWidth = 100;
			sizeRect.Left = Math.Max( sizeRect.Left, sizeRect.Right - sizeWidth );
			if ( details ) sizeRect.Height = 22;
			Paint.SetDefaultFont( 10 );
			Paint.SetPen( Theme.TextLight );
			Paint.DrawText( sizeRect, Paint.GetElidedText( sizeText, sizeRect.Width ), TextFlag.RightCenter | TextFlag.SingleLine );
			rect.Right = sizeRect.Left - 8;
			if ( showInclusion )
			{
				var reasonRect = rect;
				reasonRect.Left = item.Rect.Left + item.Rect.Width * 0.52f;
				Paint.SetDefaultFont( 9 );
				Paint.SetPen( Theme.TextLight );
				var reason = header is not null ? GroupInclusionSource( header ) : InclusionSource( file );
				Paint.DrawText( reasonRect, Paint.GetElidedText( reason, Math.Max( 0, reasonRect.Width ) ), TextFlag.LeftCenter | TextFlag.SingleLine );
				rect.Right = reasonRect.Left - 12;
			}

			Paint.SetDefaultFont( 10, weight: header is not null ? 500 : 400 );
			Paint.SetPen( item.Selected ? Theme.Primary : header is not null ? Theme.Text : Theme.TextControl );
			if ( details ) rect.Height = 22;
			if ( header is not null )
			{
				var count = header.Matches.Length == header.Group.Entries.Length
					? $"({header.Matches.Length:n0})" : $"({header.Matches.Length:n0} of {header.Group.Entries.Length:n0})";
				var titleRect = rect;
				titleRect.Width = Math.Max( 0, rect.Width - Paint.MeasureText( count ).x - 6 );
				var title = Paint.GetElidedText( header.Group.Title, titleRect.Width );
				Paint.DrawText( titleRect, title, TextFlag.LeftCenter | TextFlag.SingleLine );
				rect.Left += Paint.MeasureText( title ).x + 6;
				Paint.SetPen( Theme.TextLight );
				Paint.DrawText( rect, Paint.GetElidedText( count, Math.Max( 0, rect.Width ) ), TextFlag.LeftCenter | TextFlag.SingleLine );
			}
			else
			{
				Paint.DrawText( rect, Paint.GetElidedText( file.Path, Math.Max( 0, rect.Width ) ), TextFlag.LeftCenter | TextFlag.SingleLine );
			}
			if ( details && file?.Note is { } note )
			{
				var noteRect = item.Rect.Shrink( 16, 0, 6, 0 );
				noteRect.Top += 22;
				noteRect.Height = 16;
				Paint.SetDefaultFont( 9 );
				Paint.SetPen( Theme.TextLight );
				Paint.DrawText( noteRect, Paint.GetElidedText( note.Split( '\n' )[0], noteRect.Width ), TextFlag.LeftCenter | TextFlag.SingleLine );
			}
		}

		protected override bool OnItemPressed( VirtualWidget item, MouseEvent e )
		{
			if ( e.LeftMouseButton && HasExclude( item.Object ) && IncludeRect( item ).IsInside( e.LocalPosition ) )
			{
				e.Accepted = true;
				// Exact paths avoid excluding future files or items hidden by the filter.
				Exclude( item.Object is FileGroupHeader header ? header.Matches.Select( file => file.Path ).ToArray() : [((FileEntry)item.Object).Path] );
				return false;
			}
			if ( e.LeftMouseButton && HasInclude( item.Object ) && IncludeRect( item ).IsInside( e.LocalPosition ) )
			{
				e.Accepted = true;
				if ( item.Object is FileGroupHeader header ) include( header.Group.Rules );
				else include( [((FileEntry)item.Object).Path] );
				return false;
			}
			if ( e.LeftMouseButton && item.Object is FileGroupHeader group )
			{
				e.Accepted = true;
				ToggleGroup?.Invoke( group.Group.Key );
				return false;
			}
			return item.Object is not FileGroupHeader && base.OnItemPressed( item, e );
		}

		protected override string GetTooltip( object item )
		{
			if ( item is FileGroupHeader header )
			{
				var footprint = GetFootprint?.Invoke( header.Matches.SelectMany( x => x.Paths ) ) ?? header.Impact;
				var size = footprint is null ? header.Size.FormatBytes() : $"{DirectSize( footprint )} direct {TransitiveSize( footprint )}";
				var action = HasExclude( item ) ? $"\nClick − to exclude these {header.Matches.Length:n0} files. Only the files matching the current filter are affected."
					: !HasInclude( item ) ? "" : WasAdded( item ) ? "\nClick the checkmark to undo this addition; pre-existing rules are kept."
					: $"\nClick + to include all {header.Group.Title}: {string.Join( ", ", header.Group.Rules )}\nApplies to the whole type, including files hidden by the filter and future matches.";
				return WebUtility.HtmlEncode( $"{header.Group.Title}\nClick to expand or collapse.{action}\n{size}\n{DependencyText( header.Dependencies )}{FootprintNote}" );
			}
			var file = (FileEntry)item;
			var fileFootprint = GetFootprint?.Invoke( file.Paths ) ?? file.Impact;
			var fileSize = fileFootprint is not null ? $"{DirectSize( fileFootprint )} direct {TransitiveSize( fileFootprint )}"
				: file.Size.FormatBytes();
			return WebUtility.HtmlEncode( $"{file.Path}\n{file.Note}\n{fileSize}\n{DependencyText( file.Dependencies )}{FootprintNote}" + (HasExclude( item ) ? "\nClick − to add an exclusion for this file. Its include rules are kept." : !HasInclude( item ) ? "" : WasAdded( item )
				? "\nClick the checkmark to undo this addition; pre-existing rules are kept." : "\nClick + to include this file and its dependencies.") );
		}

		string FootprintNote => GetFootprint is null ? "" : "\nIncludes dependencies already in this package; shared files count once.";
	}

	sealed record WarningEntry( string Title, string Details, string IncludeRule, int Priority, PublishImpact Impact, DependencySummary Dependencies, CodeAssetWarning Source = null,
		bool IsGroup = false, string ExpansionKey = null, bool IsAsset = false, int Depth = 0, bool Included = false )
	{
		public string DisclosureKey => ExpansionKey ?? GroupKey;
		public string GroupKey => Source?.Usage?.StartsWith( "GetAll<", StringComparison.Ordinal ) == true
			? $"query:{Source.SourceFile}\n{Source.Line}\n{Source.Usage}" : null;
	}

	static string WarningTitle( CodeAssetWarning warning )
	{
		var path = ReviewAssetPath( warning.Pattern );
		if ( path == "*.scene" && warning.Usage?.StartsWith( "GetAll<", StringComparison.Ordinal ) == true )
			return $"{warning.ExcludedPaths.Length:n0} scenes may be loaded at runtime";
		if ( warning.Usage?.StartsWith( "GetAll<", StringComparison.Ordinal ) == true )
		{
			// Queries use the asset type's canonical extension, which can differ from source files (JPG/PNG).
			var type = AssetType.FromExtension( Path.GetExtension( warning.Pattern ).TrimStart( '.' ) );
			return type is not null ? $"Resource query may load {type.FriendlyName} assets" : $"Resource query may load {path}";
		}
		return $"Code references: {path}";
	}

	static string WarningReason( CodeAssetWarning warning ) => warning.ExclusionRule is not null ? $"Excluded by rule: {warning.ExclusionRule}. Review before adding."
		: warning.Recommended ? "This path appears in your game code but is not included."
		: warning.Usage is not null ? "Expand to choose which matching files should ship with your game."
		: "This runtime path may need an include rule.";

	/// <summary>Review rows show a short explanation; code and dependency details stay in tooltips.</summary>
	sealed class WarningList : ListView
	{
		readonly Action<string> include;
		public int? RestoreScroll { get; set; }
		public Action<string> ToggleGroup { get; set; }
		public Func<string, bool> IsGroupExpanded { get; set; }
		public Func<string, bool> IsAdded { get; set; }
		public WarningList( Widget parent, Action<string> include ) : base( parent )
		{
			this.include = include;
			ItemSize = new Vector2( 0, 52 );
			ItemSpacing = 0;
			Margin = new Margin( 0, 2, 8, 2 );
		}
		protected override void Rebuild()
		{
			base.Rebuild();
			if ( RestoreScroll is not { } position ) return;
			RestoreScroll = null;
			VerticalScrollbar.Value = position;
			base.Rebuild();
		}
		static Rect IncludeRect( VirtualWidget item )
		{
			var rect = item.Rect.Shrink( 4, 11 );
			rect.Left = rect.Right - 106;
			return rect;
		}
		static Rect SourceRect( VirtualWidget item )
		{
			var rect = IncludeRect( item );
			if ( ((WarningEntry)item.Object).IncludeRule is not null ) rect.Right = rect.Left - 6;
			rect.Left = rect.Right - 116;
			return rect;
		}
		protected override void PaintItem( VirtualWidget item )
		{
			var entry = (WarningEntry)item.Object;
			var added = entry.IncludeRule is not null && IsAdded?.Invoke( entry.IncludeRule ) == true;
			var button = IncludeRect( item );
			var sourceButton = SourceRect( item );
			var mouse = FromScreen( Application.CursorPosition );
			Paint.SetPen( Theme.Text.WithAlpha( 0.07f ) );
			Paint.DrawLine( item.Rect.BottomLeft, item.Rect.BottomRight );
			if ( entry.IncludeRule is not null )
			{
				PublishButton.DrawBackground( button, item.Hovered && button.IsInside( mouse ) );
				PublishButton.DrawAction( button, added ? "Added" : entry.Included ? "Included" : entry.IsGroup ? "Include all" : "Include", added || entry.Included ? "check" : "add", added ? Theme.Green : entry.Included ? Theme.TextLight : Theme.Text );
			}

			if ( entry.Source is not null )
			{
				PublishButton.DrawBackground( sourceButton, item.Hovered && sourceButton.IsInside( mouse ) );
				PublishButton.DrawAction( sourceButton, "View source", "code", Theme.Text );
			}

			var rect = item.Rect.Shrink( 6, 0 );
			rect.Left += entry.Depth * 22;
			var icon = rect;
			icon.Width = 18;
			if ( entry.IsGroup )
			{
				Paint.SetPen( Theme.TextLight );
				Paint.DrawIcon( icon, IsGroupExpanded?.Invoke( entry.DisclosureKey ) == true ? "expand_more" : "chevron_right", 14 );
			}
			// Reserve the disclosure column on every row so warning icons and text line up.
			icon.Left += 24;
			icon.Width = 18;
			rect.Left += 24;
			Paint.SetPen( added || entry.Included ? Theme.Green : entry.Priority == 0 ? Theme.Red : Theme.Yellow );
			if ( entry.IsAsset ) DrawAssetIcon( icon, entry.IncludeRule );
			else Paint.DrawIcon( icon, added || entry.Included ? "check_circle" : "warning", 18 );
			rect.Left += 28;
			rect.Right = (entry.Source is not null ? sourceButton.Left : button.Left) - 8;
			rect.Top += 6;
			rect.Height = 20;
			Paint.SetDefaultFont( 10, weight: entry.Depth > 0 ? 400 : 600 );
			Paint.SetPen( entry.Priority == 0 ? Theme.Red : Theme.Text );
			Paint.DrawText( rect, Paint.GetElidedText( entry.Title, Math.Max( 0, rect.Width ) ), TextFlag.LeftCenter | TextFlag.SingleLine );
			rect.Top += 22;
			rect.Height = 18;
			Paint.SetDefaultFont( 9 );
			Paint.SetPen( Theme.TextLight );
			var details = added ? "Added to publishing. Click Added to undo." : entry.Included ? "Included through another rule or dependency." : entry.Details;
			Paint.DrawText( rect, Paint.GetElidedText( details, Math.Max( 0, rect.Width ) ), TextFlag.LeftCenter | TextFlag.SingleLine );
		}
		protected override void OnMouseMove( MouseEvent e )
		{
			base.OnMouseMove( e );
			// The base list only repaints when moving between rows, not between row actions.
			Update();
		}
		protected override bool OnItemPressed( VirtualWidget item, MouseEvent e )
		{
			if ( e.LeftMouseButton && ((WarningEntry)item.Object).Source is { } source && SourceRect( item ).IsInside( e.LocalPosition ) )
			{
				e.Accepted = true;
				CodeEditor.OpenFile( source.SourceFile, source.Line );
				return false;
			}
			if ( e.LeftMouseButton && ((WarningEntry)item.Object).IncludeRule is { } rule && IncludeRect( item ).IsInside( e.LocalPosition ) )
			{
				e.Accepted = true;
				if ( IsAdded?.Invoke( rule ) == true || !((WarningEntry)item.Object).Included ) include( rule );
				return false;
			}
			if ( e.LeftMouseButton && ((WarningEntry)item.Object) is { IsGroup: true } group )
			{
				e.Accepted = true;
				ToggleGroup?.Invoke( group.DisclosureKey );
				return false;
			}
			return base.OnItemPressed( item, e );
		}
		protected override string GetTooltip( object item )
		{
			var entry = (WarningEntry)item;
			var details = $"<b>{WebUtility.HtmlEncode( entry.Title )}</b><br>{WebUtility.HtmlEncode( entry.Details )}";
			if ( entry.Dependencies is not null ) details += $"<br>{WebUtility.HtmlEncode( DependencyText( entry.Dependencies ) )}";
			if ( entry.Impact is not null ) details += $"<br>{WebUtility.HtmlEncode( $"{DirectSize( entry.Impact )} {TransitiveSize( entry.Impact )}" )}";
			if ( entry.IncludeRule is not null ) details += IsAdded?.Invoke( entry.IncludeRule ) == true
				? "<br>Click Added to undo this addition. Pre-existing rules are kept."
				: entry.Included ? "<br>Included through another rule or dependency." : "<br>Use Include to add this content and its dependencies.";
			if ( entry.Source is { } source )
			{
				if ( source.Usage is not null ) details += $"<br>{WebUtility.HtmlEncode( source.Usage )}";
				details += $"<br><br>{WebUtility.HtmlEncode( Path.GetFileName( source.SourceFile ) )}:{source.Line}";
				if ( !string.IsNullOrWhiteSpace( source.SourceLine ) )
					details += $"<pre>{WebUtility.HtmlEncode( source.SourceLine )}</pre>";
				details += $"<br>View source opens this reference in {WebUtility.HtmlEncode( CodeEditor.Title )}.";
			}
			return $"<qt>{details}</qt>";
		}
	}

}
