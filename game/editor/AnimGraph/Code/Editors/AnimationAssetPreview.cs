using Editor.Assets;

namespace Editor.AnimGraph;

internal sealed class SkeletonAssetPreview : PreviewSceneWidget
{
	private readonly PreviewSkeleton _source;
	private readonly AnimationPreviewModelPicker _modelPicker;
	private string _modelName;

	public SkeletonAssetPreview( Widget parent, PreviewSkeleton source, Action modelChanged ) : base( parent, "Skeleton Preview" )
	{
		_source = source;
		Toolbar.Add( new PreviewOptions( this, "skeleton", PreviewOption.All & ~PreviewOption.RootMotion, true ).CreateBar( this ), 1 );
		SetSkeleton( Skeleton.Load( source.Asset.Path ) );
		_modelPicker = Layout.Add( new AnimationPreviewModelPicker( this, modelChanged ) );
	}

	public void SetDocument( SkeletonDocument document, bool readOnly )
	{
		_modelPicker.SetDocument( document, readOnly );
		SetPreviewModel( document?.PreviewModel );
	}

	private void SetPreviewModel( string name )
	{
		if ( _modelName == name )
			return;

		_modelName = name;
		SetModel( string.IsNullOrWhiteSpace( name ) ? null : Model.Load( name ) );
		ShowBones = Options.Cookie( "bones", PreviewModel is null );
		if ( PreviewModel is null )
			FrameCamera();
	}

	public void SetBoneWeights( IReadOnlyDictionary<string, float> weights, bool showWeights, string rootBone )
		=> _source.SetBoneWeights( weights, showWeights, rootBone );

	protected override Color BoneColor( string bone, bool hovered ) => _source.GetBoneColor( bone, hovered );
}

internal sealed class ClipAssetPreview : ClipEditor, AssetSystem.IEventListener
{
	private readonly Asset _asset;
	private readonly AnimationPreviewModelPicker _modelPicker;
	private string _modelName;

	protected override bool AllowSkeletonPreview => true;
	protected override string NoModelHint => "Compile this clip and its skeleton to preview its animation.";

	public ClipAssetPreview( Widget parent, Asset asset, Action modelChanged ) : base( parent, "Animation Clip Preview" )
	{
		_asset = asset;
		_modelPicker = Layout.Add( new AnimationPreviewModelPicker( this, modelChanged ) );
		UpdateSource();
	}

	public void SetDocument( ClipDocument document, bool readOnly )
	{
		_modelPicker.SetDocument( document, readOnly );
		if ( _modelName != document?.PreviewModel )
			UpdateSource();
	}

	private void UpdateSource()
	{
		_modelName = _modelPicker.Document?.PreviewModel;
		var modelName = _modelName;
		if ( string.IsNullOrWhiteSpace( modelName ) )
		{
			var skeleton = AssetSystem.FindByPath( Clip.Load( _asset.Path )?.SkeletonName );
			var document = skeleton?.HasSourceFile == true ? SkeletonDocument.Load( skeleton.GetSourceFile( true ) ) : null;
			modelName = document?.PreviewModel;
		}
		var model = string.IsNullOrWhiteSpace( modelName ) ? null : Model.Load( modelName );
		SetSource( model, _asset.Path );
		ShowBones = Options.Cookie( "bones", model is null );
	}

	void AssetSystem.IEventListener.OnAssetChanged( Asset asset )
	{
		if ( asset != _asset && !_asset.GetReferences( false ).Contains( asset ) )
			return;

		var position = PlaybackPosition;
		var playing = IsPlaying;
		UpdateSource();
		QueueRebuild();
		ScrubTo( position );
		IsPlaying = playing;
	}
}

internal sealed class AnimationPreviewModelPicker : Widget
{
	private readonly Action _changed;
	public IAnimationDocument Document { get; private set; }

	public AnimationPreviewModelPicker( Widget parent, Action changed ) : base( parent )
	{
		_changed = changed;
		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 0, 2, 0, 0 );
	}

	public void SetDocument( IAnimationDocument document, bool readOnly )
	{
		Enabled = !readOnly;
		Visible = document is not null;
		if ( Document == document )
			return;

		Document = document;
		Layout.Clear( true );
		if ( document is null )
			return;

		var serialized = EditorTypeLibrary.GetSerializedObject( document );
		serialized.OnPropertyChanged += _ => _changed?.Invoke();
		var picker = ControlWidget.Create( serialized.GetProperty( nameof( IAnimationDocument.PreviewModel ) ) );
		picker.HorizontalSizeMode = SizeMode.Flexible;
		Layout.Add( picker, 1 );
	}
}
