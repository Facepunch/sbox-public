using Editor.Assets;
using Editor.Inspectors;

namespace Editor.AnimGraph;

[CanEdit( "asset:skel" )]
public sealed class SkeletonInspector : Widget, AssetInspector.IAssetInspector
{
	private AnimationDocumentSession _session;
	private Asset _asset;
	private SkeletonDocument _document => (SkeletonDocument)_session?.Document;
	private bool _readOnly => _session?.ReadOnly ?? true;
	private SkeletonEditor _editor;
	private SkeletonAssetPreview _preview;
	private SkeletonDocument.BoneMask _mask;
	private Option _undoOption;
	private Option _redoOption;
	private Label _error;

	public SkeletonInspector( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public void SetAsset( Asset asset )
	{
		_asset = asset;
		_session = AnimationDocumentSession.Acquire( asset, SkeletonDocument.Load );
		_session.Changed += OnDocumentChanged;
		if ( _document is not null )
			_document.SourceSkeleton = Skeleton.Load( asset.Path );
		BuildUI();
	}

	public void SetInspector( AssetInspector inspector )
	{
		inspector.BindSaveToUnsavedChanges();
		inspector.OnSave += Save;
		inspector.OnReset += Reset;
	}

	public Widget CreatePreviewWidget( Widget parent, AssetPreview preview )
	{
		_preview = new SkeletonAssetPreview( parent, (PreviewSkeleton)preview, () => Commit( "Set Preview Model" ) );
		if ( _document is not null )
			_document.SourceSkeleton = _preview.PreviewSkeleton;
		_editor?.Refresh();
		UpdatePreview();
		return _preview;
	}

	public bool RebuildOnHotload()
	{
		BuildUI();
		return true;
	}

	private void BuildUI()
	{
		using var suspend = SuspendUpdates.For( this );
		Layout.Clear( true );
		_error = Layout.Add( new Label( this ) { Color = Theme.Red, WordWrap = true, Visible = false } );

		if ( _document is not null )
		{
			var bar = Layout.Add( new ToolBar( this ) );
			_undoOption = bar.AddOption( new Option( "Undo", "undo", Undo ) { ShortcutName = "editor.undo" } );
			_redoOption = bar.AddOption( new Option( "Redo", "redo", Redo ) { ShortcutName = "editor.redo" } );
		}

		_editor = Layout.Add( new SkeletonEditor( this )
		{
			ReadOnly = _readOnly,
			Committed = Commit,
			MaskPreviewed = mask =>
			{
				_mask = mask;
				UpdatePreview();
			}
		} );
		_editor.SetDocument( _asset, _document, _mask?.Name );

		if ( _readOnly && _document is not null )
			Layout.Add( new Label( "Installed package skeletons are read-only.", this ) { WordWrap = true } );

		UpdateOptions();
	}

	private void Commit( string name ) => _session.Commit( name, this );

	private void OnDocumentChanged( object source )
	{
		if ( source == _session )
		{
			_document.SourceSkeleton = Skeleton.Load( _asset.Path );
			BuildUI();
			return;
		}

		if ( source != this )
			_editor.SetDocument( _asset, _document, _mask?.Name );
		_error.Visible = false;
		UpdateOptions();
		UpdatePreview();
	}

	private void UpdateOptions()
	{
		if ( _undoOption is not null )
			_undoOption.Enabled = _session.CanUndo;
		if ( _redoOption is not null )
			_redoOption.Enabled = _session.CanRedo;
	}

	private void UpdatePreview()
	{
		_preview?.SetDocument( _document, _readOnly );
		var bones = BoneMaskWeights.GetBones( _document?.SourceSkeleton );
		_preview?.SetBoneWeights( BoneMaskWeights.Build( bones, _mask ), _mask is not null, _document?.RootBone );
	}

	private void Save()
	{
		if ( _readOnly || _document is null )
			return;

		if ( _document.Masks.Any( mask => string.IsNullOrWhiteSpace( mask.Name )
			|| (!mask.Weights.Any( weight => !string.IsNullOrWhiteSpace( weight.Bone ) )
				&& !mask.FloatChannelWeights.Any( weight => !string.IsNullOrWhiteSpace( weight.Channel ) )) )
			|| _document.Masks.Select( mask => mask.Name ).Distinct( StringComparer.OrdinalIgnoreCase ).Count() != _document.Masks.Count )
		{
			_error.Text = "Each mask needs a unique name and at least one weighted bone or channel before saving.";
			_error.Visible = true;
			return;
		}

		if ( !_session.Save( this ) )
		{
			_error.Text = "Couldn't save this skeleton. See the console for details.";
			_error.Visible = true;
			return;
		}

		_asset.Compile( false );
	}

	private void Reset() => _session.Reset();

	[Shortcut( "editor.undo", "CTRL+Z", ShortcutType.Widget )]
	private void Undo() => _session?.Undo();

	[Shortcut( "editor.redo", "CTRL+Y", ShortcutType.Widget )]
	private void Redo() => _session?.Redo();

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
