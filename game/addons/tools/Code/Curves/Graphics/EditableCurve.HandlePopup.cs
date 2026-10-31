namespace Editor.GraphicsItems;

public partial class EditableCurve
{
	/// <summary>
	/// This would be a lot simpler if we just passed the handle to it, and let it fuck with it directly!!
	/// </summary>
	public class HandlePopup : Widget
	{
		Widget X;
		Widget Y;
		public bool AimUp;

		const float ArrowHeight = 8;

		Handle Handle;
		IDisposable editScope;

		float HandleTime
		{
			get
			{
				if ( !Handle.IsValid() )
					return 0.0f;

				var rangeX = Handle.EditableCurve.TimeRange;
				return Handle.Frame.Time.Remap( 0, 1, rangeX.x, rangeX.y, false );
			}
			set
			{
				Handle?.SetValue( value, HandleValue );
			}
		}

		float HandleValue
		{
			get
			{
				if ( !Handle.IsValid() )
					return 0.0f;

				var rangeY = Handle.EditableCurve.ValueRange;
				return Handle.Frame.Value.Remap( 0, 1, rangeY.x, rangeY.y, false );
			}
			set
			{
				Handle?.SetValue( HandleTime, value );
			}
		}

		public HandlePopup( Widget parent ) : base( parent )
		{
			Layout = Layout.Column();

			var so = this.GetSerialized();

			so.OnPropertyStartEdit += _ => editScope ??= Handle?.EditableCurve?.History?.Push( "Move Keyframe" );
			so.OnPropertyFinishEdit += _ =>
			{
				editScope?.Dispose();
				editScope = null;
			};

			{
				X = new Widget( this );
				X.Layout = Layout.Row();
				var control = X.Layout.Add( new FloatControlWidget( so.GetProperty( nameof( HandleTime ) ) ) );
				control.Label = null;
				control.Icon = "east";
				control.HighlightColor = Theme.Red;
			}

			{
				Y = new Widget( this );
				Y.Layout = Layout.Row();
				var control = Y.Layout.Add( new FloatControlWidget( so.GetProperty( nameof( HandleValue ) ) ) );
				control.Label = null;
				control.Icon = "north";
				control.HighlightColor = Theme.Green;
			}

			Layout.Add( X );
			Layout.Add( Y );
			Layout.Spacing = 2;
			Layout.Margin = new Sandbox.UI.Margin( 4, 4, 4, 4 + ArrowHeight );

			var row = Layout.AddRow();

			foreach ( var e in DisplayInfo.ForEnumValues<Curve.HandleMode>() )
			{
				var b = new Button( this );
				b.MaximumSize = 19;
				b.ToolTip = $"{e.info.Name} - {e.info.Description}";
				b.Clicked = () => Handle?.SetHandleMode( e.value );
				b.OnPaintOverride = () => PaintButton( b, e.value, e.info.Icon );

				row.Add( b );
			}
		}

		public override void OnDestroyed()
		{
			editScope?.Dispose();
			editScope = null;

			base.OnDestroyed();
		}

		public void UpdateFrom( Handle handle )
		{
			Handle = handle;
			HandleMode = Handle.Frame.Mode;

			Position = Handle.ViewPosition - (Size * new Vector2( 0.5f, 1.0f )) - new Vector2( 0, 60 );
			Layout.Margin = new Sandbox.UI.Margin( 4, 4, 4, 4 + 8 );

			// popping off screen, be at the bottom instead
			if ( Position.y < -4 )
			{
				Position += new Vector2( 0, 110 + Height );
				Layout.Margin = new Sandbox.UI.Margin( 4, 4 + 8, 4, 4 );
			}

			if ( Parent.IsValid() )
			{
				ConstrainTo( Parent.LocalRect.Shrink( 4 ) );
			}
		}

		protected override void DoLayout()
		{
			base.DoLayout();

			if ( Handle.IsValid() )
				UpdateFrom( Handle );
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			var c = Theme.WidgetBackground;

			Paint.Antialiasing = true;
			Paint.SetBrush( c );
			Paint.ClearPen();

			var r = LocalRect;

			if ( Layout.Margin.Top > 4 )
			{
				Paint.DrawArrow( new Vector2( r.Center.x, Layout.Margin.Top ), new Vector2( r.Center.x, 0 ), Layout.Margin.Top );
				r.Top += Layout.Margin.Top - 4;
			}

			if ( Layout.Margin.Bottom > 4 )
			{
				Paint.DrawArrow( new Vector2( r.Center.x, r.Bottom - Layout.Margin.Bottom ), new Vector2( r.Center.x, r.Bottom ), Layout.Margin.Bottom );
				r.Bottom -= Layout.Margin.Bottom - 4;
			}

			Paint.DrawRect( r, 4 );
		}

		Curve.HandleMode _handlemode;
		public Curve.HandleMode HandleMode
		{
			get => _handlemode;

			set
			{
				if ( _handlemode == value ) return;

				_handlemode = value;
				Update();
			}
		}

		protected bool PaintButton( Button b, Curve.HandleMode h, string icon )
		{
			if ( HandleMode == h )
			{
				Paint.Antialiasing = true;
				Paint.ClearPen();
				Paint.SetBrush( Theme.Blue.WithAlpha( 0.3f ) );
				Paint.DrawRect( b.LocalRect, 3 );

				Paint.SetPen( Theme.Blue );
				Paint.DrawIcon( b.LocalRect, icon, 12 );
			}
			else
			{
				Paint.SetPen( Theme.Blue.WithAlpha( 0.5f ) );
				Paint.DrawIcon( b.LocalRect, icon, 14 );
			}

			return true;
		}
	}
}
