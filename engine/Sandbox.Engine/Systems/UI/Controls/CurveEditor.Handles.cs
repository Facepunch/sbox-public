namespace Sandbox.UI;

public partial class CurveEditor
{
	sealed partial class CurveCanvas
	{
		readonly Panel _handles;
		readonly Dictionary<CurveKey, CurveKeyPanel> _keys = new();
		CurveTangentPanel _incoming, _outgoing;

		void SyncHandles()
		{
			_handles.Style.Left = Plot.Left / ScaleToScreen;
			_handles.Style.Top = Plot.Top / ScaleToScreen;
			_handles.Style.Width = Plot.Width / ScaleToScreen;
			_handles.Style.Height = Plot.Height / ScaleToScreen;
			var keys = _editor.AllKeys().ToHashSet();
			foreach ( var id in _keys.Keys.Where( x => !keys.Contains( x ) ).ToArray() )
			{
				_keys[id].Delete( true );
				_keys.Remove( id );
			}
			foreach ( var id in keys )
			{
				if ( !_keys.TryGetValue( id, out var panel ) )
				{
					panel = _handles.AddChild( new CurveKeyPanel( this, id ) );
					_keys.Add( id, panel );
				}
				panel.Position = _editor.KeyPoint( id );
				// Keys win over tangent handles, and the active curve wins overlapping keys.
				panel.Style.ZIndex = id.CurveIndex == _editor.ActiveCurve ? 2 : 1;
			}
			_incoming ??= _handles.AddChild( new CurveTangentPanel( this, true ) );
			_outgoing ??= _handles.AddChild( new CurveTangentPanel( this, false ) );
			_incoming.Style.Display = _outgoing.Style.Display = HasTangents ? DisplayMode.Flex : DisplayMode.None;
			if ( HasTangents )
			{
				_incoming.Position = ScreenToCanvas( TangentPosition( true ) );
				_outgoing.Position = ScreenToCanvas( TangentPosition( false ) );
			}
		}

		sealed class CurveKeyPanel : CanvasPanel.Handle
		{
			readonly CurveCanvas _canvas;
			CurveEditor Editor => _canvas._editor;
			public CurveKey Key { get; }
			bool _snap;
			EditorState _edit;
			Vector2 _origin;
			Vector2 _minimumDelta, _maximumDelta;

			public CurveKeyPanel( CurveCanvas canvas, CurveKey key )
			{
				_canvas = canvas;
				Key = key;
				AddClass( "curve-key" );
				Style.Width = Style.Height = 18;
			}

			protected override bool OnDragStart( MousePanelEvent e )
			{
				var selected = Editor._selection.ToList();
				if ( e.HasShift && e.HasCtrl && selected.Contains( Key ) )
				{
					selected.Remove( Key );
					Editor.SelectKeys( selected );
					return false;
				}
				if ( !e.HasShift && !selected.Contains( Key ) )
					selected.Clear();
				selected.Remove( Key );
				selected.Add( Key );
				Editor.SelectKeys( selected );
				_snap = e.HasCtrl;
				Editor.BeginEdit();
				_edit = Editor._currentEdit;
				if ( !Editor.IsCurrentDrag( _edit ) )
					return false;
				_origin = Editor.KeyPoint( new( _edit.Active, _edit.Primary ), _edit.Curves );
				var a = _canvas.ScreenToCanvas( _canvas.Plot.Position );
				var b = _canvas.ScreenToCanvas( _canvas.Plot.Position + _canvas.Plot.Size );
				var min = Vector2.Min( a, b );
				var max = Vector2.Max( a, b );
				_minimumDelta = new( float.NegativeInfinity );
				_maximumDelta = new( float.PositiveInfinity );
				foreach ( var id in Editor._selection )
				{
					var point = Editor.KeyPoint( id );
					// Keep the group together. Already off-screen keys may move inward, but not farther out.
					_minimumDelta = Vector2.Max( _minimumDelta, Vector2.Min( Vector2.Zero, min - point ) );
					_maximumDelta = Vector2.Min( _maximumDelta, Vector2.Max( Vector2.Zero, max - point ) );
				}
				return true;
			}

			protected override void OnDragMove( Vector2 delta )
			{
				if ( !Editor.IsCurrentDrag( _edit ) )
				{
					FinishDrag();
					return;
				}
				var origin = _origin;
				if ( _snap || Editor.SnapToGrid )
					delta = Editor.SnapPoint( origin + delta ) - origin;
				if ( DragAxis == Axis.Horizontal )
					delta.y = 0;
				else if ( DragAxis == Axis.Vertical )
					delta.x = 0;
				Editor.MoveSelectionFromStart( Vector2.Max( _minimumDelta, Vector2.Min( _maximumDelta, delta ) ) );
			}

			protected override void OnDragFinish( bool cancelled )
			{
				if ( ReferenceEquals( Editor._currentEdit, _edit ) )
					Editor.EndEdit( cancelled && Editor.IsCurrentDrag( _edit ) );
				_edit = null;
			}

			protected override void OnDoubleClick( MousePanelEvent e ) => e.StopPropagation();

			public override void OnDraw( Painter painter )
			{
				using var scope = painter.Scope();
				bool selected = Editor._selection.Contains( Key );
				float radius = (selected || HasHovered ? 6 : 5) * ScaleToScreen;
				var center = Box.Rect.Size * 0.5f;
				painter.Stroke = Stroke.Solid( _canvas.ComputedStyle.BackgroundColor ?? Color.Black, 2 );
				painter.Fill = selected || HasHovered ? Color.White : Editor.ChannelColor( Key.CurveIndex );
				painter.Polygon( new Vector2[] { center + new Vector2( 0, -radius ), center + new Vector2( radius, 0 ), center + new Vector2( 0, radius ), center + new Vector2( -radius, 0 ) } );
			}
		}

		sealed class CurveTangentPanel : CanvasPanel.Handle
		{
			readonly CurveCanvas _canvas;
			readonly bool _incoming;
			EditorState _edit;
			CurveEditor Editor => _canvas._editor;

			public CurveTangentPanel( CurveCanvas canvas, bool incoming )
			{
				_canvas = canvas;
				_incoming = incoming;
				LockAxisWithShift = false;
				AddClass( "curve-tangent" );
				Style.Width = Style.Height = 18;
			}

			protected override bool OnDragStart( MousePanelEvent e )
			{
				Editor.BeginEdit();
				_edit = Editor._currentEdit;
				return Editor.IsCurrentDrag( _edit );
			}

			protected override void OnDragMove( Vector2 delta )
			{
				if ( !Editor.IsCurrentDrag( _edit ) )
				{
					FinishDrag();
					return;
				}
				var point = Editor.FromDisplay( Editor.ActiveCurveValue, _canvas.ScreenToCanvas( _canvas.MousePosition ) );
				var key = Editor.ActiveCurveValue.Frames[Editor.SelectedIndex];
				float dx = _incoming ? Math.Max( 0.001f, key.Time - point.x ) : Math.Max( 0.001f, point.x - key.Time );
				Editor.SetTangent( Editor.SelectedIndex, _incoming, Math.Clamp( (point.y - key.Value) / dx, -10000, 10000 ) );
			}

			protected override void OnDragFinish( bool cancelled )
			{
				if ( ReferenceEquals( Editor._currentEdit, _edit ) )
					Editor.EndEdit( cancelled && Editor.IsCurrentDrag( _edit ) );
				_edit = null;
			}

			protected override void OnDoubleClick( MousePanelEvent e ) => e.StopPropagation();

			public override void OnDraw( Painter painter )
			{
				using var scope = painter.Scope();
				painter.Stroke = Stroke.None;
				painter.Fill = HasHovered ? Color.White : Editor.ChannelColor( Editor.ActiveCurve );
				painter.Circle( Box.Rect.Size * 0.5f, 3 * ScaleToScreen );
			}
		}
	}
}
