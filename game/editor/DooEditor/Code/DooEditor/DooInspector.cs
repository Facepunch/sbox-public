namespace Editor.DooEditor;

/// <summary>
/// Inspector panel for editing selected block properties.
/// </summary>
public class DooInspector : Widget
{
	public DooEditorWidget Editor { get; }
	public SerializedObject Target { get; private set; }

	private Layout _content;

	public DooInspector( DooEditorWidget editor ) : base( null )
	{
		Editor = editor;

		MinimumWidth = 300;

		Layout = Layout.Column();
		Layout.Margin = 16;
		Layout.Spacing = 4;

		_content = Layout.AddColumn();
		Layout.AddStretchCell();

		RebuildContent();
	}

	[EditorEvent.Frame]
	public void UpdateSelection()
	{
		if ( !Editor.ValidateTarget() ) return;

		// SetValue catches setter/callback exceptions without sending a changed event.
		// Implicit edits are synchronous, so any still open on the next frame failed.
		if ( _implicitEdit is not null )
		{
			var property = _implicitEdit;
			_implicitEdit = null;
			OnBlockFinishEdit( property );
		}

		var tree = GetAncestor<DooEditorWidget>()?.BlockTree;
		if ( !tree.IsValid() ) return;

		var selection = tree.Selection.FirstOrDefault();
		SetTarget( selection as Doo.Block );
	}

	Doo.Block _target;
	int _editDepth;
	SerializedProperty _implicitEdit;

	public void SetTarget( Doo.Block target ) // todo support multi-select
	{
		if ( !Editor.ValidateTarget() ) return;

		if ( _target == target )
			return;

		UnsubscribeTarget();
		_target = target;
		Target = target?.GetSerialized();

		if ( Target is not null )
		{
			Target.OnPropertyStartEdit += OnBlockStartEdit;
			Target.OnPropertyFinishEdit += OnBlockFinishEdit;
			// Some block controls set values without an explicit editing gesture.
			Target.OnPropertyPreChange += OnBlockPreChange;
			Target.OnPropertyChanged += OnBlockChanged;
		}

		RebuildContent();
	}

	void OnBlockStartEdit( SerializedProperty property )
	{
		if ( Editor.StartEdit() )
			_editDepth++;
	}

	void OnBlockFinishEdit( SerializedProperty property )
	{
		if ( _editDepth == 0 ) return;

		_editDepth--;
		if ( _editDepth == 0 )
			_implicitEdit = null;
		Editor.FinishEdit();
	}

	void OnBlockChanged( SerializedProperty property )
	{
		try
		{
			Editor.NoteChanged();
		}
		finally
		{
			// Property OnChanged handlers can rebuild other properties before this
			// notification reaches us. Only the initiating property's completion ends it.
			if ( _implicitEdit is not null && ReferenceEquals( _implicitEdit, property ) )
			{
				_implicitEdit = null;
				OnBlockFinishEdit( property );
			}
		}
	}

	void OnBlockPreChange( SerializedProperty property )
	{
		if ( !Editor.ValidateTarget() ) return;
		if ( _editDepth > 0 ) return;

		OnBlockStartEdit( property );
		if ( _editDepth > 0 )
			_implicitEdit = property;
	}

	internal void UnsubscribeTarget()
	{
		if ( Target is not null )
		{
			Target.OnPropertyStartEdit -= OnBlockStartEdit;
			Target.OnPropertyFinishEdit -= OnBlockFinishEdit;
			Target.OnPropertyPreChange -= OnBlockPreChange;
			Target.OnPropertyChanged -= OnBlockChanged;
		}

		_implicitEdit = null;
		while ( _editDepth > 0 )
			OnBlockFinishEdit( null );
	}

	public override void OnDestroyed()
	{
		UnsubscribeTarget();
		base.OnDestroyed();
	}

	void RebuildContent()
	{
		_content.Clear( true );

		if ( !Target.IsValid() )
			return;

		var inspector = InspectorWidget.Create( Target );
		if ( inspector == null )
			return;

		_content.Add( inspector );

		Update();
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect.Shrink( 8 ), 4 );
	}
}
