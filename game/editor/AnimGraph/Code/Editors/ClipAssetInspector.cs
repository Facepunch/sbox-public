using Editor.Assets;

namespace Editor.AnimGraph;

[CanEdit( "asset:anim" )]
public sealed class ClipAssetInspector( Widget parent ) : AssetDocumentInspector( parent )
{
	private ClipAssetPreview _preview;
	private protected override IAnimationDocument LoadDocument( string path ) => ClipDocument.Load( path );

	public override Widget CreatePreviewWidget( Widget parent, AssetPreview preview )
	{
		var replacement = new ClipAssetPreview( parent, Asset, null );
		replacement.SetDocument( Session.Document as ClipDocument, true );
		if ( _preview is { IsValid: true } previous )
		{
			replacement.ScrubTo( previous.PlaybackPosition );
			replacement.IsPlaying = previous.IsPlaying;
		}
		_preview = replacement;
		return _preview;
	}

	protected override void UpdatePreview()
	{
		if ( _preview.IsValid() )
			_preview.SetDocument( Session.Document as ClipDocument, true );
	}
}
