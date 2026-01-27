namespace Editor;

/// <summary>
/// Manages those annoying notices on the side of your screen. You get them when you're compiling.
/// This is what's making those errors keep appearing. It's not your bad code, it's this bad class,
/// blame this class - it's this classes fault you're getting annoyed.
/// </summary>
public static class ToastManager
{
	class Entry
	{
		public Widget Widget;

		public bool IsDead;
		public RealTimeUntil TimeUntilRemove;
		public RealTimeUntil TimeUntilShow = 0.1f;

		public Vector2 Position => (Vector2)Springy.Current;
		public Vector3.SpringDamped Springy = new Vector3.SpringDamped( 0, 0, 1.5f, 0.5f );

		public void Tick( Vector3 idealPosition )
		{
			ToastWidget nw = Widget as ToastWidget;

			nw?.Tick();

			if ( TimeUntilShow > 0 )
				return;

			Springy.Target = idealPosition;

			Springy.Update( RealTime.SmoothDelta );

			Widget.Position = Position;

			if ( nw.IsValid() )
			{
				Widget.Visible = nw.WantsVisible;
			}
		}

		public void Died( float delay )
		{
			if ( IsDead && delay > TimeUntilRemove )
				return;

			IsDead = true;
			TimeUntilRemove = delay;
		}

		internal bool WantsToKeepOpen()
		{
			if ( !Widget.IsValid() )
				return false;

			if ( !Widget.IsUnderMouse )
				return false;

			if ( Widget.TransparentForMouseEvents )
				return false;

			return true;
		}

		internal float Height()
		{
			if ( !Widget.IsValid() )
				return 0;

			return Widget.Height;
		}
	}

	public static IEnumerable<Widget> All => Entries.Select( x => x.Widget );

	static List<Entry> Entries = new();

	/// <summary>
	/// Add a new widget. Derive your widget from NoticeWidget to win big prizes!
	/// </summary>
	public static void Add( Widget widget )
	{
		var e = new Entry
		{
			Widget = widget,
		};

		var rect = EditorWindow.ScreenGeometry.Shrink( 16 );

		e.Springy = e.Springy with { Current = new Vector3( rect.Left - 500, rect.Bottom, 0 ) };

		Entries.Add( e );
	}

	/// <summary>
	/// If we're going to kill this widget, then don't
	/// </summary>
	internal static void Reset( Widget widget )
	{
		foreach ( var entry in Entries.Where( x => x.Widget == widget ) )
		{
			entry.IsDead = false;
		}

	}

	/// <summary>
	/// Remove this widget in timeDelay seconds. If we're already removing it, this will only have
	/// an effect is timeDelay is lower than the current delay until we're being removed
	/// </summary>
	public static void Remove( Widget widget, float timeDelay = 2.0f )
	{
		foreach ( var e in Entries.Where( x => x.Widget == widget ) )
		{
			e.Died( timeDelay );
		}
	}

	/// <summary>
	/// We're done with this widget, get rid of it straight away, even if we're hovering over it.
	/// </summary>
	public static void Dismiss( Widget widget )
	{
		widget.TransparentForMouseEvents = true;

		foreach ( var e in Entries.Where( x => x.Widget == widget ) )
		{
			e.Died( 0.3f );
		}
	}

	/// <summary>
	/// Instantly destroy all notifications
	/// </summary>
	public static void ClearAll()
	{
		foreach ( var r in Entries )
		{
			if ( r.Widget.IsValid() )
			{
				r.Widget.Destroy();
			}
		}

		Entries.Clear();
	}

	[EditorEvent.Frame]
	public static void Tick()
	{
		if ( !EditorWindow.IsValid() ) return;

		var rect = EditorWindow.ScreenGeometry.Shrink( 16 );
		var y = rect.Bottom - 16;

		foreach ( var e in Entries )
		{
			var x = rect.Left;
			var height = e.Height();

			if ( e.IsDead )
			{
				// Don't destroy a widget we're hovering
				if ( e.WantsToKeepOpen() && e.TimeUntilRemove < 2 )
				{
					e.TimeUntilRemove = 2;
				}

				if ( e.TimeUntilRemove < 0.3f )
					x -= 400;
			}

			e.Tick( new Vector2( x, y - height ) );

			if ( (!e.IsDead || e.TimeUntilRemove > 0.3f) && e.Widget.IsValid() && e.Widget.Visible )
			{
				y -= height;
				y -= 10;
			}
		}

		var removal = Entries.Where( x => x.IsDead && x.TimeUntilRemove < 0 ).ToArray();
		foreach ( var r in removal )
		{
			if ( r.Widget.IsValid() )
			{
				r.Widget.Destroy();
			}

			Entries.Remove( r );
		}
	}

	public static void AddProgress( IProgressSection section )
	{
		var existing = All.OfType<ProgressToast>().FirstOrDefault( x => x.Section == section );
		if ( existing is null )
		{
			new ProgressToast( section );
		}
	}

	public static void RemoveProgress( IProgressSection section )
	{
		var existing = All.OfType<ProgressToast>().FirstOrDefault( x => x.Section == section );
		if ( existing is not null )
		{
			Remove( existing, 0.5f );
		}
	}
}
