using Editor.Assets;
using Editor.Inspectors;
using Sandbox.Helpers;

namespace Editor.AnimGraph;

[CanEdit( "asset:skel" )]
public sealed class SkeletonInspector : Widget, AssetInspector.IAssetInspector
{
	private readonly UndoSystem _undo = new();
	private Asset _asset;
	private SkeletonDocument _document;
	private SkeletonEditor _editor;
	private PreviewSkeleton _preview;
	private SkeletonDocument.BoneMask _mask;
	private Option _undoOption;
	private Option _redoOption;
	private Label _error;
	private string _committed;
	private string _saved;
	private bool _readOnly;

	public SkeletonInspector( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;
		VerticalSizeMode = SizeMode.CanGrow;
	}

	[EditorForAssetType( "skel" )]
	public static void Open( Asset asset ) => EditorUtility.InspectorObject = asset;

	public void SetAsset( Asset asset )
	{
		_asset = asset;
		_readOnly = !asset.HasSourceFile || AssetSystem.IsCloudInstalled( asset.Package );
		_document = asset.HasSourceFile ? SkeletonDocument.Load( asset.GetSourceFile( true ) ) : null;
		if ( _document is not null )
			_document.SourceSkeleton = Skeleton.Load( asset.Path );
		_saved = _committed = _document?.Snapshot();
		_undo.Initialize();
		BuildUI();
	}

	public void SetInspector( AssetInspector inspector )
	{
		if ( _readOnly || _document is null )
			return;

		inspector.BindSaveToUnsavedChanges();
		inspector.OnSave += Save;
		inspector.OnReset += Reset;
	}

	public void SetAssetPreview( AssetPreview preview )
	{
		_preview = preview as PreviewSkeleton;
		_editor?.Refresh();
		UpdatePreview();
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

	private void Commit( string name )
	{
		if ( _readOnly || _document is null )
			return;

		var before = _committed;
		var after = _document.Snapshot();
		if ( before == after )
			return;

		_committed = after;
		_undo.Insert( name, () => Restore( before ), () => Restore( after ) );
		UpdateOptions();
		UpdatePreview();
	}

	private void Restore( string snapshot )
	{
		_document.Restore( snapshot );
		_committed = _document.Snapshot();
		_editor.SetDocument( _asset, _document, _mask?.Name );
		UpdateOptions();
		UpdatePreview();
	}

	private void UpdateOptions()
	{
		if ( _undoOption is not null )
			_undoOption.Enabled = !_readOnly && _undo.Back.Count > 0;
		if ( _redoOption is not null )
			_redoOption.Enabled = !_readOnly && _undo.Forward.Count > 0;

		_asset.HasUnsavedChanges = !_readOnly && _committed != _saved;
	}

	private void UpdatePreview()
	{
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

		if ( !_document.Save() )
		{
			_error.Text = "Couldn't save this skeleton. See the console for details.";
			_error.Visible = true;
			return;
		}

		_error.Visible = false;
		_saved = _committed = _document.Snapshot();
		UpdateOptions();
		_asset.Compile( false );
	}

	private void Reset()
	{
		Restore( _saved );
		_undo.Initialize();
		_error.Visible = false;
		UpdateOptions();
	}

	[Shortcut( "editor.undo", "CTRL+Z", ShortcutType.Widget )]
	private void Undo()
	{
		_undo.Undo();
		UpdateOptions();
	}

	[Shortcut( "editor.redo", "CTRL+Y", ShortcutType.Widget )]
	private void Redo()
	{
		_undo.Redo();
		UpdateOptions();
	}
}
