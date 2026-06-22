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

		record PackageLicenseEntry( Package Package, LicenseFlags Flags );

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
			AssetList.ItemPaint = PaintAssetEntry;
			right.Add( AssetList, 1 );

			// Start fetching license data
			FetchTask = FetchLicenses();
		}

		async Task FetchLicenses()
		{
			Entries.Clear();

			var references = CloudAsset.GetAssetReferences( true );
			if ( references.Count == 0 )
				return;

			// Fetch all packages in parallel with a concurrency limit
			// useCache: false to ensure we get fresh data with license fields
			using var semaphore = new SemaphoreSlim( 10 );
			var tasks = references.Select( async ident =>
			{
				await semaphore.WaitAsync();
				try
				{
					return await Package.FetchAsync( ident, partial: false, useCache: false );
				}
				finally
				{
					semaphore.Release();
				}
			} ).ToList();

			var packages = await Task.WhenAll( tasks );

			foreach ( var package in packages )
			{
				if ( package is null )
					continue;

				var flags = CategorizePackage( package );
				Entries.Add( new PackageLicenseEntry( package, flags ) );
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

			AssetList.SetItems( Entries );
		}

		void CopyAttributionToClipboard()
		{
			var attributionEntries = Entries
				.Where( e => e.Flags.HasFlag( LicenseFlags.Attribution ) )
				.Select( e => $"{e.Package.Title ?? e.Package.FullIdent} by {e.Package.Org?.Title ?? e.Package.Org?.Ident ?? "Unknown"} ({e.Package.Url})" );

			var text = string.Join( "\n", attributionEntries );
			EditorUtility.Clipboard.Copy( text );
		}

		void PaintAssetEntry( VirtualWidget item )
		{
			if ( item.Object is not PackageLicenseEntry entry )
				return;

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

			// Draw multiple color indicator bars on the left for each flag
			float barX = item.Rect.Left;
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

			// Draw package title
			var textColor = item.Hovered ? Color.White : Theme.TextControl;
			Paint.SetPen( textColor );

			var titleRect = r;
			titleRect.Left = barX + 4;
			titleRect.Right -= 160;
			Paint.DrawText( titleRect, $"{entry.Package.Title ?? entry.Package.FullIdent}", TextFlag.LeftCenter | TextFlag.SingleLine );

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
