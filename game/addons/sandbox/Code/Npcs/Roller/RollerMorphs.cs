using Sandbox.Utility;

namespace Sandbox.Npcs.Roller;

/// <summary>
/// Drives morph targets and material glow on the rollermine mesh based on hunting state.
/// Same mesh as hoverball — uses Coils_Deployed and Pins_Deployed morphs.
/// </summary>
public sealed class RollerMorphs : Component
{
	private RollerNpc _rollermine;
	private SkinnedModelRenderer _renderer;
	private Material _glowMaterialCopy;

	private float _coils;
	private float _pins;
	private float _brightnessTarget;
	private float _brightnessCurrent;
	private float _brightnessTimer;

	private float _coilsFrom;
	private float _coilsTo;
	private float _coilsTime;
	private float _pinsFrom;
	private float _pinsTo;
	private float _pinsTime;

	[Property] public float Speed { get; set; } = 15f;
	public float TransitionDuration => 0.3f;
	[Property] public Material GlowMaterial { get; set; }

	public Color IllumTint => Color.FromBytes( 20, 165, 200 );
	public float IllumBrightness => 8f;

	protected override void OnStart()
	{
		_rollermine = GetComponent<RollerNpc>();
		_renderer = GetComponentInChildren<SkinnedModelRenderer>();

		if ( GlowMaterial is not null && _renderer.IsValid() )
		{
			_glowMaterialCopy = GlowMaterial.CreateCopy();
			_renderer.MaterialOverride = _glowMaterialCopy;
			_renderer.SceneModel.Batchable = false;
		}
	}

	protected override void OnUpdate()
	{
		if ( !_rollermine.IsValid() || !_renderer.IsValid() ) return;

		var hunting = _rollermine.IsHunting;

		var targetCoils = hunting ? 1f : 0f;
		var targetPins = hunting ? 1f : 0f;

		if ( targetCoils != _coilsTo )
		{
			_coilsFrom = _coils;
			_coilsTo = targetCoils;
			_coilsTime = 0f;
		}

		if ( targetPins != _pinsTo )
		{
			_pinsFrom = _pins;
			_pinsTo = targetPins;
			_pinsTime = 0f;
		}

		_coilsTime = Math.Min( _coilsTime + Time.Delta / TransitionDuration, 1f );
		_pinsTime = Math.Min( _pinsTime + Time.Delta / TransitionDuration, 1f );

		_coils = MathX.Lerp( _coilsFrom, _coilsTo, Easing.BounceOut( _coilsTime ) );
		_pins = MathX.Lerp( _pinsFrom, _pinsTo, Easing.BounceOut( _pinsTime ) );

		_renderer.SceneModel?.Morphs.Set( "Coils_Deployed", _coils );
		_renderer.SceneModel?.Morphs.Set( "Pins_Deployed", _pins );

		UpdateGlowMaterial();
	}

	void UpdateGlowMaterial()
	{
		if ( _glowMaterialCopy is null ) return;

		var hunting = _rollermine.IsHunting;
		var brightness = hunting ? IllumBrightness : 0f;

		if ( hunting )
		{
			_brightnessTimer -= Time.Delta;
			if ( _brightnessTimer <= 0f )
			{
				_brightnessTarget = Random.Shared.Float( 6f, 8f );
				_brightnessTimer = Random.Shared.Float( 0.1f, 0.4f );
			}
			_brightnessCurrent = MathX.Approach( _brightnessCurrent, _brightnessTarget, Time.Delta * 7f );
			brightness = _brightnessCurrent;
		}

		_glowMaterialCopy.Set( "g_vSelfIllumTint", hunting ? IllumTint : Color.Black );
		_glowMaterialCopy.Set( "g_flSelfIllumBrightness", brightness * _coils );
	}
}
