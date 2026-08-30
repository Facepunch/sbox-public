namespace Editor;

/// <summary>
/// A widget which holds a list of user curve presets, saved in a cookie
/// </summary>
public class CurvePresets : ListView
{
	public Func<Curve?> GetCurveToSave { get; set; }

	/// <summary>
	/// Apply a preset to the curve being edited. The preset's own time/value ranges are ignored,
	/// the edited curve keeps the ranges it already has.
	/// </summary>
	public Action<Curve> OnCurveClicked { get; set; }

	/// <summary>
	/// Apply a preset including its saved time/value ranges. Optional.
	/// </summary>
	public Action<Curve> OnCurveRangesClicked { get; set; }

	/// <summary>
	/// Whether <see cref="OnCurveRangesClicked"/> can do anything right now - ranges are locked
	/// by the property being edited otherwise.
	/// </summary>
	public Func<bool> CanApplyRanges { get; set; }

	List<Curve> curveList = new();

	string CookieName;

	public CurvePresets( Widget parent, string cookie = "CurvePresets" ) : base( parent )
	{
		ItemContextMenu = ShowItemContext;
		ItemSelected = OnItemClicked;
		Margin = 10;
		ItemSpacing = 5;

		CookieName = cookie;
		ItemSize = new Vector2( 68, 38 );

		curveList = EditorCookie.Get<List<Curve>>( cookie, null );

		// defaults
		if ( curveList == null || curveList.Count == 0 )
		{
			curveList = new List<Curve>()
			{
				// Default Curve
				new Curve(),

				// Ease To
				Curve.Ease,

				// Ease From
				Curve.Ease.Reverse(),

				// Linear To
				Curve.Linear,

				// Linear From
				Curve.Linear.Reverse(),

				// Ease In To
				Curve.EaseIn,

				// Ease In From
				Curve.EaseIn.Reverse(),

				// Ease Out To
				Curve.EaseOut,

				// Ease Out From
				Curve.EaseOut.Reverse(),

				new Curve( new List<Curve.Frame>() { new Curve.Frame( 0, 1 ), new Curve.Frame(0.5f, 0), new Curve.Frame( 1, 1 ) } ),
				new Curve( new List<Curve.Frame>() { new Curve.Frame( 0, 0 ), new Curve.Frame(0.5f, 1), new Curve.Frame( 1, 0 ) } ),
			};
		}

		BuildItems();
	}

	void SaveChanges()
	{
		EditorCookie.Set<List<Curve>>( CookieName, curveList );
	}

	public void OnItemClicked( object value )
	{
		// add
		if ( value is string )
		{
			if ( GetCurveToSave == null ) return;
			var fetched = GetCurveToSave?.Invoke();
			if ( !fetched.HasValue ) return;

			Curve curve = fetched.Value;
			curveList.Add( curve );

			// save list
			BuildItems();
			SaveChanges();
		}

		if ( value is Curve c )
		{
			OnCurveClicked?.Invoke( c );
		}
	}

	private void ShowItemContext( object obj )
	{
		if ( obj is not Curve c ) return;

		var m = new ContextMenu( this );

		if ( OnCurveRangesClicked is not null && (CanApplyRanges?.Invoke() ?? true) )
		{
			m.AddOption( "Apply With Ranges", "open_in_full", () => OnCurveRangesClicked( c ) );
			m.AddSeparator();
		}

		m.AddOption( "Copy Json", "content_copy", () =>
		{
			var json = JsonSerializer.Serialize( c );
			EditorUtility.Clipboard.Copy( json );
		} );

		var clipboard = EditorUtility.Clipboard.Paste();
		var p = m.AddOption( "Paste Json", "content_paste", () =>
		{
			var curve = JsonSerializer.Deserialize<Curve>( clipboard );

			var i = curveList.IndexOf( c );
			curveList.Remove( c );

			var fetched = GetCurveToSave?.Invoke();
			if ( !fetched.HasValue ) return;

			curveList.Insert( i, curve );

			BuildItems();
			SaveChanges();
		} );

		try
		{
			JsonSerializer.Deserialize<Curve>( clipboard );
		}
		catch ( System.Exception )
		{
			p.Enabled = false;
		}

		m.AddSeparator();

		var r = m.AddOption( "Replace", "refresh", () =>
		{
			var i = curveList.IndexOf( c );
			curveList.Remove( c );

			var fetched = GetCurveToSave?.Invoke();
			if ( !fetched.HasValue ) return;

			Curve curve = fetched.Value;
			curveList.Insert( i, curve );

			BuildItems();
			SaveChanges();
		} );

		r.Enabled = (GetCurveToSave?.Invoke()).HasValue;

		m.AddSeparator();

		m.AddOption( "Delete", "delete", () =>
		{
			curveList.Remove( c );
			BuildItems();
			SaveChanges();
		} );

		m.OpenAtCursor();
	}

	void BuildItems()
	{
		SetItems( curveList.Cast<object>() );
		AddItem( "add" );
	}

	protected override void PaintItem( VirtualWidget item )
	{
		var rect = item.Rect;

		if ( item.Object is Curve c )
		{
			if ( Paint.HasPressed )
				rect = rect.Shrink( 5 );

			Paint.SetBrush( Theme.Blue.WithAlpha( Paint.HasMouseOver ? 0.5f : 0.2f ) );
			Paint.ClearPen();
			Paint.DrawRect( rect, 4 );

			Paint.Antialiasing = true;
			Paint.SetPen( Theme.Blue.WithAlpha( Paint.HasMouseOver ? 1 : 0.7f ), 3.0f );
			Paint.ClearBrush();
			c.DrawLine( rect.Shrink( 5 ), 2 );
		}

		if ( item.Object is string )
		{
			Paint.SetBrush( Theme.Green.WithAlpha( Paint.HasMouseOver ? 0.5f : 0.3f ) );
			Paint.SetBrushRadial( rect.Center, 65, Theme.Green.WithAlpha( Paint.HasMouseOver ? 0.5f : 0.3f ), Theme.Blue.WithAlpha( 0.03f ) );
			Paint.ClearPen();
			Paint.DrawRect( rect, 4 );

			Paint.Antialiasing = true;
			Paint.SetPen( Theme.Green, 2.0f );
			Paint.ClearBrush();
			Paint.SetDefaultFont( 20, 1000 );
			Paint.DrawText( rect, "+" );
		}
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 4 );

		base.OnPaint();
	}
}
