using static Editor.ProjectPublisher.PackageManifest;

namespace Editor;

/// <summary>Shared publishing controls, with preview analysis only while referenced-only mode is enabled.</summary>
internal sealed class PublishSettingsWidget : Widget
{
	readonly Checkbox referencedOnly;
	readonly PublishResourceRulesWidget resourceRules;
	readonly Widget selection;
	readonly InformationBox benefits;
	readonly PreviewLoading loading;
	readonly PreviewLoading updating;
	Widget focusedBeforePreview;

	public PublishAssetReviewWidget Review { get; }
	public PublishOptions Options => new( referencedOnly.Value, resourceRules.Value );
	public Action Changed { get; set; }

	public PublishSettingsWidget( Widget parent, Project project, string footer, Action<PublishOptions> beforePreview = null ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 14;
		Layout.Alignment = TextFlag.Top;
		var heading = Layout.AddRow();
		heading.Spacing = 14;
		heading.Add( new Label.Title( "Publishing" ) { WordWrap = false } );
		updating = heading.Add( new PreviewLoading( this, compact: true ) { Visible = false } );
		heading.AddStretchCell();
		referencedOnly = heading.Add( new Checkbox( "Only publish referenced assets", this ) { Value = project.Config.PublishUsedAssetsOnly } );
		benefits = Layout.Add( new InformationBox( "<b>Smaller downloads, less unused content</b><br>Leave unused assets out of downloads. Review scenes and runtime content before publishing." ) { FixedHeight = 52 } );
		referencedOnly.ToolTip = "Include configured scenes, project settings, Resources folders and explicit rules, plus their dependencies.";
		loading = Layout.Add( new PreviewLoading( this ) { Visible = false } );
		selection = Layout.Add( new Widget( this ) { Layout = Layout.Column() } );
		selection.Layout.Spacing = 14;
		selection.Layout.Alignment = TextFlag.Top;
		var automatic = selection.Layout.Add( new InformationBox( "Choose what ships with your game. Include everything it needs to run while keeping download sizes small.<br>Everything in the Resources/ folder is included automatically. Configured scenes and assets referenced directly or indirectly by those scenes or project settings are included too." )
		{
			BackgroundColor = Theme.Primary,
			VerticalSizeMode = SizeMode.CanShrink
		} );
		automatic.Label.Color = Theme.Text;
		automatic.Layout.Clear( false );
		var informationRow = automatic.Layout.AddRow();
		informationRow.Spacing = 16;
		var informationText = informationRow.AddColumn( 1 );
		informationText.Spacing = 4;
		informationText.Alignment = TextFlag.Top;
		informationText.Add( automatic.Label );
		var agentActions = informationRow.AddColumn();
		agentActions.AddStretchCell();
		Review = selection.Layout.Add( new PublishAssetReviewWidget( selection, project, async cancel =>
		{
			var options = Options;
			beforePreview?.Invoke( options );
			cancel.ThrowIfCancellationRequested();
			var manifest = new ProjectPublisher.PackageManifest();
			await manifest.BuildFromAssets( project, cancel: cancel, options: options, previewOnly: true );
			return manifest;
		}, () => resourceRules.Value, value => resourceRules.Value = value, footer ) );
		resourceRules = Review.RulesEditor.Layout.Add( new PublishResourceRulesWidget( Review.RulesEditor, project, project.Config.Resources, Review.RuleActions, informationText, agentActions ) );
		agentActions.AddStretchCell();
		Review.EditRule = exclude => resourceRules.StartRule( exclude );
		Review.PreviewStateChanged = UpdatePreviewVisibility;
		resourceRules.ValueChanged = () =>
		{
			Changed?.Invoke();
			if ( referencedOnly.Value ) Review.InvalidatePreview();
		};
		referencedOnly.Toggled = () =>
		{
			UpdateSelectionMode();
			Changed?.Invoke();
			if ( referencedOnly.Value ) Review.InvalidatePreview();
		};
		Layout.AddStretchCell();
		UpdateSelectionMode();
	}

	void UpdateSelectionMode()
	{
		benefits.Visible = !referencedOnly.Value;
		if ( !referencedOnly.Value ) Review.CancelPreview();
		UpdatePreviewVisibility();
	}

	void UpdatePreviewVisibility()
	{
		var busy = referencedOnly.Value && Review.IsRefreshing && Review.Manifest is null;
		updating.Visible = referencedOnly.Value && Review.IsRefreshing && !busy;
		var wasBusy = loading.Visible;
		if ( busy && !wasBusy )
		{
			var focused = Application.FocusWidget;
			focusedBeforePreview = focused.IsValid() && focused.IsDescendantOf( selection ) ? focused : null;
		}
		selection.Visible = referencedOnly.Value && !busy;
		loading.Visible = busy;
		if ( wasBusy && !busy )
		{
			if ( referencedOnly.Value && focusedBeforePreview.IsValid() ) focusedBeforePreview.Focus( false );
			focusedBeforePreview = null;
		}
	}

	sealed class PreviewLoading : Widget
	{
		readonly bool compact;
		public PreviewLoading( Widget parent, bool compact = false ) : base( parent )
		{
			this.compact = compact;
			if ( compact ) FixedSize = new Vector2( 184, 32 );
			else FixedHeight = 320;
		}

		[EditorEvent.Frame]
		void OnFrame()
		{
			if ( Visible ) Update();
		}

		protected override void OnPaint()
		{
			var center = LocalRect.Center;
			Paint.Antialiasing = true;
			if ( compact )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.12f ) );
				Paint.DrawRect( LocalRect, 4 );
			}
			Paint.ClearBrush();
			Paint.SetPen( Theme.Primary, compact ? 2 : 4 );
			Paint.DrawArc( compact ? new Vector2( 18, center.y ) : center - new Vector2( 0, 24 ), compact ? new Vector2( 8, 8 ) : new Vector2( 18, 18 ), (float)RealTime.Now * 270, 260 );
			Paint.SetDefaultFont( compact ? 9 : 12, weight: 500 );
			Paint.SetPen( compact ? Theme.Primary.Lighten( 0.25f ) : Theme.Text );
			Paint.DrawText( compact ? LocalRect.Shrink( 36, 0, 8, 0 ) : new Rect( 0, center.y + 12, Width, 28 ), "Updating preview…", compact ? TextFlag.LeftCenter : TextFlag.Center );
		}
	}
}
