using Editor.Assets;
using Editor.Inspectors;

namespace Editor.AnimGraph;

public abstract class AssetDocumentInspector : Widget, AssetInspector.IAssetInspector
{
	private Asset _asset;
	private AnimationDocumentSession _session;
	protected Asset Asset => _asset;
	private protected AnimationDocumentSession Session => _session;
	private protected abstract IAnimationDocument LoadDocument( string path );
	public abstract Widget CreatePreviewWidget( Widget parent, AssetPreview preview );
	protected abstract void UpdatePreview();

	protected AssetDocumentInspector( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public void SetAsset( Asset asset )
	{
		_asset = asset;
		_session = AnimationDocumentSession.Acquire( asset, LoadDocument );
		_session.Changed += OnDocumentChanged;
	}

	private void OnDocumentChanged( object source ) => UpdatePreview();

	public bool RebuildOnHotload() => true;

	public override void OnDestroyed()
	{
		if ( _session is not null )
		{
			_session.Changed -= OnDocumentChanged;
			_session.Release();
		}
		base.OnDestroyed();
	}
}
