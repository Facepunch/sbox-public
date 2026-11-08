using Editor.Assets;

namespace Editor.AnimGraph;

[EditorForAssetType( "skel" )]
public sealed class SkeletonAssetEditor : AssetDocumentEditor
{
	private SkeletonAssetPreview _skeletonPreview;
	private PreviewSkeleton _skeletonSource;
	private SkeletonEditor _skeletonSettings;
	private SkeletonDocument.BoneMask _mask;
	private SkeletonDocument Document => Session.Document as SkeletonDocument;
	protected override string EditorName => "Skeleton";
	protected override string LayoutCookie => "AnimationSkeletonEditor.v2";
	private protected override IAnimationDocument LoadDocument( string path ) => SkeletonDocument.Load( path );

	protected override void CreateEditor( Widget center, ScrollArea settings )
	{
		_skeletonSource = new PreviewSkeleton( Asset );
		_skeletonPreview = center.Layout.Add( new SkeletonAssetPreview( center, _skeletonSource, OnPreviewModelChanged ), 1 );
		_skeletonSettings = new SkeletonEditor( settings )
		{
			ReadOnly = Session.ReadOnly,
			Committed = Commit,
			MaskPreviewed = OnMaskPreviewed
		};
		settings.Canvas = _skeletonSettings;
	}

	private void OnMaskPreviewed( SkeletonDocument.BoneMask mask )
	{
		_mask = mask;
		UpdatePreview();
	}

	protected override void RefreshDocument()
	{
		if ( Document is { } document )
			document.SourceSkeleton = Skeleton.Load( Asset.Path );
		_skeletonSettings.SetDocument( Asset, Document, _mask?.Name );
	}

	protected override void UpdatePreview()
	{
		_skeletonPreview.SetDocument( Document, Session.ReadOnly );
		var bones = BoneMaskWeights.GetBones( Document?.SourceSkeleton );
		_skeletonPreview.SetBoneWeights( BoneMaskWeights.Build( bones, _mask ), _mask is not null, Document?.RootBone );
	}

	protected override void RefreshCompiledAsset()
	{
		FinishEditing();
		OnDocumentChanged( null );
	}

	protected override bool ValidateDocument()
	{
		if ( Document is { } skeleton && (skeleton.Masks.Any( mask => string.IsNullOrWhiteSpace( mask.Name )
			|| (!mask.Weights.Any( weight => !string.IsNullOrWhiteSpace( weight.Bone ) )
				&& !mask.FloatChannelWeights.Any( weight => !string.IsNullOrWhiteSpace( weight.Channel ) )) )
			|| skeleton.Masks.Select( mask => mask.Name ).Distinct( StringComparer.OrdinalIgnoreCase ).Count() != skeleton.Masks.Count) )
			return ShowError( "Each mask needs a unique name and at least one weighted bone or channel before saving." );

		return true;
	}

	protected override void OnDocumentClosing() => _skeletonSource?.Dispose();
}
