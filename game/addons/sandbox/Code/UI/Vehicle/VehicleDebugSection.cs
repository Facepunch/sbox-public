namespace Sandbox;

/// <summary>
/// The vehicle tuning readout, painted into the HUD canvas: gear, speed, rpm, inputs and a per-wheel breakdown of
/// suspension, slip and grip. Fed by <see cref="FourWheelVehicle"/> while <c>vehicle_debug</c> is on.
/// </summary>
public sealed class VehicleDebugSection : HudSection
{
	public struct WheelReadout
	{
		public string Name;

		/// <summary>
		/// Wheel centre above its hard point, inches, and the most it goes either way.
		/// </summary>
		public float Height;
		public float TravelDown;
		public float TravelUp;

		public float Torque;
		public float Spin;
		public float RollSpin;
		public float SlipRatio;
		public float SlipAngle;
		public float Load;
		public bool Contact;
		public bool Locked;

		/// <summary>
		/// Asking for more grip than the tyre has.
		/// </summary>
		public bool Sliding;
		public bool Steers;
		public float Steer;
	}

	public struct Readout
	{
		public bool Visible;
		public string Title;
		public bool EngineOn;

		public float Mph;
		public float GovernorMph;
		public int Gear;
		public int GearCount;
		public float Rpm;
		public float SoundRpm;
		public float MaxRpm;
		public float ShiftUpRpm;
		public float ShiftDownRpm;

		public float Throttle;
		public float Brake;
		public float Steering;
		public float SteeringAngle;
		public float SteeringCone;

		public bool Handbrake;
		public bool WheelsLocked;
		public bool Boosting;
		public int BoostPercent;
		public bool FreeRevving;
		public string TireMode;

		public string SoundState;
		public int WheelsInContact;
		public float SkidSpeed;
		public float Pitch;
		public float Roll;
		public int TickRate;

		public WheelReadout[] Wheels;
	}

	public Readout Data { get; set; }

	public override int Order => 10;

	const float PanelWidth = 470;
	const float PanelHeight = 600;
	const float Pad = 16;
	const float Margin = 30;

	static readonly Color Good = new( 0.35f, 0.95f, 0.5f );
	static readonly Color Warn = new( 1f, 0.75f, 0.2f );
	static readonly Color Bad = new( 1f, 0.3f, 0.25f );
	static readonly Color Dim = new( 1f, 1f, 1f, 0.35f );
	static readonly Color Track = new( 1f, 1f, 1f, 0.1f );

	Rect PanelRect
	{
		get
		{
			var scale = ScaleToScreen;
			var size = new Vector2( PanelWidth, PanelHeight ) * scale;
			var rect = CanvasRect;
			return new Rect( new Vector2( Margin * scale, MathF.Max( Margin * scale, (rect.Height - size.y) * 0.4f ) ), size );
		}
	}

	public override void GetCompositeRegions( List<Rect> regions )
	{
		if ( !Data.Visible ) return;

		var glow = GlowRadius * ScaleToScreen;
		var rect = PanelRect;
		regions.Add( rect.Grow( glow ) );
	}

	public override void Paint( Painter painter )
	{
		var d = Data;
		if ( !d.Visible ) return;

		var s = ScaleToScreen;
		var panel = PanelRect;
		HudStyle.DrawPanel( painter, panel, HudStyle.PanelColor, s );

		var x = panel.Left + Pad * s;
		var y = panel.Top + Pad * s;
		var w = panel.Width - Pad * 2 * s;

		// title row
		Text( painter, d.Title?.ToUpperInvariant() ?? "VEHICLE", new Rect( x, y, w, 20 * s ), 18, HudStyle.TitleColor, TextFlag.LeftTop, 700 );
		Text( painter, d.EngineOn ? "ENGINE ON" : "ENGINE OFF", new Rect( x, y, w, 20 * s ), 15, d.EngineOn ? Good : Dim, TextFlag.RightTop, 700 );
		y += 30 * s;

		// gear and speed, big
		var gearRect = new Rect( x, y, 80 * s, 80 * s );
		HudStyle.DrawPanel( painter, gearRect, new Color( 0, 0, 0, 0.35f ), s );
		Text( painter, "GEAR", new Rect( gearRect.Left, gearRect.Top + 4 * s, gearRect.Width, 14 * s ), 12, Dim, TextFlag.CenterTop, 700 );
		Text( painter, d.Gear.ToString(), gearRect, 56, Glowing( Tinted( Color.White ) ), TextFlag.Center, 700 );

		var speedRect = new Rect( gearRect.Right + 14 * s, y, w - gearRect.Width - 14 * s, 80 * s );
		Text( painter, $"{d.Mph:0}", new Rect( speedRect.Left, speedRect.Top - 6 * s, speedRect.Width * 0.6f, speedRect.Height ), 64, Glowing( Tinted( Color.White ) ), TextFlag.LeftCenter, 700 );
		Text( painter, "MPH", new Rect( speedRect.Left + speedRect.Width * 0.45f, speedRect.Top + 10 * s, 60 * s, 20 * s ), 14, Dim, TextFlag.LeftTop, 700 );
		Text( painter, $"limit {d.GovernorMph:0.0}\n{d.GearCount} gears", new Rect( speedRect.Left, speedRect.Top, speedRect.Width, speedRect.Height - 6 * s ), 13, Dim, TextFlag.RightBottom, 400 );
		y += 92 * s;

		// rpm bar with redline, shift points and the sound rpm
		Text( painter, "RPM", new Rect( x, y, 60 * s, 16 * s ), 13, Dim, TextFlag.LeftTop, 700 );
		Text( painter, $"{d.Rpm:0}  (sound {d.SoundRpm:0})", new Rect( x, y, w, 16 * s ), 13, HudStyle.TitleColor, TextFlag.RightTop, 700 );
		y += 18 * s;
		var rpmBar = new Rect( x, y, w, 16 * s );
		DrawRpm( painter, rpmBar, d, s );
		y += 30 * s;

		// inputs
		y = DrawBar( painter, "THROTTLE", MathF.Max( 0, d.Throttle ), d.Throttle < 0 ? $"reverse {-d.Throttle:0.00}" : $"{d.Throttle:0.00}", Good, x, y, w, s );
		y = DrawBar( painter, "BRAKE", d.Brake, $"{d.Brake:0.00}", Bad, x, y, w, s );
		y = DrawSteer( painter, d, x, y, w, s );

		// status lights
		y += 4 * s;
		var lx = x;
		lx = Light( painter, "HANDBRAKE", d.Handbrake, Warn, lx, y, s );
		lx = Light( painter, "LOCKED", d.WheelsLocked, Bad, lx, y, s );
		lx = Light( painter, d.Boosting ? "BOOST" : $"BOOST {d.BoostPercent}%", d.Boosting, Good, lx, y, s );
		lx = Light( painter, "FREE REV", d.FreeRevving, Warn, lx, y, s );
		Light( painter, d.TireMode?.ToUpperInvariant() ?? "", d.TireMode != "Normal", Warn, lx, y, s );
		y += 30 * s;

		// wheels, laid out like the car seen from above
		if ( d.Wheels is { Length: 4 } )
		{
			var cell = new Vector2( (w - 12 * s) / 2, 112 * s );
			for ( int i = 0; i < 4; i++ )
			{
				var col = i % 2;
				var row = i / 2;
				var rect = new Rect( x + col * (cell.x + 12 * s), y + row * (cell.y + 10 * s), cell.x, cell.y );
				DrawWheel( painter, d.Wheels[i], rect, s );
			}
			y += cell.y * 2 + 18 * s;
		}

		// footer
		var footer = $"sound {d.SoundState}   contact {d.WheelsInContact}/4   skid {d.SkidSpeed:0} in/s\npitch {d.Pitch:0.0}  roll {d.Roll:0.0}   {d.TickRate} Hz";
		Text( painter, footer, new Rect( x, y, w, panel.Bottom - y - Pad * s ), 13, Dim, TextFlag.LeftTop, 400 );
	}

	void DrawRpm( Painter painter, Rect bar, Readout d, float s )
	{
		var max = MathF.Max( 1, d.MaxRpm );
		Fill( painter, bar, Track, 2 * s );

		// redline zone from the upshift point
		var red = d.ShiftUpRpm / max;
		Fill( painter, new Rect( bar.Left + bar.Width * red, bar.Top, bar.Width * (1 - red), bar.Height ), Bad.WithAlpha( 0.18f ), 0 );

		var fraction = (d.Rpm / max).Clamp( 0, 1 );
		var color = d.Rpm >= d.ShiftUpRpm ? Bad : d.Rpm >= d.ShiftUpRpm * 0.85f ? Warn : Good;
		Fill( painter, new Rect( bar.Left, bar.Top, bar.Width * fraction, bar.Height ), Glowing( Tinted( color ) ), 2 * s );

		// shift points and the sound rpm
		Tick( painter, bar, d.ShiftDownRpm / max, Dim, s );
		Tick( painter, bar, d.ShiftUpRpm / max, Warn, s );
		Tick( painter, bar, (d.SoundRpm / max).Clamp( 0, 1 ), Color.Cyan, s, 4 );
	}

	float DrawBar( Painter painter, string label, float value, string valueText, Color color, float x, float y, float w, float s )
	{
		var labelW = 90 * s;
		Text( painter, label, new Rect( x, y, labelW, 14 * s ), 12, Dim, TextFlag.LeftCenter, 700 );
		var bar = new Rect( x + labelW, y + 2 * s, w - labelW - 70 * s, 10 * s );
		Fill( painter, bar, Track, 2 * s );
		Fill( painter, new Rect( bar.Left, bar.Top, bar.Width * value.Clamp( 0, 1 ), bar.Height ), Tinted( color ), 2 * s );
		Text( painter, valueText, new Rect( bar.Right, y, 70 * s, 14 * s ), 12, HudStyle.TitleColor, TextFlag.RightCenter, 700 );
		return y + 20 * s;
	}

	float DrawSteer( Painter painter, Readout d, float x, float y, float w, float s )
	{
		var labelW = 90 * s;
		Text( painter, "STEER", new Rect( x, y, labelW, 14 * s ), 12, Dim, TextFlag.LeftCenter, 700 );
		var bar = new Rect( x + labelW, y + 2 * s, w - labelW - 70 * s, 10 * s );
		Fill( painter, bar, Track, 2 * s );

		// the lock available at this speed, and the input inside it
		var cone = (d.SteeringCone / 50f).Clamp( 0, 1 );
		Fill( painter, new Rect( bar.Center.x - bar.Width * 0.5f * cone, bar.Top, bar.Width * cone, bar.Height ), Track, 2 * s );

		var mid = bar.Center.x;
		var end = mid + bar.Width * 0.5f * d.Steering.Clamp( -1, 1 );
		Fill( painter, new Rect( MathF.Min( mid, end ), bar.Top, MathF.Abs( end - mid ), bar.Height ), Tinted( Color.Cyan ), 2 * s );
		Fill( painter, new Rect( mid - 1 * s, bar.Top - 2 * s, 2 * s, bar.Height + 4 * s ), Dim, 0 );

		Text( painter, $"{d.SteeringAngle:0.0}° / {d.SteeringCone:0}", new Rect( bar.Right, y, 70 * s, 14 * s ), 12, HudStyle.TitleColor, TextFlag.RightCenter, 700 );
		return y + 20 * s;
	}

	float Light( Painter painter, string label, bool on, Color color, float x, float y, float s )
	{
		if ( string.IsNullOrEmpty( label ) ) return x;

		painter.TextStyle = Style( 12, Color.White, TextFlag.Center, 700 );
		var width = painter.MeasureText( label, new Vector2( 400, 20 ) * s ).x + 16 * s;
		var rect = new Rect( x, y, width, 20 * s );
		Fill( painter, rect, on ? Tinted( color ).WithAlpha( 0.85f ) : Track, 3 * s );
		Text( painter, label, rect, 12, on ? Color.Black : Dim, TextFlag.Center, 700 );
		return rect.Right + 6 * s;
	}

	void DrawWheel( Painter painter, WheelReadout wheel, Rect rect, float s )
	{
		var state = !wheel.Contact ? Warn : wheel.Locked ? Bad : wheel.Sliding ? Warn : Good;
		HudStyle.DrawPanel( painter, rect, new Color( 0, 0, 0, 0.3f ), s );
		Fill( painter, new Rect( rect.Left, rect.Top, 4 * s, rect.Height ), Tinted( state ), 0 );

		var x = rect.Left + 12 * s;
		var w = rect.Width - 20 * s;
		var y = rect.Top + 6 * s;

		var status = !wheel.Contact ? "AIR" : wheel.Locked ? "LOCKED" : wheel.Sliding ? "SLIDE" : "GRIP";
		Text( painter, wheel.Name, new Rect( x, y, w, 16 * s ), 15, HudStyle.TitleColor, TextFlag.LeftTop, 700 );
		Text( painter, status, new Rect( x, y, w - 30 * s, 16 * s ), 12, Tinted( state ), TextFlag.RightTop, 700 );

		// suspension: a vertical bar on the right, the hard point is the line
		var susp = new Rect( rect.Right - 18 * s, rect.Top + 8 * s, 8 * s, rect.Height - 16 * s );
		Fill( painter, susp, Track, 2 * s );
		var range = MathF.Max( 1, wheel.TravelDown + wheel.TravelUp );
		var zero = susp.Bottom - susp.Height * (wheel.TravelDown / range);
		var at = susp.Bottom - susp.Height * ((wheel.Height + wheel.TravelDown) / range).Clamp( 0, 1 );
		Fill( painter, new Rect( susp.Left, MathF.Min( zero, at ), susp.Width, MathF.Abs( zero - at ) + 1 ), Tinted( Color.Cyan ), 0 );
		Fill( painter, new Rect( susp.Left - 3 * s, zero - 1 * s, susp.Width + 6 * s, 2 * s ), HudStyle.TitleColor, 0 );
		y += 20 * s;

		var slipColor = MathF.Abs( wheel.SlipRatio ) > 0.3f || MathF.Abs( wheel.SlipAngle ) > 12 ? Bad : MathF.Abs( wheel.SlipRatio ) > 0.1f || MathF.Abs( wheel.SlipAngle ) > 5 ? Warn : HudStyle.TitleColor;

		Row( painter, "slip", $"{wheel.SlipRatio * 100,4:0}%  {wheel.SlipAngle,5:0.0}°", slipColor, x, ref y, w - 20 * s, s );
		Row( painter, "spin", $"{wheel.Spin,5:0.0} / roll {wheel.RollSpin:0.0}", HudStyle.TitleColor, x, ref y, w - 20 * s, s );
		Row( painter, "torque", $"{wheel.Torque,6:0} Nm", HudStyle.TitleColor, x, ref y, w - 20 * s, s );
		Row( painter, "load", $"{wheel.Load,6:0} N", HudStyle.TitleColor, x, ref y, w - 20 * s, s );
		Row( painter, "susp", $"{wheel.Height,5:0.0} in{(wheel.Steers ? $"   steer {wheel.Steer:0}°" : "")}", HudStyle.TitleColor, x, ref y, w - 20 * s, s );
	}

	void Row( Painter painter, string label, string value, Color color, float x, ref float y, float w, float s )
	{
		Text( painter, label, new Rect( x, y, 50 * s, 15 * s ), 12, Dim, TextFlag.LeftCenter, 400 );
		Text( painter, value, new Rect( x + 50 * s, y, w - 50 * s, 15 * s ), 12, color, TextFlag.LeftCenter, 700 );
		y += 16 * s;
	}

	void Tick( Painter painter, Rect bar, float fraction, Color color, float s, float width = 2 )
	{
		var x = bar.Left + bar.Width * fraction.Clamp( 0, 1 );
		Fill( painter, new Rect( x - width * 0.5f * s, bar.Top - 3 * s, width * s, bar.Height + 6 * s ), Tinted( color ), 0 );
	}

	static void Fill( Painter painter, Rect rect, Color color, float radius )
	{
		painter.Fill = color;
		painter.Rect( rect, radius );
	}

	TextStyle Style( float size, Color color, TextFlag alignment, int weight )
		=> new( HudStyle.Font, size * ScaleToScreen, HudStyle.Brightened( color, TextGain ) ) { FontWeight = weight, Alignment = alignment };

	void Text( Painter painter, string text, Rect rect, float size, Color color, TextFlag alignment, int weight )
	{
		painter.TextStyle = Style( size, color, alignment, weight );
		painter.Text( text, rect );
	}
}
