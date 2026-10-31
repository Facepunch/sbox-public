using Sandbox;
using Sandbox.VR;

namespace SboxMenu;

/// <summary>
/// Draws a laser along this object's forward axis, fading out towards its end, stopping where it hits a world panel.
/// Put it next to a <see cref="Sandbox.WorldInput"/> so the laser matches what it's interacting with.
/// </summary>
[Title( "VR Pointer" ), Category( "VR" ), Icon( "highlight_alt" )]
public sealed class VRPointer : Component
{
	/// <summary>
	/// Color of the laser.
	/// </summary>
	[Property] public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// Width of the laser.
	/// </summary>
	[Property, Range( 0.01f, 2.0f )] public float Width { get; set; } = 0.15f;

	/// <summary>
	/// How long the laser is when it isn't hitting a panel.
	/// </summary>
	[Property] public float Length { get; set; } = 32.0f;

	LineRenderer _beam;

	protected override void OnEnabled()
	{
		var go = new GameObject( GameObject, true, "Beam" );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.Hidden;

		_beam = go.AddComponent<LineRenderer>();
		_beam.UseVectorPoints = true;
		_beam.VectorPoints = new();
		_beam.Face = SceneLineObject.FaceMode.Camera;
		_beam.Opaque = false;
		_beam.Additive = true;
		_beam.CastShadows = false;
		_beam.FogStrength = 0.0f;
	}

	protected override void OnDisabled()
	{
		_beam?.GameObject.Destroy();
		_beam = null;
	}

	protected override void OnUpdate()
	{
		if ( !_beam.IsValid() )
			return;

		var ray = new Ray( WorldPosition, WorldRotation.Forward );
		var hit = TracePanels( ray, out var distance );
		var length = hit ? distance : Length;

		// Pull the trigger and the laser gets brighter
		var press = GetTriggerValue();

		// Fade in just off the controller, then fade out towards the end. A laser
		// that hits something stays faintly visible right up to the panel.
		var fadeIn = MathF.Min( 2.0f, length * 0.25f );
		_beam.VectorPoints.Clear();
		_beam.VectorPoints.Add( ray.Position );
		_beam.VectorPoints.Add( ray.Project( fadeIn ) );
		_beam.VectorPoints.Add( ray.Project( length ) );
		_beam.Color = CreateGradient( 0.0f, 0.6f + 0.4f * press, hit ? 0.3f : 0.0f );
		_beam.Width = Width;
	}

	/// <summary>
	/// Find the closest world panel along the ray, the same way <see cref="Sandbox.WorldInput"/> does.
	/// </summary>
	bool TracePanels( Ray ray, out float distance )
	{
		distance = float.MaxValue;

		foreach ( var worldPanel in Scene.GetAllComponents<Sandbox.WorldPanel>() )
		{
			var panel = worldPanel.GetPanel();
			if ( panel is null || !panel.IsVisible )
				continue;

			if ( panel.RayToLocalPosition( ray, out _, out var panelDistance ) && panelDistance < distance )
				distance = panelDistance;
		}

		return distance < float.MaxValue;
	}

	float GetTriggerValue()
	{
		if ( !Game.IsRunningInVR )
			return 0.0f;

		var worldInput = GetComponent<Sandbox.WorldInput>();
		var source = worldInput.IsValid() ? worldInput.VRHandSource : VRHand.HandSources.Right;
		var hand = source == VRHand.HandSources.Left ? Input.VR.LeftHand : Input.VR.RightHand;

		return hand.Trigger.Value;
	}

	Gradient CreateGradient( float startAlpha, float alpha, float endAlpha )
	{
		var gradient = new Gradient();
		gradient.AddColor( 0.0f, Tint );
		gradient.AddAlpha( 0.0f, startAlpha );
		gradient.AddAlpha( 0.5f, alpha );
		gradient.AddAlpha( 1.0f, endAlpha );
		return gradient;
	}
}
