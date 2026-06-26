using System.Threading;

namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// Step 2b - Check licenses of referenced cloud assets and warn about
	/// attribution requirements, non-commercial restrictions, or missing licenses.
	/// </summary>
	class LicenseCheckWizardPage : PublishWizardPage
	{
		public override string PageTitle => "Cloud Asset Licenses";
		public override string PageSubtitle => "Review the licenses of cloud assets referenced by your project.";

		/// <summary>
		/// Non-blocking - the user can always proceed past this page.
		/// </summary>
		public override bool CanProceed() => true;

		WarningBox NonCommercialWarning;
		WarningBox AttributionWarning;
		WarningBox NoLicenseWarning;
		Button CopyAttributionButton;
		ListView AssetList;

		Task FetchTask;
		List<PackageLicenseEntry> Entries = new();

		readonly HashSet<PackageLicenseEntry> ExpandedEntries = new();
		record PackageLicenseEntry( string Ident, Package Package, LicenseFlags Flags, IReadOnlyList<Asset> Sources );
		record ReferenceRow( PackageLicenseEntry Parent, Asset Asset );

		[Flags]
		enum LicenseFlags
		{
			None = 0,

			/// <summary>CC0 - no restrictions</summary>
			Free = 1,

			/// <summary>Requires attribution (CC BY, CC BY-SA, CC BY-NC-ND)</summary>
			Attribution = 2,

			/// <summary>Non-commercial only (CC BY-NC-ND, CC BY-SA)</summary>
			NonCommercial = 4,

			/// <summary>No license specified</summary>
			Unknown = 8
		}

		static readonly Color ColorNonCommercial = Theme.Red;
		static readonly Color ColorAttribution = Theme.Yellow;
		static readonly Color ColorNoLicense = Theme.Blue;

		public override async Task OpenAsync()
		{
			BodyLayout.Clear( true );
			BodyLayout.Spacing = 12;

			Visible = true;

			// Two-column layout: warnings on the left, asset list on the right
			var row = Layout.Row();
			row.Spacing = 16;
			BodyLayout.Add( row, 1 );

			// Left column - warning messages
			var left = row.AddColumn();
			left.Spacing = 8;

			NonCommercialWarning = new WarningBox( "Some referenced assets use non-commercial licenses (CC BY-NC-ND).\nProjects using these assets are ineligible for the Play Fund.", this );
			NonCommercialWarning.BackgroundColor = ColorNonCommercial;
			NonCommercialWarning.Icon = "block";
			NonCommercialWarning.Visible = false;
			left.Add( NonCommercialWarning );

			AttributionWarning = new WarningBox( "Some referenced assets require attribution (CC BY / CC BY-SA).\nYou must provide credit to the asset creators when distributing your project.", this );
			AttributionWarning.BackgroundColor = ColorAttribution;
			AttributionWarning.Icon = "attribution";
			AttributionWarning.Visible = false;
			AttributionWarning.Layout.AddSpacingCell( 8f );

			CopyAttributionButton = new Button( "Copy Attribution Text", "content_copy", AttributionWarning );
			CopyAttributionButton.Clicked = CopyAttributionToClipboard;
			AttributionWarning.Layout.Add( CopyAttributionButton );

			left.Add( AttributionWarning );

			NoLicenseWarning = new WarningBox( "Some referenced assets have no license specified by their creators.\nThese should be used with caution, as their usage rights are unclear.", this );
			NoLicenseWarning.BackgroundColor = ColorNoLicense;
			NoLicenseWarning.Icon = "help_outline";
			NoLicenseWarning.Visible = false;
			left.Add( NoLicenseWarning );

			left.AddStretchCell();

			// Right column - asset list
			var right = row.AddColumn();
			right.Add( new Label( "Referenced Cloud Assets" ) );
			var hint = new Label( "Click a package to see which project assets reference it." );
			hint.Color = Theme.TextControl.WithAlpha( 0.6f );
			right.Add( hint );
			right.Spacing = 8;

			AssetList = new ListView( null );
			AssetList.ItemSize = new Vector2( 0, 24 );
			AssetList.OnPaintOverride = () =>
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.ControlBackground );
				Paint.DrawRect( AssetList.LocalRect, Theme.ControlRadius );
				return false;
			};
			AssetList.ItemPaint = PaintItem;
			AssetList.ItemClicked = OnItemClicked;
			right.Add( AssetList, 1 );

			// Start fetching license data
			FetchTask = FetchLicenses();
		}

		async Task FetchLicenses()
		{
			Entries.Clear();
			ExpandedEntries.Clear();

			var referenceSources = CloudAsset.GetAssetReferenceSources( true );
			if ( referenceSources.Count == 0 )
				return;

			// Fetch all packages in parallel with a concurrency limit
			// useCache: false to ensure we get fresh data with license fields
			using var semaphore = new SemaphoreSlim( 10 );
			var tasks = referenceSources.Select( async kv =>
			{
				await semaphore.WaitAsync();
				try
				{
					var package = await Package.FetchAsync( kv.Key, partial: false, useCache: false );
					return (Ident: kv.Key, Package: package, Sources: kv.Value);
				}
				finally
				{
					semaphore.Release();
				}
			} ).ToList();

			var results = await Task.WhenAll( tasks );

			foreach ( var result in results )
			{
				if ( result.Package is null )
					continue;

				var flags = CategorizePackage( result.Package );
				var sources = result.Sources
					.OrderBy( a => a.Path ?? a.Name, StringComparer.OrdinalIgnoreCase )
					.ToList();

				Entries.Add( new PackageLicenseEntry( result.Ident, result.Package, flags, sources ) );
			}

			// Sort: non-commercial first, then attribution-only, then unknown, then free
			// Within each group, sort alphabetically by title
			Entries.Sort( ( a, b ) =>
			{
				var aSeverity = GetSortOrder( a.Flags );
				var bSeverity = GetSortOrder( b.Flags );
				var cmp = aSeverity.CompareTo( bSeverity );
				if ( cmp != 0 ) return cmp;
				return string.Compare( a.Package.Title ?? a.Package.FullIdent, b.Package.Title ?? b.Package.FullIdent, StringComparison.OrdinalIgnoreCase );
			} );

			if ( !IsValid )
				return;

			UpdateUI();
		}

		static LicenseFlags CategorizePackage( Package package )
		{
			return package.AssetLicense switch
			{
				"CC0" => LicenseFlags.Free,                                      // CC0 - no restrictions
				"CC_BY" => LicenseFlags.Attribution,                             // CC BY - attribution only
				"CC_BYSA" => LicenseFlags.Attribution | LicenseFlags.NonCommercial,  // CC BY-SA - attribution + share alike
				"CC_BYNCND" => LicenseFlags.Attribution | LicenseFlags.NonCommercial, // CC BY-NC-ND - attribution + non-commercial
				_ => LicenseFlags.Unknown                                        // None or unrecognized
			};
		}

		void UpdateUI()
		{
			bool hasNonCommercial = Entries.Any( e => e.Flags.HasFlag( LicenseFlags.NonCommercial ) );
			bool hasAttribution = Entries.Any( e => e.Flags.HasFlag( LicenseFlags.Attribution ) );
			bool hasNoLicense = Entries.Any( e => e.Flags.HasFlag( LicenseFlags.Unknown ) );

			NonCommercialWarning.Visible = hasNonCommercial;
			AttributionWarning.Visible = hasAttribution;
			NoLicenseWarning.Visible = hasNoLicense;

			RebuildList();
		}

		void RebuildList()
		{
			var rows = new List<object>();
			foreach ( var entry in Entries )
			{
				rows.Add( entry );

				if ( !ExpandedEntries.Contains( entry ) )
					continue;

				foreach ( var asset in entry.Sources )
					rows.Add( new ReferenceRow( entry, asset ) );
			}

			AssetList.SetItems( rows );
		}

		void OnItemClicked( object value )
		{
			if ( value is PackageLicenseEntry entry )
			{
				if ( entry.Sources.Count == 0 )
					return;

				if ( !ExpandedEntries.Remove( entry ) )
					ExpandedEntries.Add( entry );

				RebuildList();
				return;
			}

			// Clicking a reference row focuses that asset in the browser
			if ( value is ReferenceRow reference && reference.Asset is not null )
			{
				MainAssetBrowser.Instance?.Local?.FocusOnAsset( reference.Asset );
				EditorUtility.InspectorObject = reference.Asset;
			}
		}

		void CopyAttributionToClipboard()
		{
			var attributionEntries = Entries
				.Where( e => e.Flags.HasFlag( LicenseFlags.Attribution ) )
				.Select( e => $"{e.Package.Title ?? e.Package.FullIdent} by {e.Package.Org?.Title ?? e.Package.Org?.Ident ?? "Unknown"} ({e.Package.Url})" );

			var text = string.Join( "\n", attributionEntries );
			EditorUtility.Clipboard.Copy( text );
		}

		void PaintItem( VirtualWidget item )
		{
			switch ( item.Object )
			{
				case PackageLicenseEntry entry:
					PaintAssetEntry( item, entry );
					break;
				case ReferenceRow reference:
					PaintReferenceRow( item, reference );
					break;
			}
		}

		void PaintAssetEntry( VirtualWidget item, PackageLicenseEntry entry )
		{
			Paint.SetDefaultFont();

			var r = item.Rect.Shrink( 8, 2 );

			// Draw highlighted background for non-free entries
			if ( entry.Flags != LicenseFlags.Free )
			{
				var bgColor = GetPrimaryColor( entry.Flags );
				Paint.ClearPen();
				Paint.SetBrush( bgColor.WithAlpha( 0.08f ) );
				Paint.DrawRect( item.Rect, Theme.ControlRadius );
			}

			bool hasSources = entry.Sources.Count > 0;
			bool isExpanded = ExpandedEntries.Contains( entry );

			// Expand/Collapse arrow
			float contentLeft = item.Rect.Left + 4;
			if ( hasSources )
			{
				var chevronRect = new Rect( item.Rect.Left + 4, item.Rect.Top, item.Rect.Height, item.Rect.Height );
				Paint.SetPen( item.Hovered ? Color.White : Theme.TextControl.WithAlpha( 0.7f ) );
				Paint.DrawIcon( chevronRect, isExpanded ? "expand_more" : "chevron_right", 16, TextFlag.Center );
				contentLeft = chevronRect.Right;
			}

			// Draw multiple color indicator bars on the left for each flag
			float barX = contentLeft;
			float barWidth = 4;
			float barGap = 2;

			foreach ( var flag in GetIndividualFlags( entry.Flags ) )
			{
				var color = GetFlagColor( flag );
				var indicator = new Rect( barX, item.Rect.Top + 2, barWidth, item.Rect.Height - 4 );
				Paint.ClearPen();
				Paint.SetBrush( color );
				Paint.DrawRect( indicator, 2 );
				barX += barWidth + barGap;
			}

			// Draw package title (with reference count)
			var textColor = item.Hovered ? Color.White : Theme.TextControl;
			Paint.SetPen( textColor );

			var titleRect = r;
			titleRect.Left = barX + 4;
			titleRect.Right -= 160;

			var title = entry.Package.Title ?? entry.Package.FullIdent;
			if ( hasSources )
				title = $"{title} ({entry.Sources.Count})";

			Paint.DrawText( titleRect, title, TextFlag.LeftCenter | TextFlag.SingleLine );

			Paint.SetDefaultFont( 6 );

			// Draw license tags on the right
			float tagX = r.Right;
			foreach ( var flag in GetIndividualFlags( entry.Flags ) )
			{
				var label = GetFlagLabel( flag );
				var color = GetFlagColor( flag );
				Paint.SetPen( color );

				var tagRect = new Rect( tagX - 96, r.Top, 92, r.Height );
				var finalRect = Paint.DrawText( tagRect, label, TextFlag.RightCenter | TextFlag.SingleLine );
				tagX -= finalRect.Width + 4;
			}
		}

		void PaintReferenceRow( VirtualWidget item, ReferenceRow reference )
		{
			var asset = reference.Asset;

			Paint.ClearPen();
			Paint.SetBrush( Color.Black.WithAlpha( item.Hovered ? 0.10f : 0.18f ) );
			Paint.DrawRect( item.Rect );

			var r = item.Rect.Shrink( 8, 2 );
			r.Left += 28; // Indent beneath the package row

			// Asset Icon
			var iconRect = new Rect( r.Left, item.Rect.Top + 4, item.Rect.Height - 8, item.Rect.Height - 8 );
			var icon = asset?.AssetType?.Icon16;
			if ( icon is not null )
			{
				Paint.ClearPen();
				Paint.Draw( iconRect, icon );
			}

			// Asset Name
			var textRect = r;
			textRect.Left = iconRect.Right + 8;

			Paint.SetDefaultFont( 7 );
			Paint.SetPen( item.Hovered ? Color.White : Theme.TextControl.WithAlpha( 0.85f ) );
			Paint.DrawText( textRect, asset?.Path ?? asset?.Name ?? "Unknown asset", TextFlag.LeftCenter | TextFlag.SingleLine );
		}

		static IEnumerable<LicenseFlags> GetIndividualFlags( LicenseFlags flags )
		{
			if ( flags.HasFlag( LicenseFlags.NonCommercial ) ) yield return LicenseFlags.NonCommercial;
			if ( flags.HasFlag( LicenseFlags.Attribution ) ) yield return LicenseFlags.Attribution;
			if ( flags.HasFlag( LicenseFlags.Unknown ) ) yield return LicenseFlags.Unknown;
			if ( flags == LicenseFlags.Free ) yield return LicenseFlags.Free;
		}

		static int GetSortOrder( LicenseFlags flags )
		{
			if ( flags.HasFlag( LicenseFlags.NonCommercial ) ) return 0;
			if ( flags.HasFlag( LicenseFlags.Attribution ) ) return 1;
			if ( flags.HasFlag( LicenseFlags.Unknown ) ) return 2;
			return 3;
		}

		static Color GetPrimaryColor( LicenseFlags flags )
		{
			if ( flags.HasFlag( LicenseFlags.NonCommercial ) ) return ColorNonCommercial;
			if ( flags.HasFlag( LicenseFlags.Attribution ) ) return ColorAttribution;
			if ( flags.HasFlag( LicenseFlags.Unknown ) ) return ColorNoLicense;
			return Theme.Green;
		}

		static Color GetFlagColor( LicenseFlags flag ) => flag switch
		{
			LicenseFlags.NonCommercial => ColorNonCommercial,
			LicenseFlags.Attribution => ColorAttribution,
			LicenseFlags.Unknown => ColorNoLicense,
			_ => Theme.Green
		};

		static string GetFlagLabel( LicenseFlags flag ) => flag switch
		{
			LicenseFlags.NonCommercial => "Non-Commercial",
			LicenseFlags.Attribution => "Attribution",
			LicenseFlags.Unknown => "No License",
			_ => "CC0"
		};
	}
}
