namespace Editor.VisemeEditor;

public class Preview : Widget
{
	public Model Model { set => Rendering.Model = value; }

	private readonly RenderingWidget Rendering;

	public Preview( Widget parent ) : base( parent )
	{
		Name = "Preview";
		WindowTitle = "Preview";
		SetWindowIcon( "photo" );

		Layout = Layout.Column();

		Rendering = new RenderingWidget( this );
		Layout.Add( Rendering );
	}

	public void SetMorphs( Dictionary<string, float> morphs )
	{
		Rendering.SetMorphs( morphs );
	}

	public void SetMorph( string key, float value )
	{
		Rendering.SetMorph( key, value );
	}

	private class RenderingWidget : SceneRenderingWidget
	{
		private SceneWorld World;
		public SceneModel SceneObject { get; private set; }

		public Model Model { set => CreateSceneObject( value ); }

		private void CreateSceneObject( Model model )
		{
			if ( model == null || model.IsError )
				return;

			if ( model.MorphCount == 0 )
				return;

			if ( SceneObject.IsValid() )
			{
				SceneObject.Delete();
				SceneObject = null;
			}

			SceneObject = new SceneModel( World, model, Transform.Zero.WithPosition( Vector3.Backward * 250 ) );
			SceneObject.UseAnimGraph = false;
		}

		public RenderingWidget( Widget parent ) : base( parent )
		{
			MouseTracking = true;
			FocusMode = FocusMode.Click;

			Scene = Scene.CreateEditorScene();

			using ( Scene.Push() )
			{
				{
					Camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>( false );
					Camera.ZNear = 0.1f;
					Camera.ZFar = 4000;
					Camera.LocalRotation = new Angles( 0, 180, 0 );
					Camera.FieldOfView = 10;
					Camera.BackgroundColor = Color.Transparent;
					Camera.Enabled = true;
				}
			}

			World = Scene.SceneWorld;

			new ScenePointLight( World, new Vector3( 100, 100, 100 ), 500, Color.White * 4 ).ShadowsEnabled = false;
			new ScenePointLight( World, new Vector3( -100, -100, 100 ), 500, Color.White * 4 ).ShadowsEnabled = false;
		}

		protected override void PreFrame()
		{
			if ( !SceneObject.IsValid() )
				return;

			SceneObject.Update( RealTime.Delta );

			var position = Vector3.Zero;
			var attachment = SceneObject.GetAttachment( "eyes" );
			if ( attachment.HasValue )
				position = attachment.Value.Position;

			Camera.WorldPosition = position + Vector3.Down * 0.5f + Camera.WorldRotation.Backward * 110;
		}

		public override void OnDestroyed()
		{
			base.OnDestroyed();

			Scene?.Destroy();
			Scene = null;
		}

		public void SetMorphs( Dictionary<string, float> morphs )
		{
			if ( !SceneObject.IsValid() )
				return;

			SceneObject.Morphs.ResetAll();

			if ( morphs != null )
			{
				foreach ( var morph in morphs )
				{
					SceneObject.Morphs.Set( morph.Key, morph.Value );
				}
			}
		}

		public void SetMorph( string key, float value )
		{
			if ( !SceneObject.IsValid() )
				return;

			SceneObject.Morphs.Set( key, value );
		}
	}
}
