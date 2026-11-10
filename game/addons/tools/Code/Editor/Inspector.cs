namespace Editor;

[Dock( "Editor", "Inspector", "manage_search", DockArea.Right )]
public class Inspector : Widget
{
	Layout Editor;
	InspectorToolbar Toolbar;

	public bool IsLocked = false;
	public string CurrentTime => System.DateTime.Now.ToString();

	private InspectorWidget _currentInspector;

	public Inspector( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		Toolbar = new InspectorToolbar( this );

		Layout.Add( Toolbar );
		Layout.AddSeparator();

		Editor = Layout.AddRow( 1 );
		Layout.AddStretchCell();

		EditorUtility.OnInspect += StartInspecting;
	}

	protected override void OnPaint()
	{
		if ( _currentInspector is not null ) return;

		Paint.ClearPen();
		Paint.ClearBrush();

		Paint.SetDefaultFont( italic: true );
		Paint.SetPen( Theme.SurfaceLightBackground );

		var r = LocalRect;
		r.Top += 128;
		Paint.DrawText( r, "No object selected.", TextFlag.CenterTop );
	}

	private void StartInspecting( EditorUtility.OnInspectArgs args )
	{
		if ( !StartInspecting( args.Object, true ) )
		{
			args.Cancel = true;
		}
	}

	private bool StartInspecting( object obj, bool addToHistory = true )
	{
		if ( IsLocked ) return true;
		if ( Editor is null ) return true;

		using var sx = SuspendUpdates.For( this );

		SerializedObject so = obj?.GetSerialized();

		if ( obj is Array array )
		{
			if ( array.Length == 0 )
				return true;

			if ( array.Length == 1 )
			{
				so = array.GetValue( 0 )?.GetSerialized();
			}
			else
			{
				var mo = new MultiSerializedObject();

				for ( int i = 0; i < array.Length; i++ )
				{
					var val = array?.GetValue( i );
					if ( val is null ) continue;
					mo.Add( val.GetSerialized() );
				}

				mo.Rebuild();
				so = mo;
			}
		}

		if ( !so.IsValid() )
		{
			Editor.Clear( true );
			return true;
		}

		if ( IsAlreadyInspectingObject( so ) ) return true;

		// The current inspector may not want to be closed, maybe changes are unsaved.
		if ( _currentInspector != null && !_currentInspector.CloseInspector( obj ) )
		{
			return false;
		}

		Editor.Clear( true );

		_currentInspector = InspectorWidget.Create( so );

		if ( _currentInspector.IsValid() )
		{
			Editor.Add( _currentInspector, 1 );
		}
		else
		{
			// Try CanEdit still..
			// todo: Everything that should be an inspector should be an InspectorWidget
			var customeditor = CanEditAttribute.CreateEditorForObject( obj );

			if ( customeditor.IsValid() )
			{
				Editor.Add( customeditor, 1 );
			}
			else
			{
				try
				{
					var sheet = new ControlSheet();
					sheet.IncludePropertyNames = true;
					sheet.AddObject( so );

					var scroller = new ScrollArea( this );
					scroller.Canvas = new Widget();
					scroller.Canvas.Layout = Layout.Column();
					scroller.Canvas.VerticalSizeMode = SizeMode.CanGrow;
					scroller.Canvas.HorizontalSizeMode = SizeMode.Flexible;

					scroller.Canvas.Layout.Add( sheet );
					scroller.Canvas.Layout.AddStretchCell();

					Editor.Add( scroller );
				}
				catch { }

			}
		}

		if ( addToHistory )
		{
			// Clear everything in our forward
			while ( ObjectHistory.Count > HistoryPlace + 1 )
				ObjectHistory.RemoveAt( ObjectHistory.Count - 1 );

			// Add to history
			ObjectHistory.Add( new HistoryReference( obj ) );
			HistoryPlace = ObjectHistory.Count - 1;

			// limit history size
			if ( ObjectHistory.Count > 100 )
			{
				ObjectHistory.RemoveAt( 0 );
				HistoryPlace--;
			}
		}

		// keep buttons updates
		UpdateBackForward();

		return true;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		EditorUtility.OnInspect -= StartInspecting;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.Button == MouseButtons.Back && GoBack() )
		{
			e.Accepted = true;
			return;
		}

		if ( e.Button == MouseButtons.Forward && GoForward() )
		{
			e.Accepted = true;
			return;
		}

		base.OnMousePress( e );
	}

	public bool GoBack()
	{
		HistoryPlace = HistoryPlace.Clamp( 0, ObjectHistory.Count );

		while ( HistoryPlace > 0 )
		{
			HistoryPlace--;

			if ( JumpToHistory() )
			{
				UpdateBackForward();
				return true;
			}
			else
			{
				ObjectHistory.RemoveAt( HistoryPlace );
			}
		}

		UpdateBackForward();
		return false;
	}

	public bool GoForward()
	{
		HistoryPlace = HistoryPlace.Clamp( 0, ObjectHistory.Count );

		while ( HistoryPlace < ObjectHistory.Count - 1 )
		{
			HistoryPlace++;

			if ( JumpToHistory() )
			{
				UpdateBackForward();
				return true;
			}
			else
			{
				ObjectHistory.RemoveAt( HistoryPlace );
				HistoryPlace--;
			}
		}

		UpdateBackForward();
		return false;
	}

	private bool JumpToHistory()
	{
		var entry = ObjectHistory[HistoryPlace];
		if ( !entry.TryGetTarget( out var target ) )
		{
			return false;
		}

		StartInspecting( target, false );
		return true;
	}

	public void ToggleLock()
	{
		IsLocked = !IsLocked;
		Toolbar?.UpdateLock( IsLocked );
		if ( !IsLocked )
		{
			// Update selection when unlocking
			var selection = SceneEditorSession.Active?.Selection;
			if ( selection is not null && selection.Count > 0 )
			{
				StartInspecting( EditorUtility.InspectorObject );
			}
		}
	}

	void UpdateBackForward()
	{
		Toolbar.UpdateBackForward( HistoryPlace > 0, HistoryPlace < ObjectHistory.Count() - 1 );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( _currentInspector.IsValid() )
		{
			if ( !_currentInspector.SerializedObject.IsValid() )
			{
				_currentInspector.Destroy();
				return;
			}
		}
	}

	private bool IsAlreadyInspectingObject( SerializedObject newObj )
	{
		if ( _currentInspector is null || _currentInspector.SerializedObject is null )
		{
			return false;
		}

		var oldTargets = _currentInspector.SerializedObject.Targets.ToHashSet();
		if ( oldTargets.Count != newObj.Targets.Count() ) return false;

		foreach ( var newTarget in newObj.Targets )
		{
			if ( !oldTargets.Contains( newTarget ) )
			{
				return false;
			}
		}

		return true;
	}

	public int HistoryPlace = 0;
	public List<HistoryReference> ObjectHistory = new();

	// Often we're inspecting local arrays of targets, so we store WeakReferences to the actual contents
	// to avoid ending up with dangling or invalid references after GC. This wraps that so we don't have to think about it.
	public struct HistoryReference
	{
		List<WeakReference<object>> _objects = new List<WeakReference<object>>();

		public HistoryReference( object obj )
		{
			if ( obj is Array array )
			{
				foreach ( var element in array )
				{
					_objects.Add( new WeakReference<object>( element ) );
				}

				return;
			}

			_objects.Add( new WeakReference<object>( obj ) );
		}

		public bool TryGetTarget( out object target )
		{
			var list = new List<object>();
			foreach ( var item in _objects )
			{
				if ( item.TryGetTarget( out var element ) )
				{
					// skip stuff that's no longer valid
					if ( !((element as IValid)?.IsValid() ?? true) )
						continue;

					list.Add( element );
				}
			}

			target = list.ToArray();
			return list.Any();
		}
	}
}

public class InspectorToolbar : Widget
{
	Inspector Inspector;

	IconButton Back;
	IconButton Forward;

	IconButton Lock;

	public InspectorToolbar( Inspector parent ) : base( parent )
	{
		Inspector = parent;
		Layout = Layout.Row();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		//SetIconSize( 16 );

		Back = Layout.Add( new IconButton( "arrow_back", () => Inspector.GoBack() ) );
		Back.IconSize = 16;
		Back.Background = Color.Transparent;

		Forward = Layout.Add( new IconButton( "arrow_forward", () => Inspector.GoForward() ) );
		Forward.IconSize = 16;
		Forward.Background = Color.Transparent;

		Layout.AddStretchCell( 1 );

		Lock = Layout.Add( new IconButton( "lock_open", () => Inspector.ToggleLock() ) );
		Lock.IconSize = 14;
		Lock.Background = Color.Transparent;

		UpdateBackForward( false, false );
		UpdateLock( Inspector.IsLocked );
	}

	public void UpdateBackForward( bool back, bool forward )
	{
		Back.Enabled = back;
		Forward.Enabled = forward;
	}

	public void UpdateLock( bool locked )
	{
		Lock.Icon = locked ? "lock" : "lock_open";
	}
}
