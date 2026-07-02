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

	public void AddVisemes( List<VisemeFrame> visemes, float t, float dt )
	{
		Rendering.AddVisemes( visemes, t, dt );
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

		public void AddVisemes( List<VisemeFrame> visemes, float t, float dt )
		{
			if ( !SceneObject.IsValid() )
				return;

			SceneObject.Morphs.ResetAll();

			if ( visemes == null || visemes.Count == 0 )
				return;

			// Accumulate a weight per viseme for the frames active around time t.
			Span<float> visemeWeights = stackalloc float[LipSyncGenerator.Count];

			int count = visemes.Count;
			for ( int k = 0; k < count; k++ )
			{
				var frame = visemes[k];
				float startTime = frame.StartTime;
				float endTime = frame.EndTime;

				if ( t > startTime && t < endTime )
				{
					if ( k < count - 1 )
					{
						var next = visemes[k + 1];
						float nextStartTime = next.StartTime;
						float nextEndTime = next.EndTime;

						// Determine the blend length based on the current and next viseme
						if ( nextStartTime == endTime )
						{
							// No gap, increase the blend length to the end of the next viseme
							dt = MathF.Max( dt, MathF.Min( nextEndTime - t, endTime - startTime ) );
						}
						else
						{
							// Dead space, increase the blend length to the start of the next viseme
							dt = MathF.Max( dt, MathF.Min( nextStartTime - t, endTime - startTime ) );
						}
					}
					else
					{
						// Last viseme in list, increase the blend length to its own length
						dt = MathF.Max( dt, endTime - startTime );
					}
				}

				float t1 = (startTime - t) / dt;
				float t2 = (endTime - t) / dt;

				// Check for overlap of the current time t with the viseme duration
				if ( t1 < 1.0f && t2 > 0.0f )
				{
					t1 = MathF.Max( t1, 0 );
					t2 = MathF.Min( t2, 1 );

					float scale = (t2 - t1);
					visemeWeights[frame.Viseme] = MathF.Max( visemeWeights[frame.Viseme], scale );
				}
			}

			ApplyVisemes( visemeWeights );
		}

		// Blend the model's morphs towards the accumulated viseme weights, the same way the
		// runtime LipSync component drives a face from lipsync visemes.
		private void ApplyVisemes( ReadOnlySpan<float> visemeWeights )
		{
			var model = SceneObject.Model;
			if ( model is null )
				return;

			var morphs = SceneObject.Morphs;
			int morphCount = model.MorphCount;

			for ( int morphIndex = 0; morphIndex < morphCount; morphIndex++ )
			{
				float morph = 0.0f;
				for ( int v = 0; v < LipSyncGenerator.Count; v++ )
				{
					if ( visemeWeights[v] <= 0.0f )
						continue;

					var weight = model.GetVisemeMorph( LipSyncGenerator.MorphName( v ), morphIndex );
					morph += (weight - morph) * visemeWeights[v];
				}

				morphs.Set( morphIndex, Math.Clamp( morph, 0f, 1f ) );
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
