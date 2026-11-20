using Sandbox.Rendering;

public partial class ScreenWeapon : BaseSandboxWeapon
{
	private Material _screenMaterialCopy;
	private Texture _screenTexture;
	private RenderTarget _screenRenderTarget;
	private CommandList _screenCommandList;
	private float _coilSpin;
	private float _joystickX;
	private float _joystickY;
	private TimeSince _lastScreenUpdate;
	private TimeSince _sinceTransition = float.MaxValue;

	/// <summary>
	/// How long a screen transition lasts after <see cref="StartScreenTransition"/>
	/// </summary>
	const float TransitionDuration = 0.35f;

	/// <summary>
	/// How bright the screen flares at the start of a transition, fading back to 1
	/// </summary>
	const float TransitionIntensity = 5f;

	/// <summary>
	/// Starts a transition on the screen, drawn by <see cref="DrawScreenContent"/> using <see cref="TransitionProgress"/>
	/// </summary>
	protected void StartScreenTransition() => _sinceTransition = 0;

	/// <summary>
	/// A transition from <see cref="StartScreenTransition"/> is still running
	/// </summary>
	protected bool InScreenTransition => _sinceTransition < TransitionDuration;

	/// <summary>
	/// How far through the current transition the screen is, 0 to 1
	/// </summary>
	protected float TransitionProgress => (_sinceTransition / TransitionDuration).Clamp( 0f, 1f );

	/// <summary>
	/// Override to match a different model's screen material.
	/// </summary>
	protected virtual string ScreenMaterialName => "toolgun_screen";

	/// <summary>
	/// Override to use a different screen material
	/// </summary>
	protected virtual string ScreenMaterialPath => "weapons/toolgun/toolgun-screen.vmat";

	/// <summary>
	/// Size of the screen texture. The toolgun's dot matrix shader draws one dot per pixel.
	/// </summary>
	protected virtual Vector2Int ScreenTextureSize => new( 52, 13 );

	/// <summary>
	/// Minimum time in seconds between screen redraws
	/// </summary>
	protected virtual float ScreenRefreshInterval => 0f;

	/// <summary>
	/// Add energy to the coil spin (e.g. on fire).
	/// </summary>
	public void SpinCoil()
	{
		_coilSpin += 10;
	}

	/// <summary>
	/// Smoothly decays the coil spin and applies it to the viewmodel's "coil" object.
	/// </summary>
	protected void ApplyCoilSpin()
	{
		_coilSpin = _coilSpin.LerpTo( 0, Time.Delta * 1 );

		if ( !ViewModel.IsValid() ) return;

		var coil = ViewModel.GetAllObjects( true ).FirstOrDefault( x => x.Name == "coil" );
		if ( coil.IsValid() )
		{
			coil.WorldRotation *= Rotation.From( 0, 0, _coilSpin );
		}
	}

	/// <summary>
	/// Updates joystick_x and joystick_y on the viewmodel based on a per-frame look delta.
	/// </summary>
	public void UpdateJoystick( Angles lookDelta )
	{
		_joystickX = _joystickX.LerpTo( lookDelta.yaw.Clamp( -1f, 1f ), Time.Delta * 10f );
		_joystickY = _joystickY.LerpTo( lookDelta.pitch.Clamp( -1f, 1f ), Time.Delta * 10f );

		WeaponModel?.Renderer?.Set( "joystick_x", _joystickX );
		WeaponModel?.Renderer?.Set( "joystick_y", _joystickY );
	}

	public void SetIsUsingJoystick( bool isUsing )
	{
		WeaponModel?.Renderer?.Set( "b_joystick", isUsing );
	}

	/// <summary>
	/// Updates the viewmodel screen render target and redraws it.
	/// </summary>
	protected void UpdateViewmodelScreen()
	{
		if ( !ViewModel.IsValid() ) return;

		var modelRenderer = ViewModel.GetComponentInChildren<SkinnedModelRenderer>();
		if ( !modelRenderer.IsValid() ) return;

		// Clear any previously assigned command list so it only executes once per update
		modelRenderer.ExecuteBefore = null;

		if ( ScreenRefreshInterval > 0f && _lastScreenUpdate < ScreenRefreshInterval )
			return;

		_lastScreenUpdate = 0;

		var materials = modelRenderer.Model.Materials;
		var screenMaterialName = ScreenMaterialName;
		var index = -1;
		for ( var i = 0; i < materials.Length; i++ )
		{
			if ( materials[i].Name.Contains( screenMaterialName ) )
			{
				index = i;
				break;
			}
		}

		if ( index < 0 ) return;

		var oldMaterial = materials[index];

		_screenTexture ??= Texture.CreateRenderTarget().WithSize( ScreenTextureSize.x, ScreenTextureSize.y ).WithFormat( ImageFormat.RGBA16161616F ).WithInitialColor( Color.Black )
			.Create();

		_screenMaterialCopy ??= Material.Load( ScreenMaterialPath ).CreateCopy();
		_screenMaterialCopy.Attributes.Set( "Emissive", _screenTexture );

		var attributes = modelRenderer.SceneObject.Attributes;
		attributes.Set( "Emissive", _screenTexture );

		// the art of the model's own screen (glass, panels), for screen shaders that draw it over their picture
		attributes.Set( "PanelColor", oldMaterial.GetTexture( "g_tColor" ) );
		attributes.Set( "PanelNormal", oldMaterial.GetTexture( "g_tNormal" ) );
		attributes.Set( "PanelAmbientOcclusion", oldMaterial.GetTexture( "g_tAmbientOcclusion" ) );

		// the screen flares up during a transition and settles back
		attributes.Set( "ScreenIntensity", MathX.Lerp( TransitionIntensity, 1f, TransitionProgress ) );

		modelRenderer.Materials.SetOverride( index, _screenMaterialCopy );

		UpdateViewScreenCommandList( modelRenderer );
	}

	private void UpdateViewScreenCommandList( SkinnedModelRenderer renderer )
	{
		var rt = _screenRenderTarget ??= RenderTarget.From( _screenTexture );

		var cl = _screenCommandList ??= new CommandList( "ScreenWeaponViewScreen" );
		cl.Reset();
		renderer.ExecuteBefore = cl;

		cl.SetRenderTarget( rt );

		var rect = new Rect( 0, _screenTexture.Size );
		using ( var paint = Painter.Begin( cl, rect ) )
		{
			paint.Clear( Color.Black );
			DrawScreenContent( rect, paint );
		}

		cl.ClearRenderTarget();
	}

	/// <summary>
	/// Override this to draw custom content onto the viewmodel screen.
	/// </summary>
	protected virtual void DrawScreenContent( Rect rect, Painter paint )
	{
	}
}
