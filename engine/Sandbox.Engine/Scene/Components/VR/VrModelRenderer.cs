namespace Sandbox.VR;

/// <summary>
/// Renders a device-specific model for a VR device
/// </summary>
[Title( "VR Model Renderer" )]
[Category( "VR" )]
[Icon( "view_in_ar" )]
public class VRModelRenderer : Component
{
	/// <summary>
	/// Represents a controller to use when fetching the model (which device)
	/// </summary>
	public enum ModelSources
	{
		/// <summary>
		/// The left controller
		/// </summary>
		LeftHand,

		/// <summary>
		/// The right controller
		/// </summary>
		RightHand
	}

	private ModelSources _modelSource = ModelSources.LeftHand;

	/// <summary>
	/// Which device should we use to fetch the model?
	/// </summary>
	[Property]
	public ModelSources ModelSource
	{
		get => _modelSource;
		set
		{
			_modelSource = value;
			UpdateModel();
		}
	}

	/// <summary>
	/// Which model renderer should we use as the target?
	/// </summary>
	[Property]
	public ModelRenderer ModelRenderer { get; set; }

	protected override void OnStart()
	{
		UpdateModel();
	}

	private void UpdateModel()
	{
		if ( !Enabled || Scene.IsEditor || !Game.IsRunningInVR )
			return;

		if ( ModelRenderer == null )
			return;

		if ( IsProxy )
			return;

		var hand = (ModelSource == ModelSources.LeftHand) ? Input.VR.LeftHand : Input.VR.RightHand;
		ModelRenderer.Model = hand.GetModel() ?? Model.Load( "models/dev/box.vmdl" );
	}
}
