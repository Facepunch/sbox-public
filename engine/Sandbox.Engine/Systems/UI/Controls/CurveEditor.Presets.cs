namespace Sandbox.UI;

public partial class CurveEditor
{
	CurvePresetStore _presetStore;
	internal CurvePresetStore PresetStore => _presetStore ??= CurvePresetStore.ForCurrentContext();

	/// <summary>
	/// User presets shared between curve editors and retained between sessions.
	/// </summary>
	public IReadOnlyList<Curve> SavedPresets => PresetStore.Presets.Select( preset => preset.Curve ).ToArray();

	/// <summary>
	/// Save the active curve, including its axis ranges, as a reusable preset.
	/// </summary>
	public void SavePreset() => PresetStore.Add( ActiveCurveValue );

	/// <summary>
	/// Replace a saved preset with the active curve.
	/// </summary>
	public void ReplacePreset( int index ) => PresetStore.Replace( PresetStore.Presets[index].Id, ActiveCurveValue );

	/// <summary>
	/// Remove a saved preset.
	/// </summary>
	public void DeletePreset( int index ) => PresetStore.Remove( PresetStore.Presets[index].Id );

	sealed class CurvePresets : Panel
	{
		static readonly (string Name, Curve Curve)[] BuiltInPresets =
		[
			("Flat", (Curve)0f),
			("Ease", Curve.Ease),
			("Ease reversed", Curve.Ease.Reverse()),
			("Linear", Curve.Linear),
			("Linear reversed", Curve.Linear.Reverse()),
			("Ease in", Curve.EaseIn),
			("Ease in reversed", Curve.EaseIn.Reverse()),
			("Ease out", Curve.EaseOut),
			("Ease out reversed", Curve.EaseOut.Reverse())
		];

		readonly CurveEditor _editor;
		readonly CurvePresetStore _store;

		public CurvePresets( CurveEditor editor )
		{
			_editor = editor;
			_store = editor.PresetStore;
			_store.Changed += Rebuild;
			AddClass( "curve-presets" );
			Rebuild();
		}

		void Rebuild()
		{
			DeleteChildren( true );
			foreach ( var (name, curve) in BuiltInPresets )
			{
				AddChild( new PresetButton( name, curve, () => _editor.ApplyPreset( curve ) ) );
			}

			var saved = _store.Presets;
			for ( int i = 0; i < saved.Count; i++ )
			{
				var preset = saved[i];
				var tooltip = $"Saved curve {i + 1} - Right-click for options";
				var button = AddChild( new PresetButton( tooltip, preset.Curve, () => _editor.ApplyPreset( preset.Curve ) ) );
				button.AddClass( "saved-curve-preset" );
				button.BuildContextMenu = menu =>
				{
					menu.AddOption( "Apply with ranges", "open_in_full", () => _editor.ApplyPreset( preset.Curve, true ) );
					menu.AddOption( "Replace with current curve", "refresh", () => _store.Replace( preset.Id, _editor.ActiveCurveValue ) );
					menu.AddOption( "Delete preset", "delete", () => _store.Remove( preset.Id ) );
				};
			}

			var add = AddChild( new Button( "", "add", _editor.SavePreset ) );
			add.AddClass( "curve-preset" );
			add.Tooltip = "Save current curve as a preset";
		}

		public override void OnDeleted()
		{
			_store.Changed -= Rebuild;
			base.OnDeleted();
		}
	}

	sealed class PresetButton : Button
	{
		readonly Curve _curve;
		readonly Painter.CachedLine _line = new();
		readonly Vector2[] _points = new Vector2[25];
		Rect _previewRect;
		Menu _menu;
		public Action<Menu> BuildContextMenu { get; set; }

		public PresetButton( string name, Curve curve, Action apply ) : base( "", null, apply )
		{
			_curve = curve;
			Tooltip = name;
			AddClass( "curve-preset" );
		}

		protected override void OnRightClick( MousePanelEvent e )
		{
			if ( BuildContextMenu is null )
				return;
			e.StopPropagation();
			_menu?.Delete( true );
			_menu = new Menu();
			BuildContextMenu( _menu );
			_menu.Closed += menu => menu.Delete( true );
			_menu.Open( this, Popup.PositionMode.UnderMouse );
		}

		public override void OnDeleted()
		{
			_menu?.Delete( true );
			base.OnDeleted();
		}

		public override void OnDraw( Painter painter )
		{
			base.OnDraw( painter );
			var inset = 7 * ScaleToScreen;
			var rect = new Rect( new Vector2( inset ), Box.Rect.Size - inset * 2 );
			if ( rect.Width <= 0 || rect.Height <= 0 || !painter.IsRectVisible( rect ) )
				return;
			if ( _previewRect != rect )
			{
				_previewRect = rect;
				for ( int i = 0; i < _points.Length; i++ )
				{
					float time = i / 24f;
					_points[i] = rect.Position + new Vector2( time * rect.Width, (1 - _curve.EvaluateDelta( time )) * rect.Height );
				}
			}
			_line.Draw( painter, _points, ComputedStyle?.FontColor ?? Color.White );
		}
	}
}
