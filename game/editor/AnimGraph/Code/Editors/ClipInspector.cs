using Editor.Assets;
using Editor.Inspectors;

namespace Editor.AnimGraph;

[CanEdit( "asset:anim" )]
public sealed class ClipInspector : Widget, AssetInspector.IAssetInspector
{
	private AnimationDocumentSession _session;
	private Asset _asset;
	private ClipDocument _document => (ClipDocument)_session?.Document;
	private bool _readOnly => _session?.ReadOnly ?? true;
	private ClipEventTimeline _events;
	private ClipProperties _properties;
	private ClipAssetPreview _preview;
	private Option _undoOption;
	private Option _redoOption;
	private Label _error;

	public ClipInspector( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public void SetAsset( Asset asset )
	{
		_asset = asset;
		_session = AnimationDocumentSession.Acquire( asset, ClipDocument.Load );
		_session.Changed += OnDocumentChanged;
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
		var replacement = new ClipAssetPreview( parent, _asset, () => Commit( "Set Preview Model" ) );
		if ( _preview.IsValid() )
		{
			replacement.ScrubTo( _preview.PlaybackPosition );
			replacement.IsPlaying = _preview.IsPlaying;
		}
		if ( _preview is not null )
			_preview.PlayheadMoved = null;

		_preview = replacement;
		BindPreview();
		BindEvents();
		return replacement;
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
				_preview?.ScrubTo( position );
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
		_preview?.SetDocument( _document, _readOnly );
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
		_events.Playhead = _preview?.PlaybackPosition ?? 0f;
	}

	private void Commit( string name ) => _session.Commit( name, this );

	private void OnDocumentChanged( object source )
	{
		if ( source == _session )
		{
			BuildUI();
			return;
		}

		if ( source != this )
		{
			_events?.Refresh();
			_properties?.SetDocument( _document );
		}
		_error.Visible = false;
		UpdateOptions();
		BindPreview();
	}

	private void UpdateOptions()
	{
		if ( _undoOption is not null )
			_undoOption.Enabled = _session.CanUndo;
		if ( _redoOption is not null )
			_redoOption.Enabled = _session.CanRedo;
	}

	private void Save()
	{
		if ( _readOnly || _document is null )
			return;

		if ( !_session.Save( this ) )
		{
			_error.Text = "Couldn't save this clip. See the console for details.";
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
		if ( _preview is not null )
			_preview.PlayheadMoved = null;
		if ( _session is not null )
		{
			_session.Changed -= OnDocumentChanged;
			_session.Release();
		}
		base.OnDestroyed();
	}
}
