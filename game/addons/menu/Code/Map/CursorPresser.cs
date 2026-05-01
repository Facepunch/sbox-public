namespace Sandbox.Mapping;

[Category( "Gameplay" ), Icon( "mouse" )]
public sealed class CursorPresser : Component
{
	CameraComponent _camera;

	protected override void OnStart()
	{
		_camera = GetComponent<CameraComponent>();
	}

	protected override void OnUpdate()
	{
		if ( !_camera.IsValid() )
			return;

		if ( !Input.Keyboard.Pressed( "MOUSE1" ) )
			return;

		var ray = _camera.ScreenPixelToRay( Mouse.Position );

		var tr = Scene.Trace
			.Ray( ray, 10000.0f )
			.IgnoreGameObjectHierarchy( GameObject )
			.HitTriggers()
			.Run();

		if ( !tr.Hit )
			return;

		var hitObject = tr.Collider?.GameObject ?? tr.GameObject;
		if ( !hitObject.IsValid() )
			return;

		if ( hitObject.GetComponent<IPressable>() is not { } pressable )
			return;

		var ev = new IPressable.Event { Ray = ray, Source = this };

		if ( pressable.CanPress( ev ) )
		{
			pressable.Press( ev );
		}
	}
}
