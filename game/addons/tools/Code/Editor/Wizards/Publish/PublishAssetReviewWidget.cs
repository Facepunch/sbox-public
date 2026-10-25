using System.IO;
using System.Net;
using System.Threading;
using static Editor.ProjectPublisher.PackageManifest;

namespace Editor;

/// <summary>The same publish preview and rule suggestions in project settings and the publish wizard.</summary>
internal sealed class PublishAssetReviewWidget : Widget
{
	readonly Func<CancellationToken, Task<ProjectPublisher.PackageManifest>> buildManifest;
	readonly Action<string[]> addRules;
	readonly SemaphoreSlim buildGate = new( 1, 1 );
	readonly PreviewStatus status;
	readonly Label.Small estimateNotice;
	readonly Widget content;
	CancellationTokenSource refreshSource;
	int generation;
	bool refreshing;
	bool stale;
	string error;
	string includedFilter = "";
	string excludedFilter = "";
	int includedSort = 3;
	int excludedSort = 3;
	int warningSort;
	readonly HashSet<string> expandedWarningGroups = new();
	bool addingFromPreview;
	readonly HashSet<string> addedRules = new( StringComparer.OrdinalIgnoreCase );
	readonly List<Widget> actionLists = new();
	readonly HashSet<string> expandedIncludedGroups = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> expandedExcludedGroups = new( StringComparer.OrdinalIgnoreCase );

	public ProjectPublisher.PackageManifest Manifest { get; private set; }
	public Action PreviewChanged { get; set; }
	public bool IsCurrent => !refreshing && !stale && Manifest is not null;

	public PublishAssetReviewWidget( Widget parent, Func<CancellationToken, Task<ProjectPublisher.PackageManifest>> buildManifest, Action<string[]> addRules ) : base( parent )
	{
		this.buildManifest = buildManifest;
		this.addRules = addRules;
		Layout = Layout.Column();
		Layout.Spacing = 10;
		var header = Layout.AddRow();
		header.Add( new Label.Subtitle( "Publish Preview" ) { Color = Theme.Text }, 1 );
		estimateNotice = header.Add( new Label.Small( "Estimates incomplete" ) { Visible = false, Color = Theme.TextLight, WordWrap = false } );
		status = header.Add( new PreviewStatus( this ) );
		content = Layout.Add( new Widget( this ) { Layout = Layout.Column(), MinimumHeight = 240 }, 1 );
	}

	/// <summary>Refresh after typing pauses, cancelling any older preview.</summary>
	public void InvalidatePreview()
	{
		// Apply rule edits immediately, but batch the expensive analysis after a burst of clicks.
		if ( !addingFromPreview ) addedRules.Clear();
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
		PreviewChanged?.Invoke();
		error = null;
		content.Enabled = Manifest is not null;
		estimateNotice.Visible = false;
		status.Busy = true;
		status.Text = "Updating preview…";
		status.Update();
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
				addedRules.Clear();
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
				PreviewChanged?.Invoke();
				Rebuild();
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
		PreviewChanged?.Invoke();
	}

	public override void OnDestroyed()
	{
		generation++;
		refreshSource?.Cancel();
		base.OnDestroyed();
	}

	public void Rebuild()
	{
		status.Busy = refreshing;
		status.Text = refreshing ? "Updating preview…" : IsCurrent ? "Up to date" : "Preview unavailable";
		status.Update();
		if ( refreshing ) return;

		actionLists.Clear();
		content.Layout.Clear( true );
		content.Layout.Spacing = 12;
		content.Enabled = true;
		if ( error is not null )
		{
			content.Layout.Add( new WarningBox( WebUtility.HtmlEncode( error ), content ) );
			content.Layout.Add( new Button( "Retry", "refresh" ) ).Clicked = () => _ = RefreshAsync();
		}
		if ( Manifest is null ) return;

		var manifest = Manifest;
		var estimateIssues = manifest.GetEstimateIssues();
		estimateNotice.Visible = estimateIssues.Count > 0;
		estimateNotice.Text = $"Estimates incomplete · {estimateIssues.Count:n0} paths";
		estimateNotice.ToolTip = WebUtility.HtmlEncode( "Sizes and dependency counts show the known amounts; some content could not be measured. Excluded assets are not compiled for the preview.\n"
			+ string.Join( "\n", estimateIssues.Take( 16 ).Select( x => $"{x.Path}: {x.Reason}" ) )
			+ (estimateIssues.Count > 16 ? $"\n… and {estimateIssues.Count - 16:n0} more paths." : "") );
		var files = manifest.Assets.ToArray();
		if ( manifest.ExclusionOverrides.Count > 0 )
		{
			var kept = content.Layout.Add( new PublishSection( content, $"Required despite exclusions · {manifest.ExclusionOverrides.Count:n0}", Theme.Yellow, "info" ) );
			kept.Layout.Add( new Label.Body( "These files still ship because they are configured, in Resources, or required by included content." ) );
			var list = kept.Layout.Add( new FilePathList( kept, null, details: true ) { FixedHeight = Math.Min( manifest.ExclusionOverrides.Count, 3 ) * 40 + 4 } );
			list.SetItems( manifest.ExclusionOverrides.Select( x => new FileEntry( x.Path, x.Size, Note: $"{x.Rule} overridden: {x.Reason}" ) ) );
		}

		foreach ( var message in manifest.Errors )
			content.Layout.Add( new WarningBox( WebUtility.HtmlEncode( message ), content ) );

		var warningEntries = manifest.SceneWarnings.Select( scene => new WarningEntry(
			$"{(scene.Missing ? "Missing scene" : "Excluded scene")}: {scene.Path}",
			scene.Missing ? "Update Game Setup or restore this configured scene." : "Include this scene if your game loads it. Leave development-only scenes excluded.",
			scene.Missing ? null : scene.Path, scene.Missing ? 0 : 1,
			scene.Missing ? null : manifest.GetPublishImpact( [scene.Path] ),
			scene.Missing ? null : manifest.GetDependencySummary( [scene.Path] ) ) )
			.Concat( manifest.CodeAssetWarnings.Select( warning => new WarningEntry(
				WarningTitle( warning ), $"{WarningReason( warning )} · {warning.ExcludedPaths.Length:n0} excluded matches",
				warning.Pattern, 2, manifest.GetPublishImpact( warning.ExcludedPaths ), manifest.GetDependencySummary( warning.ExcludedPaths ), warning ) ) ).ToArray();
		var warningCount = warningEntries.Select( x => x.GroupKey ?? $"entry:{x.Title}" ).Distinct().Count();
		var hasMissingScenes = manifest.SceneWarnings.Any( x => x.Missing );
		var warnings = content.Layout.Add( new PublishSection( content, $"Warnings · {warningCount:n0}",
			hasMissingScenes ? Theme.Red : warningEntries.Length > 0 ? Theme.Yellow : Theme.TextLight, null ) );
		if ( warningEntries.Length == 0 )
		{
			warnings.Layout.Add( new Label.Body( "No scene or code-reference warnings. Dynamic loads may still need include rules." ) );
		}
		else
		{
			warnings.ToolTip = "Expand groups to review individual scenes and resource types. Hover rows for details.";
			// Missing configured scenes stay visible; optional scene and query groups start collapsed.
			var list = warnings.Layout.Add( new WarningList( warnings, Include ) { IsAdded = IsAdded } );
			actionLists.Add( list );
			void PopulateWarnings()
			{
				var rows = warningEntries.GroupBy( x => x.Priority ).OrderBy( x => x.Key )
					.SelectMany( group => SortItems( group, warningSort, x => x.Title, x => x.Impact?.TotalBytes, x => x.Dependencies ) ).ToArray();
				var visible = new List<WarningEntry>();
				foreach ( var group in rows.GroupBy( x => x.GroupKey ?? $"entry:{x.Title}" ) )
				{
					var first = group.First();
					var grouped = first.GroupKey is not null && (first.Priority == 1 || group.Count() > 1);
					if ( grouped )
					{
						visible.Add( first with
						{
							Title = first.Priority == 1 ? $"Excluded scenes · {group.Count():n0}" : $"{first.Source.Usage} · {group.Count():n0} asset types",
							Details = first.Priority == 1 ? "Include scenes your game loads; leave development-only scenes excluded." : $"One resource query may cover these patterns: {string.Join( ", ", group.Select( x => x.IncludeRule ) )}. Review runtime filters before including them.",
							IncludeRule = null,
							Impact = null,
							Dependencies = null,
							IsGroup = true
						} );
						if ( !expandedWarningGroups.Contains( first.GroupKey ) ) continue;
					}
					visible.AddRange( group.Select( x => x with { IsGroupChild = grouped } ) );
				}
				list.SetItems( visible );
				list.FixedHeight = Math.Min( visible.Count, 6 ) * 28 + 4;
				list.Visible = visible.Count > 0;
			}
			list.IsGroupExpanded = expandedWarningGroups.Contains;
			list.ToggleGroup = key =>
			{
				if ( !expandedWarningGroups.Add( key ) ) expandedWarningGroups.Remove( key );
				PopulateWarnings();
			};
			warnings.Header.Add( SortControl( warnings, warningSort, sort => { warningSort = sort; PopulateWarnings(); } ) );
			PopulateWarnings();
		}

		var columns = content.Layout.AddRow( 1 );
		columns.Spacing = 12;
		columns.Add( FileList( $"Included · {files.Length:n0}", Theme.Green, "check_circle",
			"Publish size · uncompressed", files.Select( x => new FileEntry( x.Name, x.Size, Dependencies: manifest.GetDependencySummary( [x.Name] ), Note: manifest.IncludedExclusionReasons.GetValueOrDefault( x.Name.TrimStart( '/' ) ) ) ), false ), 1 );
		columns.Add( FileList( $"Excluded · {manifest.ExcludedAssets.Count:n0}", Theme.TextLight, "remove_circle_outline",
			"Estimated additional publish size", manifest.ExcludedAssets.Select( x => new FileEntry( x, Impact: manifest.GetPublishImpact( [x] ), Dependencies: manifest.GetDependencySummary( [x] ), Note: manifest.ExcludedByRules.TryGetValue( x, out var rule ) ? $"Excluded by {rule}. Click + to add an inclusion after it." : null ) ), true ), 1 );
	}

	Widget FileList( string title, Color accent, string icon, string description, IEnumerable<FileEntry> paths, bool excluded )
	{
		var page = new PublishSection( content, title, accent, icon ) { MinimumHeight = 240 };
		var entries = paths.ToArray();
		var totalImpact = excluded ? Manifest.GetPublishImpact( entries.Select( x => x.Path ) ) : null;
		var bytes = totalImpact is null ? entries.Sum( x => x.Size ) : totalImpact.DirectBytes + totalImpact.TransitiveBytes;
		var sizeText = bytes == 0 && totalImpact is { TotalBytes: null } ? "Unavailable" : bytes.FormatBytes();
		page.Header.Add( new SizeDisplay( page, sizeText, description, accent,
			totalImpact is null ? null : $"{DirectSize( totalImpact )} direct {TransitiveSize( totalImpact )}\n{ImpactTooltip}" ) );
		var filter = page.Layout.Add( new LineEdit( page ) { Text = excluded ? excludedFilter : includedFilter } );
		filter.PlaceholderText = "Filter paths…";
		var sortRow = page.Layout.AddRow();
		var list = page.Layout.Add( new FilePathList( page, excluded ? Include : null ), 1 );
		list.IsAdded = IsAdded;
		if ( excluded ) actionLists.Add( list );
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
				return new FileGroupHeader( group, matches, excluded ? Manifest.GetPublishImpact( matches.Select( x => x.Path ) ) : null, Manifest.GetDependencySummary( matches.Select( x => x.Path ) ), !expanded.Contains( group.Key ) );
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
		return page;
	}

	static Widget SortControl( Widget parent, int current, Action<int> changed )
	{
		var control = new Widget( parent ) { Layout = Layout.Row() };
		control.Layout.Spacing = 2;
		var buttons = new List<Button>();
		var labels = new[] { "Name", "Largest", "Smallest", "Most deps" };
		var icons = new[] { "sort_by_alpha", "arrow_downward", "arrow_upward", "account_tree" };
		for ( var i = 0; i < labels.Length; i++ )
		{
			var index = i;
			var button = control.Layout.Add( new Button( labels[i], icons[i] ) );
			buttons.Add( button );
			button.ToolTip = i == 3 ? DependencyTooltip : i == 0 ? "Sort by name" : "Sort by added size, including transitive dependencies for excluded files and warnings. Groups count shared files once; unknown sizes appear last.";
			button.Tint = i == current ? Theme.Primary : Theme.ButtonBackground;
			button.Clicked = () =>
			{
				for ( var j = 0; j < buttons.Count; j++ ) buttons[j].Tint = j == index ? Theme.Primary : Theme.ButtonBackground;
				changed( index );
			};
		}
		return control;
	}

	static IOrderedEnumerable<T> SortItems<T>( IEnumerable<T> items, int sort, Func<T, string> name, Func<T, long?> size, Func<T, DependencySummary> dependencies )
	{
		if ( sort == 0 ) return items.OrderBy( name, StringComparer.OrdinalIgnoreCase );
		if ( sort == 3 ) return items.OrderByDescending( x => dependencies( x )?.Count ?? -1 ).ThenBy( name, StringComparer.OrdinalIgnoreCase );
		var known = items.OrderBy( x => size( x ) is null );
		return (sort == 1 ? known.ThenByDescending( size ) : known.ThenBy( size )).ThenBy( name, StringComparer.OrdinalIgnoreCase );
	}

	void Include( string pattern ) => Include( [pattern] );

	bool IsAdded( string pattern ) => addedRules.Contains( pattern.Replace( '\\', '/' ).TrimStart( '/' ) );

	void Include( string[] patterns )
	{
		if ( Manifest is null ) return;
		var additions = patterns.Where( x => !IsAdded( x ) ).ToArray();
		if ( additions.Length == 0 ) return;
		var revision = generation;
		addingFromPreview = true;
		try
		{
			foreach ( var pattern in additions ) addedRules.Add( pattern.Replace( '\\', '/' ).TrimStart( '/' ) );
			foreach ( var list in actionLists ) list.Update();
			addRules( additions );
			if ( revision == generation ) InvalidatePreview();
		}
		catch ( Exception ex )
		{
			foreach ( var pattern in additions ) addedRules.Remove( pattern.Replace( '\\', '/' ).TrimStart( '/' ) );
			error = ex.Message;
			Rebuild();
		}
		finally
		{
			addingFromPreview = false;
		}
	}

	sealed record FileEntry( string Path, long Size = 0, PublishImpact Impact = null, string Note = null, DependencySummary Dependencies = null )
	{
		public long? SortSize => Impact is null ? Size : Impact.TotalBytes;
	}
	sealed record FileGroup( string Key, string Title, string[] Rules, FileEntry[] Entries );
	sealed record FileGroupHeader( FileGroup Group, FileEntry[] Matches, PublishImpact Impact = null, DependencySummary Dependencies = null, bool Collapsed = false )
	{
		public long Size { get; } = Matches.Sum( x => x.Size );
		public long? SortSize => Impact is null ? Size : Impact.TotalBytes;
	}

	const string DependencyTooltip = "Sort by most unique transitive assets, including dependencies already published. Selected roots, raw source inputs and duplicate paths are not counted. Groups count shared dependencies once. The preview notice lists incomplete metadata; affected counts are lower bounds. Source and compiled rows for the same asset share a count.";
	static string DependencyText( DependencySummary summary ) => summary is null ? "Dependencies unknown" : $"{summary.Count:n0} dependencies";

	const string ImpactTooltip = "Estimated additional uncompressed size from existing compiled files and references. Already included files are omitted; shared dependencies count once per row or group. No excluded assets are compiled. The preview notice lists paths with incomplete metadata. Values show the known amounts. Recompilation and generated thumbnails can change the final size.";
	static string DirectSize( PublishImpact impact ) => impact.DirectUnknown && impact.DirectBytes == 0 ? "Unavailable" : impact.DirectBytes.FormatBytes();
	static string TransitiveSize( PublishImpact impact ) => impact.TransitiveUnknown && impact.TransitiveBytes == 0 ? "(transitive size unavailable)" : $"(+ {impact.TransitiveBytes.FormatBytes()} transitive)";

	sealed class SizeDisplay : Widget
	{
		readonly string size;
		readonly Color accent;
		public SizeDisplay( Widget parent, string size, string caption, Color accent, string details = null ) : base( parent )
		{
			this.size = size;
			this.accent = accent;
			FixedSize = new Vector2( 180, 28 );
			ToolTip = $"{caption}\n{size}\n{details}";
		}
		protected override void OnPaint()
		{
			Paint.SetDefaultFont( 20, 600 );
			Paint.SetPen( accent );
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
			return new FileGroup( group.Key, title, rules, entries );
		} ).ToArray();
	}

	/// <summary>Virtual rows keep large manifests cheap, including type headers and their buttons.</summary>
	sealed class FilePathList : ListView
	{
		readonly Action<string[]> include;
		readonly bool details;
		public Action<string> ToggleGroup { get; set; }
		public Func<string, bool> IsAdded { get; set; }
		bool WasAdded( object item ) => item is FileGroupHeader header ? header.Group.Rules.All( x => IsAdded?.Invoke( x ) == true ) : IsAdded?.Invoke( ((FileEntry)item).Path ) == true;

		public FilePathList( Widget parent, Action<string[]> include, bool details = false ) : base( parent )
		{
			this.include = include;
			this.details = details;
			ItemSize = new Vector2( 0, details ? 40 : 28 );
			ItemSpacing = 0;
			Margin = new Margin( 0, 2, 8, 2 );
		}

		bool HasInclude( object item ) => item is FileGroupHeader header
			? include is not null && header.Group.Rules.Length > 0
			: include is not null;

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
				Paint.SetPen( Theme.Primary.WithAlpha( 0.16f ) );
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.10f ) );
				Paint.DrawRect( item.Rect.Shrink( 0, 1 ), 3 );
				var chevron = rect;
				chevron.Width = 16;
				Paint.SetPen( Theme.TextLight );
				Paint.DrawIcon( chevron, header.Collapsed ? "chevron_right" : "expand_more", 16 );
				rect.Left += 20;
			}
			else
			{
				// Child paths sit one level inside the group title, not to its left.
				rect.Left += details ? 10 : 32;
			}

			if ( HasInclude( item.Object ) )
			{
				var button = IncludeRect( item );
				rect.Right = button.Left - 6;
				Paint.ClearPen();
				Paint.SetBrush( item.Hovered ? Theme.ButtonBackground.Lighten( 0.15f ) : Theme.ButtonBackground );
				Paint.DrawRect( button, 3 );
				var added = WasAdded( item.Object );
				Paint.SetPen( added || item.Hovered ? Theme.Green : Theme.TextLight );
				Paint.DrawIcon( button, added ? "check" : "add", 16 );
			}

			var impact = header is not null ? header.Impact : file.Impact;
			var sizeText = impact is not null ? $"{DirectSize( impact )} {TransitiveSize( impact )}"
				: (header?.Size ?? file.Size).FormatBytes();
			var sizeRect = rect;
			var sizeWidth = impact is null ? 90 : Math.Min( 270, item.Rect.Width * 0.36f );
			sizeRect.Left = Math.Max( sizeRect.Left, sizeRect.Right - sizeWidth );
			if ( details ) sizeRect.Height = 22;
			Paint.SetDefaultFont( weight: header is not null ? 600 : 400 );
			Paint.SetPen( header is not null ? Theme.Text : Theme.TextLight );
			Paint.DrawText( sizeRect, Paint.GetElidedText( sizeText, sizeRect.Width ), TextFlag.RightCenter | TextFlag.SingleLine );
			rect.Right = sizeRect.Left - 8;

			if ( !details )
			{
				var summary = header is not null ? header.Dependencies : file.Dependencies;
				var countRect = rect;
				countRect.Left = Math.Max( countRect.Left, countRect.Right - 80 );
				Paint.SetDefaultFont( 9 );
				Paint.SetPen( Theme.TextLight );
				var count = summary is null || (summary.Count == 0 && summary.Incomplete) ? "— deps"
					: summary.Count == 0 ? "" : $"{summary.Count:n0} deps";
				Paint.DrawText( countRect, Paint.GetElidedText( count, countRect.Width ), TextFlag.RightCenter | TextFlag.SingleLine );
				rect.Right = countRect.Left - 8;
			}

			Paint.SetDefaultFont( weight: header is not null ? 600 : 400 );
			Paint.SetPen( item.Selected ? Theme.Primary : header is not null ? Theme.Text : Theme.TextControl );
			var text = header is not null
				? $"{header.Group.Title} · {(header.Matches.Length == header.Group.Entries.Length ? $"{header.Matches.Length:n0}" : $"{header.Matches.Length:n0} of {header.Group.Entries.Length:n0}")}"
				: file.Path;
			if ( details ) rect.Height = 22;
			Paint.DrawText( rect, Paint.GetElidedText( text, Math.Max( 0, rect.Width ) ), TextFlag.LeftCenter | TextFlag.SingleLine );
			if ( details && file?.Note is { } note )
			{
				var noteRect = item.Rect.Shrink( 16, 0, 6, 0 );
				noteRect.Top += 22;
				noteRect.Height = 16;
				Paint.SetDefaultFont( 9 );
				Paint.SetPen( Theme.TextLight );
				Paint.DrawText( noteRect, Paint.GetElidedText( note, noteRect.Width ), TextFlag.LeftCenter | TextFlag.SingleLine );
			}
		}

		protected override bool OnItemPressed( VirtualWidget item, MouseEvent e )
		{
			if ( e.LeftMouseButton && HasInclude( item.Object ) && IncludeRect( item ).IsInside( e.LocalPosition ) )
			{
				e.Accepted = true;
				if ( WasAdded( item.Object ) ) return false;
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
				var size = header.Impact is null ? header.Size.FormatBytes() : $"{DirectSize( header.Impact )} {TransitiveSize( header.Impact )}";
				var action = include is null ? "" : $"\nClick + to include all {header.Group.Title}: {string.Join( ", ", header.Group.Rules )}\nApplies to the whole type, including files hidden by the filter and future matches.";
				return WebUtility.HtmlEncode( $"{header.Group.Title}\nClick the header to expand or collapse this type.{action}\nSizes and counts describe visible matches.\n{size}\n{DependencyText( header.Dependencies )}\n{DependencyTooltip}\n{ImpactTooltip}" );
			}
			var file = (FileEntry)item;
			var fileSize = file.Impact is not null ? $"{DirectSize( file.Impact )} {TransitiveSize( file.Impact )}"
				: file.Size.FormatBytes();
			return WebUtility.HtmlEncode( $"{file.Path}\n{file.Note}\n{fileSize}" + (!details ? $"\n{DependencyText( file.Dependencies )}\n{DependencyTooltip}" : "") + (include is null ? "" : $"\nClick + to add this file to Resource Files.\n{ImpactTooltip}") );
		}
	}

	sealed record WarningEntry( string Title, string Details, string IncludeRule, int Priority, PublishImpact Impact, DependencySummary Dependencies, CodeAssetWarning Source = null, bool IsGroup = false, bool IsGroupChild = false )
	{
		public string GroupKey => Priority == 1 ? "scenes:excluded" : Source?.Usage?.StartsWith( "GetAll<", StringComparison.Ordinal ) == true
			? $"query:{Source.SourceFile}\n{Source.Line}\n{Source.Usage}" : null;
	}

	static string WarningTitle( CodeAssetWarning warning ) => warning.Usage is null ? warning.Pattern
		: warning.Usage.Contains( warning.Pattern, StringComparison.OrdinalIgnoreCase ) ? warning.Usage : $"{warning.Usage} · {warning.Pattern}";

	static string WarningReason( CodeAssetWarning warning ) => warning.ExclusionRule is not null ? $"Excluded by {warning.ExclusionRule}" : warning.Recommended ? "Recommended: exact path in code"
		: warning.Usage is not null ? "Review cached-resource query" : "Review possible runtime path";

	/// <summary>Single-line virtual rows shared by scene and code diagnostics.</summary>
	sealed class WarningList : ListView
	{
		readonly Action<string> include;
		public Action<string> ToggleGroup { get; set; }
		public Func<string, bool> IsGroupExpanded { get; set; }
		public Func<string, bool> IsAdded { get; set; }
		public WarningList( Widget parent, Action<string> include ) : base( parent )
		{
			this.include = include;
			ItemSize = new Vector2( 0, 28 );
			ItemSpacing = 0;
			Margin = new Margin( 0, 2, 8, 2 );
		}
		static Rect IncludeRect( VirtualWidget item )
		{
			var rect = item.Rect.Shrink( 4, 3 );
			rect.Left = rect.Right - 24;
			return rect;
		}
		static Rect SourceRect( VirtualWidget item )
		{
			var rect = IncludeRect( item );
			rect.Right = rect.Left - 4;
			rect.Left = rect.Right - 24;
			return rect;
		}
		protected override void PaintItem( VirtualWidget item )
		{
			var entry = (WarningEntry)item.Object;
			var button = IncludeRect( item );
			var sourceButton = SourceRect( item );
			var mouse = FromScreen( Application.CursorPosition );
			if ( !entry.IsGroupChild )
			{
				Paint.SetPen( Theme.Primary.WithAlpha( 0.16f ) );
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.10f ) );
				Paint.DrawRect( item.Rect.Shrink( 0, 1 ), 3 );
			}
			Paint.SetDefaultFont();
			if ( entry.IncludeRule is not null )
			{
				Paint.ClearPen();
				Paint.SetBrush( item.Hovered && button.IsInside( mouse ) ? Theme.ButtonBackground.Lighten( 0.15f ) : Theme.ButtonBackground );
				Paint.DrawRect( button, 3 );
				var added = IsAdded?.Invoke( entry.IncludeRule ) == true;
				Paint.SetPen( added ? Theme.Green : Theme.Text );
				Paint.DrawIcon( button, added ? "check" : "add", 16 );
			}

			if ( entry.Source is not null )
			{
				Paint.ClearPen();
				Paint.SetBrush( item.Hovered && sourceButton.IsInside( mouse ) ? Theme.ButtonBackground.Lighten( 0.15f ) : Theme.ButtonBackground );
				Paint.DrawRect( sourceButton, 3 );
				Paint.SetPen( Theme.Text );
				Paint.DrawIcon( sourceButton, "code", 16 );
			}

			var rect = item.Rect.Shrink( 6, 0 );
			if ( entry.IsGroupChild ) rect.Left += 22;
			if ( entry.IsGroup )
			{
				var icon = rect;
				icon.Width = 16;
				Paint.SetPen( Theme.TextLight );
				Paint.DrawIcon( icon, IsGroupExpanded?.Invoke( entry.GroupKey ) == true ? "expand_more" : "chevron_right", 14 );
			}
			rect.Left += 22;
			rect.Right = (entry.Source is not null ? SourceRect( item ).Left : button.Left) - 8;
			if ( entry.Impact is not null )
			{
				var size = rect;
				size.Left = Math.Max( size.Left, size.Right - Math.Min( 310, item.Rect.Width * 0.38f ) );
				Paint.SetPen( Theme.TextLight );
				var sizeText = $"{DirectSize( entry.Impact )} {TransitiveSize( entry.Impact )}";
				Paint.DrawText( size, Paint.GetElidedText( sizeText, size.Width ), TextFlag.RightCenter | TextFlag.SingleLine );
				rect.Right = size.Left - 12;
			}
			if ( entry.Dependencies is not null && item.Rect.Width >= 850 )
			{
				var dependencies = rect;
				dependencies.Left = Math.Max( dependencies.Left, dependencies.Right - 125 );
				Paint.SetPen( Theme.TextLight );
				Paint.DrawText( dependencies, Paint.GetElidedText( DependencyText( entry.Dependencies ), dependencies.Width ), TextFlag.RightCenter | TextFlag.SingleLine );
				rect.Right = dependencies.Left - 12;
			}
			Paint.SetDefaultFont( weight: entry.IsGroupChild ? 400 : 600 );
			Paint.SetPen( entry.Priority == 0 ? Theme.Red : Theme.Text );
			Paint.DrawText( rect, Paint.GetElidedText( entry.Title, Math.Max( 0, rect.Width ) ), TextFlag.LeftCenter | TextFlag.SingleLine );
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
			if ( e.LeftMouseButton && ((WarningEntry)item.Object) is { IsGroup: true } group )
			{
				e.Accepted = true;
				ToggleGroup?.Invoke( group.GroupKey );
				return false;
			}
			if ( e.LeftMouseButton && ((WarningEntry)item.Object).IncludeRule is { } rule && IncludeRect( item ).IsInside( e.LocalPosition ) )
			{
				e.Accepted = true;
				if ( IsAdded?.Invoke( rule ) != true ) include( rule );
				return false;
			}
			return base.OnItemPressed( item, e );
		}
		protected override string GetTooltip( object item )
		{
			var entry = (WarningEntry)item;
			var details = $"<b>{WebUtility.HtmlEncode( entry.Title )}</b><br>{WebUtility.HtmlEncode( entry.Details )}";
			if ( entry.IncludeRule is not null ) details += "<br>Click + to include this content.";
			if ( entry.Source is { } source )
			{
				details += $"<br><br>{WebUtility.HtmlEncode( Path.GetFileName( source.SourceFile ) )}:{source.Line}";
				if ( !string.IsNullOrWhiteSpace( source.SourceLine ) )
					details += $"<pre>{WebUtility.HtmlEncode( source.SourceLine )}</pre>";
				details += $"<br>Click &lt;&gt; to open in {WebUtility.HtmlEncode( CodeEditor.Title )}.";
			}
			return $"<qt>{details}</qt>";
		}
	}

	sealed class PreviewStatus : Widget
	{
		public bool Busy { get; set; }
		public string Text { get; set; } = "Preparing preview…";

		public PreviewStatus( Widget parent ) : base( parent ) => FixedSize = new Vector2( 210, 32 );

		[EditorEvent.Frame]
		void OnFrame()
		{
			if ( Busy && Visible ) Update();
		}

		protected override void OnPaint()
		{
			if ( Busy )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.12f ) );
				Paint.DrawRect( LocalRect, 4 );
				Paint.ClearBrush();
				Paint.SetPen( Theme.Primary, 3 );
				Paint.DrawArc( new Vector2( 18, 16 ), new Vector2( 10, 10 ), (float)RealTime.Now * 270, 260 );
			}
			Paint.SetDefaultFont( weight: Busy ? 600 : 400 );
			Paint.SetPen( Busy ? Theme.Primary : Theme.TextLight );
			Paint.DrawText( LocalRect.Shrink( 38, 0, 6, 0 ), Text, TextFlag.LeftCenter | TextFlag.SingleLine );
		}
	}
}
