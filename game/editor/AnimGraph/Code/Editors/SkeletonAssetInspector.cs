using Editor.Assets;

namespace Editor.AnimGraph;

[CanEdit( "asset:skel" )]
public sealed class SkeletonAssetInspector( Widget parent ) : AssetDocumentInspector( parent )
{
	private SkeletonAssetPreview _preview;
	private protected override IAnimationDocument LoadDocument( string path ) => SkeletonDocument.Load( path );

	public override Widget CreatePreviewWidget( Widget parent, AssetPreview preview )
	{
		_preview = new SkeletonAssetPreview( parent, (PreviewSkeleton)preview, null );
		UpdatePreview();
		return _preview;
	}

	protected override void UpdatePreview()
	{
		if ( !_preview.IsValid() )
			return;

		var document = Session.Document as SkeletonDocument;
		_preview.SetDocument( document, true );
		_preview.SetBoneWeights( null, false, document?.RootBone );
	}
}
