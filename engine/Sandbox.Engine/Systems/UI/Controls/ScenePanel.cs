// Supporting Obsolete paths
#pragma warning disable CS0612
#pragma warning disable CS0618

using Sandbox.Rendering;

namespace Sandbox.UI
{
	/// <summary>
	/// Allows to render a scene world onto a panel.
	/// </summary>
	[Expose]
	[Library( "scene" )]
	public partial class ScenePanel : Panel
	{
		/// <summary>
		/// Shortcut to Camera.World
		/// </summary>
		[Obsolete( "Handling SceneObjects like this manually will be removed soon. Use the actual Scene." )]
		public SceneWorld World
		{
			get => Camera.World;
			set => Camera.World = value;
		}

		/// <summary>
		/// The camera we're going to be using to render
		/// </summary>
		[Obsolete( "Handling SceneObjects like this manually will be removed soon. Use the actual Scene." )]
		public SceneCamera Camera { get; } = new( "Scene Panel" );

		/// <summary>
		/// If enabled, the scene will only render once. That isn't totally accurate though, because we'll
		/// also re-render the scene when the size of the panel changes.
		/// </summary>
		public bool RenderOnce { get; set; }

		/// <summary>
		/// The texture that the panel is rendering to internally. This will change to a different
		/// texture if the panel changes size, so I wouldn't hold onto this object.
		/// </summary>
		public Texture RenderTexture { get; private set; }

		/// <summary>
		/// The Scene this panel renders.
		/// </summary>
		public Scene RenderScene
		{
			get => _renderScene;
			set
			{
				if ( ReferenceEquals( _renderScene, value ) )
					return;

				// Clean up the existing scene if we own it
				if ( _ownsScene && _renderScene.IsValid() )
				{
					_renderScene.Destroy();
				}

				_renderScene = value;
				_ownsScene = false;
			}
		}

		private Scene _renderScene;
		private bool _ownsScene;

		public ScenePanel()
		{
			_renderScene = new() { WantsSystemScene = false };
			_ownsScene = true;

			Camera.FieldOfView = 60;
			Camera.BackgroundColor = Color.Transparent;
			Camera.ZNear = 0.1f;
			Camera.ZFar = 1000.0f;
		}

		/// <summary>
		/// Creates and loads a Scene from a file to render to this panel.
		/// </summary>
		public ScenePanel( string sceneFilename ) : this()
		{
			var options = new SceneLoadOptions { ShowLoadingScreen = false };
			if ( !options.SetScene( sceneFilename ) )
				return;

			RenderScene.Load( options );
		}

		public override void Tick()
		{
			base.Tick();

			if ( !RenderScene.IsValid() )
				return;

			using ( RenderScene.Push() )
			{
				RenderScene.GameTick( RealTime.Delta );
			}

			if ( Box.RectInner.Size.x <= 0 ) return;
			if ( Box.RectInner.Size.y <= 0 ) return;

			// work out whether we should actually render the scene
			bool shouldRender = shouldRenderNextFrame;
			if ( !RenderOnce ) shouldRender = true;
			if ( RenderTexture == null ) shouldRender = true;

			var oldRt = RenderTexture;
			RenderTexture = Texture.CreateRenderTarget( "__scenePanel", ImageFormat.RGBA8888, Box.RectInner.Size, RenderTexture );
			if ( RenderTexture == null ) return;

			// Texture changed - force an update
			if ( oldRt != RenderTexture )
			{
				shouldRender = true;
				IsRenderDirty = true;
			}

			if ( shouldRender )
			{
				// reset
				shouldRenderNextFrame = false;

				if ( Camera.World.IsValid() )
				{
					Camera.RenderToTexture( RenderTexture, null, default );
				}
				else if ( RenderScene.IsValid() && RenderScene.Camera.IsValid() )
				{
					RenderScene.PreCameraRender(); // TODO WTF?... terrible hack to get around Graphics.IsActive guard in RenderToTexture
					RenderScene.Camera.RenderToTexture( RenderTexture );
				}
			}
		}

		internal bool shouldRenderNextFrame = true;

		/// <summary>
		/// Render the panel again next frame. This is meant to be used with RenderOnce, where
		/// you might want to render on demand or only once.
		/// </summary>
		public void RenderNextFrame() => shouldRenderNextFrame = true;

		public override void Delete( bool immediate = false )
		{
			RenderTexture?.Dispose();
			RenderTexture = null;

			// Only destroy the scene if we created it ourselves, if the user created it themselves then they should be the one destroying it
			if ( _ownsScene )
			{
				_renderScene?.Destroy();
			}

			_renderScene = null;

			base.Delete( immediate );
		}

		public override void OnDraw()
		{
			if ( Box.RectInner.Size.x <= 0 ) return;
			if ( Box.RectInner.Size.y <= 0 ) return;

			DrawBackgroundTexture( RenderTexture, Length.Contain );
		}

		public override void SetProperty( string name, string value )
		{
			base.SetProperty( name, value );
		}
	}

	namespace Construct
	{
		public static class SceneConstructor
		{
			[Obsolete( "Will be deleted if anyone for some reason still uses this" )]
			public static ScenePanel ScenePanel( this PanelCreator self, SceneWorld world, Vector3 position, Rotation rotation, float fieldOfView, string classname = null )
			{
				var control = self.panel.AddChild<ScenePanel>();
				control.World = world;
				control.Camera.Position = position;
				control.Camera.Rotation = rotation;
				control.Camera.FieldOfView = fieldOfView;

				if ( classname != null )
					control.AddClass( classname );
				return control;
			}
		}
	}
}
