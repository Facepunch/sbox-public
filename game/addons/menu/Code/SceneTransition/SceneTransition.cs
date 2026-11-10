namespace Sandbox;

/// <summary>
/// A custom scene transition that creates an overlay camera + UI panel,
/// animates in to cover the screen, loads the target scene behind it,
/// then animates out and destroys itself.
/// </summary>
public static class SceneTransition
{
	/// <summary>
	/// Transition to a scene with a custom animated overlay instead of the default loading screen.
	/// </summary>
	public static void Load( string scenePath )
	{
		var options = new SceneLoadOptions();
		options.ShowLoadingScreen = false;
		options.SetScene( scenePath );

		Load( options );
	}

	/// <summary>
	/// Transition to a scene with a custom animated overlay instead of the default loading screen.
	/// </summary>
	public static void Load( SceneLoadOptions options )
	{
		// Disable the default loading screen
		options.ShowLoadingScreen = false;

		// Create a persistent GameObject to hold our transition overlay
		var go = new GameObject( true, "SceneTransition" );
		go.Flags = GameObjectFlags.DontDestroyOnLoad;
		go.NetworkMode = NetworkMode.Never;

		// Add the transition component which manages the animation and scene load
		var transition = go.AddComponent<SceneTransitionController>();
		transition.Options = options;
	}
}

/// <summary>
/// Controls the transition lifecycle: animate in, load scene, animate out, self-destruct.
/// </summary>
public sealed class SceneTransitionController : Component
{
	public SceneLoadOptions Options { get; set; }

	private SceneTransitionPanel _panel;
	private bool _sceneLoadStarted;
	private bool _sceneLoadComplete;

	/// <summary>
	/// How long the wipe-in animation takes.
	/// </summary>
	private const float AnimateInDuration = 0.4f;

	/// <summary>
	/// How long to hold after the scene loads before wiping out.
	/// </summary>
	private const float HoldDuration = 0.15f;

	/// <summary>
	/// How long the wipe-out animation takes.
	/// </summary>
	private const float AnimateOutDuration = 0.5f;

	private enum TransitionState
	{
		AnimatingIn,
		Loading,
		Holding,
		AnimatingOut,
		Done
	}

	private TransitionState _state = TransitionState.AnimatingIn;
	private float _stateTime;

	protected override void OnStart()
	{
		// Create a screen panel on the main camera with a very high ZIndex
		var screenPanel = GameObject.AddComponent<ScreenPanel>();
		screenPanel.ZIndex = 99999;

		// Create the UI panel that covers the screen
		_panel = GameObject.AddComponent<SceneTransitionPanel>();
		_panel.Progress = 0f;
		_panel.Direction = SceneTransitionPanel.WipeDirection.In;
	}

	protected override void OnUpdate()
	{
		_stateTime += Time.Delta;

		switch ( _state )
		{
			case TransitionState.AnimatingIn:
				float inProgress = MathF.Min( _stateTime / AnimateInDuration, 1f );
				_panel.Progress = EaseInOut( inProgress );

				if ( inProgress >= 1f )
				{
					_state = TransitionState.Loading;
					_stateTime = 0f;
					StartSceneLoad();
				}
				break;

			case TransitionState.Loading:
				_panel.Progress = 1f;

				// Poll for scene load completion since events may not reach DontDestroyOnLoad objects
				if ( !Game.ActiveScene.IsLoading )
				{
					_sceneLoadComplete = true;
				}

				if ( _sceneLoadComplete )
				{
					_state = TransitionState.Holding;
					_stateTime = 0f;
					LoadingScreen.IsVisible = false;
				}
				break;

			case TransitionState.Holding:
				_panel.Progress = 1f;

				if ( _stateTime >= HoldDuration )
				{
					_state = TransitionState.AnimatingOut;
					_stateTime = 0f;
					_panel.Direction = SceneTransitionPanel.WipeDirection.Out;
					_panel.Progress = 0f;
				}
				break;

			case TransitionState.AnimatingOut:
				float outProgress = MathF.Min( _stateTime / AnimateOutDuration, 1f );
				_panel.Progress = EaseInOut( outProgress );

				if ( outProgress >= 1f )
				{
					_state = TransitionState.Done;
					GameObject.Destroy();
				}
				break;
		}
	}

	private void StartSceneLoad()
	{
		if ( _sceneLoadStarted ) return;
		_sceneLoadStarted = true;

		LoadingScreen.IsVisible = false;
		Game.ActiveScene.Load( Options );
	}

	private static float EaseInOut( float t ) => t < 0.5f
		? 4f * t * t * t
		: 1f - MathF.Pow( -2f * t + 2f, 3f ) / 2f;
}
