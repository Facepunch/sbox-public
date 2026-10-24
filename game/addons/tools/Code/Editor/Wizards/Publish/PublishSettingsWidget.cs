using static Editor.ProjectPublisher.PackageManifest;

namespace Editor;

/// <summary>Shared publishing controls, with preview analysis only while referenced-only mode is enabled.</summary>
internal sealed class PublishSettingsWidget : Widget
{
	readonly Checkbox referencedOnly;
	readonly PublishResourceRulesWidget resourceRules;
	readonly Widget selection;
	readonly InformationBox benefits;

	public PublishAssetReviewWidget Review { get; }
	public PublishOptions Options => new( referencedOnly.Value, resourceRules.Value );
	public Action Changed { get; set; }

	public PublishSettingsWidget( Widget parent, Project project, string footer, Action<PublishOptions> beforePreview = null ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 12;
		benefits = Layout.Add( new InformationBox( "<b>Smaller downloads, less unused content</b><br>Leave unused assets out of downloads. Review scenes and runtime content before publishing." ) { FixedHeight = 52 } );
		referencedOnly = Layout.Add( new Checkbox( "Only Publish Referenced Assets", this ) { Value = project.Config.PublishUsedAssetsOnly } );
		referencedOnly.ToolTip = "Include configured scenes, project settings, Resources folders and explicit rules, plus their dependencies.";
		selection = Layout.Add( new Widget( this ) { Layout = Layout.Column() }, 1 );
		selection.Layout.Spacing = 12;
		resourceRules = selection.Layout.Add( new PublishResourceRulesWidget( selection, project, project.Config.Resources ) );
		Review = selection.Layout.Add( new PublishAssetReviewWidget( selection, async cancel =>
		{
			var options = Options;
			beforePreview?.Invoke( options );
			cancel.ThrowIfCancellationRequested();
			var manifest = new ProjectPublisher.PackageManifest();
			await manifest.BuildFromAssets( project, cancel: cancel, options: options, previewOnly: true );
			return manifest;
		}, resourceRules.AddRules ), 1 );
		selection.Layout.Add( new Label.Small( $"{footer} Compiled game code and cloud packages referenced by code are added when publishing." ) );
		Review.PreviewChanged = () => resourceRules.SetRecommendations( Review.IsCurrent ? Review.Manifest.RecommendedRules : null,
			Review.IsCurrent ? Review.Manifest.RecommendedIncludes.Length : 0 );
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
		UpdateSelectionMode();
	}

	void UpdateSelectionMode()
	{
		Layout.Alignment = referencedOnly.Value ? default : TextFlag.Top;
		selection.Visible = referencedOnly.Value;
		benefits.Visible = !referencedOnly.Value;
		if ( !referencedOnly.Value ) Review.CancelPreview();
	}
}
