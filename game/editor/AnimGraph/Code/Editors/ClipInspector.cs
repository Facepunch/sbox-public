using Editor.Assets;
using Editor.Inspectors;
using Sandbox.Helpers;

namespace Editor.AnimGraph;

[CanEdit( "asset:anim" )]
public sealed class ClipInspector : Widget, AssetInspector.IAssetInspector
{
	private readonly UndoSystem _undo = new();
	private Asset _asset;
	private ClipDocument _document;
	private ClipEventTimeline _events;
	private ClipProperties _properties;
	private PreviewClip _preview;
	private Option _undoOption;
	private Option _redoOption;
	private Label _error;
	private string _committed;
	private string _saved;
	private bool _readOnly;

	public ClipInspector( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;
		VerticalSizeMode = SizeMode.CanGrow;
	}

	[EditorForAssetType( "anim" )]
	public static void Open( Asset asset ) => EditorUtility.InspectorObject = asset;

	public void SetAsset( Asset asset )
	{
		_asset = asset;
		_readOnly = !asset.HasSourceFile || AssetSystem.IsCloudInstalled( asset.Package );
		_document = asset.HasSourceFile ? ClipDocument.Load( asset.GetSourceFile( true ) ) : null;
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
		var replacement = preview as PreviewClip;
		if ( _preview is not null && replacement is not null && _preview != replacement )
		{
			replacement.Seek( _preview.Position );
			replacement.IsPlaying = _preview.IsPlaying;
			replacement.Looping = _preview.Looping;
			replacement.PlaybackRate = _preview.PlaybackRate;
		}
		if ( _preview is not null )
			_preview.PlayheadMoved = null;

		_preview = replacement;
		BindPreview();
		BindEvents();
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

		if ( _document is null )
		{
			Layout.Add( new Label( _asset.HasSourceFile
				? "Couldn't read this clip. See the console for details."
				: "This clip has no source file. Its animation is available in the preview.", this )
			{ WordWrap = true } );
			UpdateOptions();
			return;
		}

		_properties = new ClipProperties( this ) { Committed = Commit, Enabled = !_readOnly };
		_properties.SetDocument( _document );
		AddSection( "Clip Settings", "movie", _properties );

		_events = new ClipEventTimeline( this )
		{
			ReadOnly = _readOnly,
			Edited = Commit,
			Scrubbed = position =>
			{
				_preview?.Seek( position );
				_events.Playhead = position;
			}
		};
		AddSection( "Events", "bolt", _events );
		BindEvents();
		BindPreview();

		if ( _readOnly )
			Layout.Add( new Label( "Installed package clips are read-only.", this ) { WordWrap = true } );

		Layout.AddStretchCell();
		UpdateOptions();
	}

	private void AddSection( string title, string icon, Widget body )
	{
		Layout.Add( new AnimationInspectorSection( title, icon, body, $"ClipInspector.{title}" ) );
		Layout.Add( body );
	}

	private void BindPreview()
	{
		if ( _preview is not null )
			_preview.PlayheadMoved = position =>
			{
				if ( _events.IsValid() )
					_events.Playhead = position;
			};
	}

	private void BindEvents()
	{
		if ( !_events.IsValid() )
			return;

		var frames = Clip.Load( _asset.Path )?.FrameCount ?? 0;
		_events.SetClip( frames > 1 ? _document : null, frames,
			"Compile this clip with at least two frames to edit events." );
		_events.Playhead = _preview?.Position ?? 0f;
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
	}

	private void Restore( string snapshot )
	{
		_document.Restore( snapshot );
		_committed = _document.Snapshot();
		_events?.Refresh();
		_properties?.SetDocument( _document );
		UpdateOptions();
	}

	private void UpdateOptions()
	{
		if ( _undoOption is not null )
			_undoOption.Enabled = !_readOnly && _undo.Back.Count > 0;
		if ( _redoOption is not null )
			_redoOption.Enabled = !_readOnly && _undo.Forward.Count > 0;

		_asset.HasUnsavedChanges = !_readOnly && _committed != _saved;
	}

	private void Save()
	{
		if ( _readOnly || _document is null )
			return;

		if ( !_document.Save() )
		{
			_error.Text = "Couldn't save this clip. See the console for details.";
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
		if ( _readOnly )
			return;

		_undo.Undo();
		UpdateOptions();
	}

	[Shortcut( "editor.redo", "CTRL+Y", ShortcutType.Widget )]
	private void Redo()
	{
		if ( _readOnly )
			return;

		_undo.Redo();
		UpdateOptions();
	}

	public override void OnDestroyed()
	{
		if ( _preview is not null )
			_preview.PlayheadMoved = null;
		base.OnDestroyed();
	}
}
