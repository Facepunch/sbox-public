namespace Sandbox.PanelGallery;

/// <summary>
/// An owned scene rendered inside the UI, with independent camera and animation controls.
/// </summary>
public class ScenePanelPage : GalleryPage
{
	readonly GameObject _model;
	readonly GameObject _camera;
	float _time;
	float _distance = 180;
	bool _animate = true;

	public ScenePanelPage() : base( "Scene Panel", "A real 3D scene inside a panel. Pause the model or change the camera distance; the scene is owned and cleaned up by ScenePanel." )
	{
		var controls = Add.Panel( "demo-actions" );
		controls.AddChild( new Sandbox.UI.Checkbox { LabelText = "Rotate model", Checked = true, ValueChanged = value => _animate = value } );
		var zoom = new Sandbox.UI.SliderControl( 100, 350, 1 ) { Value = _distance };
		zoom.OnValueChanged = value => _distance = value;
		controls.Add.Label( "Camera distance" );
		controls.AddChild( zoom );
		var panel = new ScenePanel();
		panel.AddClass( "scene-control-demo" );
		AddChild( panel );
		var scene = panel.RenderScene;
		_camera = scene.CreateObject();
		var camera = _camera.Components.Create<CameraComponent>();
		camera.BackgroundColor = "#101b2b";
		camera.FieldOfView = 45;
		_model = scene.CreateObject();
		var renderer = _model.Components.Create<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		renderer.Tint = GalleryPalette.CyanHex;
		var light = scene.CreateObject();
		light.WorldRotation = Rotation.From( 45, 30, 0 );
		light.Components.Create<DirectionalLight>().LightColor = Color.White * 2;
		UseSceneLayout( panel, camera );
	}

	public override void Tick()
	{
		base.Tick();
		if ( _animate ) _time += RealTime.Delta;
		_model.WorldRotation = Rotation.From( 15, _time * 30, 15 );
		_camera.WorldPosition = new Vector3( -_distance, _distance * 0.45f, _distance * 0.4f );
		_camera.WorldRotation = Rotation.LookAt( -_camera.WorldPosition );
	}
}
