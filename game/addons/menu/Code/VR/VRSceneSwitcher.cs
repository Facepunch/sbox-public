using Sandbox;

namespace Menu;

[Title( "VR Scene Switcher" )]
public sealed class VRSceneSwitcher : Component
{
	[Property] public SceneFile TargetScene { get; set; }

	protected override void OnFixedUpdate()
	{
		// We were doing this in OnAwake before, but it was causing NRE issues
		// so we just do it on first fixed update instead
		if ( !TargetScene.IsValid() )
		{
			Log.Warning( "VRSceneSwitcher: target scene isn't valid - bailing" );
			return;
		}

		if ( Game.IsRunningInVR )
		{
			Log.Info( "VRSceneSwitcher: Detected VR running, switching to VR scene" );

			var loadOptions = new SceneLoadOptions()
			{
				ShowLoadingScreen = false
			};
			loadOptions.SetScene( TargetScene );

			Scene.Load( loadOptions );
		}
	}
}
