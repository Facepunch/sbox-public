namespace Editor.SoundEditor;

public class Preview : Widget
{
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

	public void AddVisemes( List<PhonemeFrame> phonemes, float t, float dt )
	{
		Rendering.AddVisemes( phonemes, t, dt );
	}

	private class RenderingWidget : SceneRenderingWidget
	{
		public SceneModel SceneObject { get; private set; }

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

			var world = Scene.SceneWorld;

			new ScenePointLight( world, new Vector3( 100, 100, 100 ), 500, Color.White * 4 ).ShadowsEnabled = false;
			new ScenePointLight( world, new Vector3( -100, -100, 100 ), 500, Color.White * 4 ).ShadowsEnabled = false;
			SceneObject = new SceneModel( world, "models/citizen/citizen.vmdl", Transform.Zero.WithPosition( Vector3.Backward * 250 ) );
		}

		public void AddVisemes( List<PhonemeFrame> phonemes, float t, float dt )
		{
			SceneObject.Morphs.ResetAll();

			int pcount = phonemes.Count;
			for ( int k = 0; k < pcount; k++ )
			{
				var phoneme = phonemes[k];
				float phonemeStartTime = phoneme.StartTime;
				float phonemeEndTime = phoneme.EndTime;

				if ( t > phonemeStartTime && t < phonemeEndTime )
				{
					if ( k < pcount - 1 )
					{
						var next = phonemes[k + 1];
						float nextStartTime = next.StartTime;
						float nextEndTime = next.EndTime;

						// Determine the blend length based on the current and next phoneme
						if ( nextStartTime == phonemeEndTime )
						{
							// No gap, increase the blend length to the end of the next phoneme
							dt = MathF.Max( dt, MathF.Min( nextEndTime - t, phonemeEndTime - phonemeStartTime ) );
						}
						else
						{
							// Dead space, increase the blend length to the start of the next phoneme
							dt = MathF.Max( dt, MathF.Min( nextStartTime - t, phonemeEndTime - phonemeStartTime ) );
						}
					}
					else
					{
						// Last phoneme in list, increase the blend length to the length of the current phoneme
						dt = MathF.Max( dt, phonemeEndTime - phonemeStartTime );
					}
				}

				float t1 = (phonemeStartTime - t) / dt;
				float t2 = (phonemeEndTime - t) / dt;

				// Check for overlap of the current time t with the phoneme duration
				if ( t1 < 1.0f && t2 > 0.0f )
				{
					t1 = MathF.Max( t1, 0 );
					t2 = MathF.Min( t2, 1 );

					float scale = (t2 - t1);
					AddViseme( phoneme.Code, scale );
				}
			}
		}

		public void AddViseme( int phoneme, float scale )
		{
			if ( !SceneObject.IsValid() )
				return;

			var model = SceneObject.Model;
			var morphs = SceneObject.Morphs;

			for ( int i = 0; i < model.MorphCount; ++i )
			{
				var weight = 0.0f;
				if ( weight <= 0.0f )
					continue;

				weight *= scale;
				morphs.Set( i, morphs.Get( i ) + weight );
			}
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

			Camera.WorldPosition = position + Vector3.Down * 0.5f + Camera.WorldRotation.Backward * 120;
		}

		public override void OnDestroyed()
		{
			base.OnDestroyed();

			Scene?.Destroy();
			Scene = null;
		}
	}
}
