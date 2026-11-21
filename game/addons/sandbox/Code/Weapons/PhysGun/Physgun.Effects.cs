using Sandbox.Utility;

public partial class Physgun : ScreenWeapon
{
	[Property] public LineRenderer BeamRenderer { get; set; }
	[Property] public GameObject EndPointEffectPrefab { get; set; }
	[Property] public GameObject GrabEffectPrefab { get; set; }

	// ShootInput and SecondaryInput come from BaseSandboxWeapon's seat control.
	/// <summary>
	/// Input for increasing the distance to the grabbed object.
	/// </summary>
	[Property, ClientEditable, Group( "#entity.group.input" ), Title( "#weapon.setting.extend" ), Description( "#weapon.setting.extend.description" )]
	public ClientInput ExtendInput { get; set; }
	/// <summary>
	/// Input for decreasing the distance to the grabbed object.
	/// </summary>
	[Property, ClientEditable, Group( "#entity.group.input" ), Title( "#weapon.setting.retract" ), Description( "#weapon.setting.retract.description" )]
	public ClientInput RetractInput { get; set; }

	[Property, Group( "Screen" )] public float PowerMinDistance { get; set; } = 64f;
	[Property, Group( "Screen" )] public float PowerMaxDistance { get; set; } = 512f;
	[Property, Group( "Screen" )] public float PowerMinFraction { get; set; } = 0.5f;
	[Property, Group( "Screen" )] public float PowerMaxFraction { get; set; } = 1f;

	protected override string ScreenMaterialName => "v_physgun_display";
	protected override string ScreenMaterialPath => "weapons/physgun/physgun-screen.vmat";
	protected override float ScreenRefreshInterval => 0.1f;
	protected override Vector2Int ScreenTextureSize => new Vector2Int( 80, 80 );

	/// <summary>
	/// Spring frequency of the beam's middle point. Higher values follow the physgun aim more tightly.
	/// </summary>
	[Property, Group( "Beam" )] public float BeamFrequency { get; set; } = 128.0f;

	/// <summary>
	/// Damping ratio of the beam's middle point. Below 1 overshoots, lower values wobble.
	/// </summary>
	[Property, Group( "Beam" ), Range( 0, 1 )] public float BeamDamping { get; set; } = 0.8f;

	/// <summary>
	/// How far, in degrees, the beam's middle point may swing away from the aim direction when the camera moves fast.
	/// </summary>
	[Property, Group( "Beam" ), Range( 0, 45 )] public float BeamMaxSway { get; set; } = 5.0f;

	Vector3.SpringDamped middleSpring = new Vector3.SpringDamped( 0, 0 );

	float _prevBeamDistance;
	GameObject _endPointEffect;
	GameObject _grabEffect;

	// whether the beam is in use, drawn or not, so the view model still reacts with beams turned off
	bool _beamOpen;

	public bool BeamActive => _beamOpen || _state.Pulling || _stateHovered.Pulling;
	public bool PullActive => _state.Pulling || _stateHovered.Pulling;

	/// <summary>
	/// The physgun's colours when the holder hasn't picked any, what an empty <see cref="GamePreferences.PhysgunColor"/> stands for
	/// </summary>
	public static readonly Color DefaultBeamColor = new( 0.48f, 0.88f, 1f );
	public static readonly Color DefaultPullColor = new( 1f, 0.8f, 0f );

	/// <summary>
	/// The holder's colour for the beam, its effects, the glow and the outline - the only place they get their colour from
	/// </summary>
	public Color BeamColor => (HasOwner ? GamePreferences.ParseColor( Owner.PhysgunColor ) : null) ?? DefaultBeamColor;

	/// <summary>
	/// The holder's colour for the glow while gravity pulling
	/// </summary>
	public Color PullColor => (HasOwner ? GamePreferences.ParseColor( Owner.PhysgunPullColor ) : null) ?? DefaultPullColor;

	/// <summary>
	/// A part of the physgun in exactly <paramref name="color"/>. The colour the part has in its prefab only says how bright
	/// it glows: it stays that bright to the eye in <paramref name="reference"/>, the default, and brighter or darker colours
	/// scale every part alike.
	/// </summary>
	public static Color Tint( Color original, Color color, Color reference )
	{
		return (color * (Luminance( original ) / Luminance( reference ))).WithAlpha( original.a );
	}

	/// <summary>
	/// <see cref="Tint(Color, Color, Color)"/> for a part in the beam colour
	/// </summary>
	public static Color Tint( Color original, Color color ) => Tint( original, color, DefaultBeamColor );

	public static float Luminance( Color color ) => 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;

	/// <summary>
	/// A physgun colour for the screen and crosshair, which show it as it is: HDR ones are brought down to fit
	/// </summary>
	static Color FitToScreen( Color color )
	{
		var brightness = MathF.Max( 1f, MathF.Max( color.r, MathF.Max( color.g, color.b ) ) );
		return (color * (1f / brightness)).WithAlpha( 1 );
	}

	static Vector4 ScreenColor( Color color )
	{
		var fit = FitToScreen( color );
		return new Vector4( fit.r, fit.g, fit.b, 1f );
	}

	// the beam's gradient as the prefab set it up, for how bright it is along its length
	Gradient? _beamGradient;

	void UpdateBeamColor()
	{
		_beamGradient ??= BeamRenderer.Color;

		var color = BeamColor;
		var frames = _beamGradient.Value.Colors.Select( x => new Gradient.ColorFrame( x.Time, Tint( x.Value, color ) ) );
		BeamRenderer.Color = _beamGradient.Value.WithFrames( [.. frames] );

		if ( _grabEffect.IsValid() ) PhysgunEffectColor.Apply( _grabEffect, color );
		if ( _endPointEffect.IsValid() ) PhysgunEffectColor.Apply( _endPointEffect, color );
	}

	void UpdateBeam( Transform source, Vector3 aimForward, Vector3 end, Vector3 endNormal, bool grabbed )
	{
		if ( !BeamRenderer.IsValid() ) return;

		_beamOpen = true;

		if ( !GamePreferences.PhysgunBeams )
		{
			UpdateHighlight( grabbed ? _state.GameObject : null );
			HideBeam();
			return;
		}

		var endTx = new Transform( end, Rotation.LookAt( endNormal ) );

		if ( grabbed )
		{
			if ( _endPointEffect != null )
			{
				ITemporaryEffect.DisableLoopingEffects( _endPointEffect );
				_endPointEffect = null;
			}


			if ( !_grabEffect.IsValid() )
			{
				_grabEffect = GrabEffectPrefab.Clone( endTx );
			}

			if ( _grabEffect.IsValid() )
			{
				_grabEffect.WorldTransform = endTx;
			}

		}
		else
		{
			if ( _grabEffect != null )
			{
				_grabEffect.Destroy();
				_grabEffect = null;
			}

			if ( !_endPointEffect.IsValid() )
			{
				_endPointEffect = EndPointEffectPrefab.Clone( endTx );
			}

			if ( _endPointEffect.IsValid() )
			{
				_endPointEffect.WorldTransform = endTx;
			}
		}

		UpdateHighlight( grabbed ? _state.GameObject : null );
		UpdateBeamColor();

		bool justEnabled = !BeamRenderer.GameObject.Enabled;

			if ( BeamRenderer.VectorPoints == null || BeamRenderer.VectorPoints.Count != 4 )
			BeamRenderer.VectorPoints = new List<Vector3>( [0, 0, 0, 0] );

		var distance = source.Position.Distance( end );
		var targetMiddle = source.Position + source.Forward * distance * 0.33f;
		targetMiddle = targetMiddle + Noise.FbmVector( 2, Time.Now * 400.0f, Time.Now * 100.0f ) * 1.0f;

		if ( !justEnabled )
		{
			// If the beam halved or more in a single frame, snap the spring to the new position to avoid shakiness
			if ( _prevBeamDistance > 1f && distance / _prevBeamDistance < 0.5f )
			{
				middleSpring = new Vector3.SpringDamped( targetMiddle, targetMiddle, BeamFrequency, BeamDamping );
			}

			// Ensure the middle point is never behind the first one
			var alongFwd = Vector3.Dot( middleSpring.Current - source.Position, source.Forward );
			if ( alongFwd < 0 )
			{
				var clamped = middleSpring.Current - source.Forward * alongFwd;
				middleSpring = new Vector3.SpringDamped( clamped, targetMiddle, BeamFrequency, BeamDamping );
			}
		}
		_prevBeamDistance = distance;

		middleSpring.Target = targetMiddle;
		middleSpring.Frequency = BeamFrequency;
		middleSpring.Damping = BeamDamping;
		middleSpring.Update( Time.Delta );

		var restMiddle = source.Position + aimForward * distance * 0.33f;
		var maxSway = distance * 0.33f * MathF.Tan( BeamMaxSway.DegreeToRadian() );
		var sway = middleSpring.Current - restMiddle;
		if ( sway.Length > maxSway )
		{
			middleSpring.Current = restMiddle + sway.Normal * maxSway;
		}

		BeamRenderer.VectorPoints[0] = source.Position;
		BeamRenderer.VectorPoints[1] = middleSpring.Current;

		var beamEnd = grabbed ? end : end + endNormal * 3;
		var endHook = grabbed ? end + endNormal * 10 : beamEnd;
		BeamRenderer.VectorPoints[2] = Vector3.Lerp( endHook, BeamRenderer.VectorPoints[1], 0.3f + MathF.Sin( Time.Now * 10.0f ) * 0.2f );
		BeamRenderer.VectorPoints[3] = beamEnd;

		if ( justEnabled )
		{
			BeamRenderer.GameObject.Enabled = true;
			_prevBeamDistance = distance;
			BeamRenderer.VectorPoints[1] = targetMiddle;
			middleSpring = new Vector3.SpringDamped( targetMiddle, targetMiddle, BeamFrequency, BeamDamping );
		}


	}
	
	/// <summary>
	/// Outlines the object held by the beam, visible only to player holding the physgun
	/// </summary>
	void UpdateHighlight( GameObject target )
	{
		if ( IsProxy || !target.IsValid() || !GamePreferences.PhysgunOutline )
		{
			BeamHighlight.Hide();
			return;
		}

		BeamHighlight.Show( ObjectHighlight.GetTarget( target ), HighlightStyle.Selected );
	}

	void CloseBeam()
	{
		_beamOpen = false;
		UpdateHighlight( null );
		HideBeam();
	}

	/// <summary>
	/// Takes the beam and its end effects off screen, leaving the outline alone
	/// </summary>
	void HideBeam()
	{
		if ( !BeamRenderer.IsValid() ) return;

		BeamRenderer.GameObject.Enabled = false;

		if ( _endPointEffect.IsValid() )
		{
			ITemporaryEffect.DisableLoopingEffects( _endPointEffect );
			_endPointEffect = null;
		}

		if ( _grabEffect.IsValid() )
		{
			_grabEffect.Destroy();
			_grabEffect = null;
		}
	}

	private const int GraphSamples = 128;
	private float[] _graph1 = new float[GraphSamples];
	private float[] _graph2 = new float[GraphSamples];
	private float[] _graph3 = new float[GraphSamples];
	private int _graphCursor;
	private float _graphTimer;
	private const float GraphInterval = 0.02f;

	private float _plotValue1;
	private float _plotValue2;
	private float _plotValue3;

	private Texture _graphTexture;
	private byte[] _graphPixels = new byte[GraphSamples * 4]; // RGBA8

	/// <summary>
	/// Draws the power meter and channel labels on the viewmodel screen.
	/// </summary>
	protected override void DrawScreenContent( Rect rect, Painter paint )
	{
		using var state = paint.Scope();
		paint.BlendMode = BlendMode.Lighten;
		paint.Stroke = Stroke.None;

		var w = rect.Width;
		var h = rect.Height;
		var padX = w * 0.05f;
		var padY = h * 0.15f;

		var barWidthFraction = 0.55f;
		var barHeightFraction = 0.1f;

		var barW = w * barWidthFraction;
		var barH = h * barHeightFraction;
		var barX = rect.Left + padX;
		var barY = rect.Top + padY;

		var borderColor = new Color( 0.5f, 0.5f, 0.5f );

		// Fill bar
		var fillFraction = MathF.Max( _plotValue1, _plotValue2 );
		var normalized = ((fillFraction - 0.1f) / (0.8f - 0.1f)).Clamp( 0f, 1f );
		var fillWidth = barW * normalized;
		if ( fillWidth > 0f )
		{
			paint.Fill = new Color( 1, 1, 1, 0.8f );
			paint.Rect( new Rect( barX, barY, fillWidth, barH ) );
		}

		// Bar outline
		paint.Fill = Fill.None;
		paint.Stroke = Stroke.Solid( borderColor, 1f );
		paint.Rect( new Rect( barX, barY, barW, barH ) );

		// Percentage label
		var percent = (int)(normalized * 100f);
		paint.TextStyle = new TextStyle( "Consolas", h * 0.135f, Color.White )
		{
			FontWeight = 100,
			Alignment = TextFlag.LeftCenter | TextFlag.SingleLine
		};
		paint.Text( percent.ToString(), new Rect( barX + barW + padX, barY, w - barW - padX * 3f, barH ) );

		// Channel / voltage row
		var rowY = barY + barH + padY;
		paint.TextStyle = paint.TextStyle with
		{
			FontSize = h * 0.14f,
			FontWeight = 400,
			Color = new Color( 0f, 1f, 0f )
		};

		var rowHeight = paint.MeasureText( "Ch2 731v" ).y;
		var rowTop = rowY - rowHeight * 0.5f;
		paint.Text( "Ch2", new Rect( barX, rowTop, w * 0.45f, rowHeight ) );
		paint.Text( "731v", new Rect( barX + w * 0.45f, rowTop, w * 0.45f, rowHeight ) );
	}

	private float _spinIntensity;

	private TimeSince _lastGraphUpdate;

	private void UpdateScreenGraph()
	{
		var active1 = _state.Active && !_state.Pulling;
		var active2 = Input.Down( "attack2" ) && !_preventReselect || _state.Pulling;
		var active3 = _isSpinning;

		var distancePower = 1f;
		if ( active1 )
		{
			var range = PowerMaxDistance - PowerMinDistance;
			var fraction = PowerMaxFraction - PowerMinFraction;
			distancePower = ((_state.GrabDistance - PowerMinDistance) / range * fraction + PowerMinFraction).Clamp( PowerMinFraction, PowerMaxFraction );
		}

		// Track rotation intensity from analog look input
		if ( active3 )
		{
			var look = Input.AnalogLook;
			var rotationMagnitude = MathF.Sqrt( look.pitch * look.pitch + look.yaw * look.yaw + look.roll * look.roll );
			var rotationPower = (rotationMagnitude / 5f).Clamp( 0f, 1f );
			_spinIntensity = _spinIntensity.LerpTo( 0.2f + rotationPower * 0.6f, Time.Delta * 15f );
		}
		else
		{
			_spinIntensity = _spinIntensity.LerpTo( 0f, Time.Delta * 10f );
		}

		var target1 = active1 ? (0.8f * distancePower) + Random.Shared.Float( -0.05f, 0.05f ) : 0.1f + Random.Shared.Float( -0.02f, 0.02f );

		// Held object velocity increases graph power on channel 1
		if ( active1 && _state.Body.IsValid() )
		{
			var velocityPower = (_state.Body.Velocity.Length / 500f).Clamp( 0f, 0.5f );
			target1 += velocityPower;
		}
		var target2 = active2 ? 0.8f + Random.Shared.Float( -0.05f, 0.05f ) : 0.1f + Random.Shared.Float( -0.02f, 0.02f );
		var target3 = active3 ? _spinIntensity + Random.Shared.Float( -0.03f, 0.03f ) : 0.1f + Random.Shared.Float( -0.02f, 0.02f );
		_plotValue1 = _plotValue1.LerpTo( target1, Time.Delta * 10f );
		_plotValue2 = _plotValue2.LerpTo( target2, Time.Delta * 10f );
		_plotValue3 = _plotValue3.LerpTo( target3, Time.Delta * 10f );

		_graphTimer += Time.Delta;
		while ( _graphTimer >= GraphInterval )
		{
			_graphTimer -= GraphInterval;
			_graph1[_graphCursor % GraphSamples] = _plotValue1;
			_graph2[_graphCursor % GraphSamples] = _plotValue2;
			_graph3[_graphCursor % GraphSamples] = _plotValue3;
			_graphCursor++;
		}

		if ( _lastGraphUpdate < ScreenRefreshInterval )
			return;

		_lastGraphUpdate = 0;

		var count = Math.Min( _graphCursor, GraphSamples );
		for ( var i = 0; i < GraphSamples; i++ )
		{
			float r, g, b;
			if ( i < count )
			{
				var idx = (_graphCursor - 1 - i + GraphSamples) % GraphSamples;
				r = _graph1[idx];
				g = _graph2[idx];
				b = _graph3[idx];
			}
			else
			{
				r = 0.1f;
				g = 0.1f;
				b = 0.1f;
			}

			var offset = i * 4;
			_graphPixels[offset + 0] = (byte)(r * 255f);
			_graphPixels[offset + 1] = (byte)(g * 255f);
			_graphPixels[offset + 2] = (byte)(b * 255f);
			_graphPixels[offset + 3] = 255;
		}

		_graphTexture ??= Texture.Create( GraphSamples, 1 ).WithDynamicUsage().Finish();
		_graphTexture.Update( _graphPixels );

		if ( !ViewModel.IsValid() ) return;

		var renderer = ViewModel.GetComponentInChildren<SkinnedModelRenderer>();
		if ( !renderer.IsValid() ) return;

		var so = renderer.SceneObject;
		so.Attributes.Set( "GraphData", _graphTexture );

		so.Attributes.Set( "Grid", new Vector4( 16f, 16f, 0.1f, 1.0f ) );
		so.Attributes.Set( "GraphInfo", new Vector4( GraphSamples, 0f, 0f, 0f ) );
		so.Attributes.Set( "Ch1Color", ScreenColor( BeamColor ) );
		so.Attributes.Set( "Ch2Color", ScreenColor( PullColor ) );
		so.Attributes.Set( "Ch3Color", new Vector4( 1f, 0f, 0f, 0.5f ) );
		so.Attributes.Set( "Band1", new Vector4( 0.5f, 0.3f, 0f, 0f ) );
		so.Attributes.Set( "Band2", new Vector4( 0.48f, 0.28f, 0f, 0f ) );
		so.Attributes.Set( "Band3", new Vector4( 0.52f, 0.32f, 0f, 0f ) );
	}
}
