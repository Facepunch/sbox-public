using System.Collections;
using static Sandbox.Component;

namespace Sandbox.MovieMaker;

#nullable enable

[Expose]
file sealed class ComponentCapturer : ComponentCapturer<Component>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, Component component )
	{
		recorder.Property( nameof( Component.Enabled ) ).Capture();
	}
}

[Expose]
file sealed class CameraCapturer : ComponentCapturer<CameraComponent>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, CameraComponent component )
	{
		recorder.Property( nameof( CameraComponent.Priority ) ).Capture();
		recorder.Property( nameof( CameraComponent.IsMainCamera ) ).Capture();
		recorder.Property( nameof( CameraComponent.BackgroundColor ) ).Capture();
		recorder.Property( nameof( CameraComponent.Orthographic ) ).Capture();

		if ( component.Orthographic )
		{
			recorder.Property( nameof( CameraComponent.OrthographicHeight ) ).Capture();
		}
		else
		{
			recorder.Property( nameof( CameraComponent.FovAxis ) ).Capture();
			recorder.Property( nameof( CameraComponent.FieldOfView ) ).Capture();
		}

		recorder.Property( nameof( CameraComponent.ZNear ) ).Capture();
		recorder.Property( nameof( CameraComponent.ZFar ) ).Capture();
	}
}

[Expose]
file sealed class ModelRendererCapturer : ComponentCapturer<ModelRenderer>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, ModelRenderer component )
	{
		recorder.Property( nameof( ModelRenderer.Model ) ).Capture();
		recorder.Property( nameof( ModelRenderer.Tint ) ).Capture();
		recorder.Property( nameof( ModelRenderer.MaterialOverride ) ).Capture();
		recorder.Property( nameof( ModelRenderer.RenderType ) ).Capture();

		if ( component.HasBodyGroups )
		{
			recorder.Property( nameof( ModelRenderer.BodyGroups ) ).Capture();
		}
	}
}

[Expose]
file sealed class SkinnedModelRendererCapturer : ComponentCapturer<SkinnedModelRenderer>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, SkinnedModelRenderer component )
	{
		recorder.Property( nameof( SkinnedModelRenderer.CreateBoneObjects ) ).Capture();
		recorder.Property( nameof( SkinnedModelRenderer.BoneMergeTarget ) ).Capture();

		if ( component.BoneMergeTarget is not null ) return;

		recorder.Property( nameof( SkinnedModelRenderer.UseAnimGraph ) ).Capture();

		var sceneModel = component.SceneModel;

		if ( sceneModel.IsValid() && sceneModel.HasBoneOverrides() )
		{
			if ( component.Model is not { } model ) return;

			var bonesTrack = recorder.Property( "Bones" );

			for ( var i = 0; i < model.BoneCount; i++ )
			{
				var boneName = model.GetBoneName( i );

				bonesTrack.Property( boneName ).Capture();
			}
		}
		else if ( component.UseAnimGraph )
		{
			if ( component.Parameters.Graph is not { } graph ) return;

			var parametersTrack = recorder.Property( nameof( SkinnedModelRenderer.Parameters ) );

			for ( var i = 0; i < graph.ParamCount; i++ )
			{
				var paramName = graph.ParameterList.GetParameter( i ).GetName();

				if ( component.Parameters.Contains( paramName ) )
				{
					parametersTrack.Property( paramName ).Capture();
				}
			}
		}
		else
		{
			var sequenceTrack = recorder.Property( nameof( SkinnedModelRenderer.Sequence ) );

			sequenceTrack.Property( nameof( SkinnedModelRenderer.SequenceAccessor.Name ) ).Capture();
			sequenceTrack.Property( nameof( SkinnedModelRenderer.SequenceAccessor.PlaybackRate ) ).Capture();
			sequenceTrack.Property( nameof( SkinnedModelRenderer.SequenceAccessor.Blending ) ).Capture();
			sequenceTrack.Property( nameof( SkinnedModelRenderer.SequenceAccessor.Looping ) ).Capture();
		}
	}
}

[Expose]
file sealed class TrailRendererCapturer : ComponentCapturer<TrailRenderer>
{
	public static void CaptureTrailTextureConfig( IMovieTrackRecorder recorder )
	{
		recorder.Property( nameof( LineRenderer.Texturing.TextureAddressMode ) ).Capture();
		recorder.Property( nameof( LineRenderer.Texturing.Material ) ).Capture();
		recorder.Property( nameof( LineRenderer.Texturing.Offset ) ).Capture();
		recorder.Property( nameof( LineRenderer.Texturing.Scale ) ).Capture();
		recorder.Property( nameof( LineRenderer.Texturing.Scroll ) ).Capture();
		recorder.Property( nameof( LineRenderer.Texturing.UnitsPerTexture ) ).Capture();
		recorder.Property( nameof( LineRenderer.Texturing.WorldSpace ) ).Capture();
	}

	protected override void OnCapture( IMovieTrackRecorder recorder, TrailRenderer component )
	{
		recorder.Property( nameof( TrailRenderer.MaxPoints ) ).Capture();
		recorder.Property( nameof( TrailRenderer.PointDistance ) ).Capture();
		recorder.Property( nameof( TrailRenderer.LifeTime ) ).Capture();
		recorder.Property( nameof( TrailRenderer.Emitting ) ).Capture();

		CaptureTrailTextureConfig( recorder.Property( nameof( TrailRenderer.Texturing ) ) );

		recorder.Property( nameof( TrailRenderer.Color ) ).Capture();
		recorder.Property( nameof( TrailRenderer.Width ) ).Capture();
		recorder.Property( nameof( TrailRenderer.Face ) ).Capture();
		recorder.Property( nameof( TrailRenderer.Wireframe ) ).Capture();
		recorder.Property( nameof( TrailRenderer.Opaque ) ).Capture();

		if ( component.Opaque )
		{
			recorder.Property( nameof( TrailRenderer.CastShadows ) ).Capture();
		}
		else
		{
			recorder.Property( nameof( TrailRenderer.BlendMode ) ).Capture();
		}
	}
}

[Expose]
file sealed class LineRendererCapturer : ComponentCapturer<LineRenderer>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, LineRenderer component )
	{
		recorder.Property( nameof( LineRenderer.Additive ) ).Capture();
		recorder.Property( nameof( LineRenderer.CastShadows ) ).Capture();
		recorder.Property( nameof( LineRenderer.Color ) ).Capture();
		recorder.Property( nameof( LineRenderer.DepthFeather ) ).Capture();

		if ( component.Face != SceneLineObject.FaceMode.Cylinder )
		{
			recorder.Property( nameof( LineRenderer.StartCap ) ).Capture();
			recorder.Property( nameof( LineRenderer.EndCap ) ).Capture();
		}

		if ( component.Face != SceneLineObject.FaceMode.Camera )
		{
			recorder.Property( nameof( LineRenderer.AutoCalculateNormals ) ).Capture();
		}

		recorder.Property( nameof( LineRenderer.Lighting ) ).Capture();
		recorder.Property( nameof( LineRenderer.Opaque ) ).Capture();
		recorder.Property( nameof( LineRenderer.SplineBias ) ).Capture();
		recorder.Property( nameof( LineRenderer.SplineContinuity ) ).Capture();
		recorder.Property( nameof( LineRenderer.SplineInterpolation ) ).Capture();
		recorder.Property( nameof( LineRenderer.SplineTension ) ).Capture();
		recorder.Property( nameof( LineRenderer.Width ) ).Capture();

		TrailRendererCapturer.CaptureTrailTextureConfig( recorder.Property( nameof( LineRenderer.Texturing ) ) );

		recorder.Property( nameof( LineRenderer.UseVectorPoints ) ).Capture();

		if ( component.UseVectorPoints )
		{
			if ( component.VectorPoints is null ) return;

			var vectorPointsTrack = recorder.Property( nameof( LineRenderer.VectorPoints ) );

			vectorPointsTrack.Property( nameof( IList.Count ) ).Capture();

			for ( var i = 0; i < component.VectorPoints.Count; i++ )
			{
				vectorPointsTrack.Property( i.ToString() ).Capture();
			}
		}
		else
		{
			if ( component.Points is null ) return;

			var pointsTrack = recorder.Property( nameof( LineRenderer.Points ) );

			pointsTrack.Property( nameof( IList.Count ) ).Capture();

			for ( var i = 0; i < component.Points.Count; i++ )
			{
				pointsTrack.Property( i.ToString() ).Capture();
			}
		}
	}
}


[Expose]
file sealed class TextRendererCapturer : ComponentCapturer<TextRenderer>
{
	public static void CaptureTextRenderingScope( IMovieTrackRecorder recorder )
	{
		recorder.Property( nameof( TextRendering.Scope.Text ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.TextColor ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.FontName ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.FontSize ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.FontWeight ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.FontItalic ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.LineHeight ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.LetterSpacing ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.WordSpacing ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.FilterMode ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.FontSmooth ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.Outline ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.Shadow ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.OutlineUnder ) ).Capture();
		recorder.Property( nameof( TextRendering.Scope.ShadowUnder ) ).Capture();
	}

	protected override void OnCapture( IMovieTrackRecorder recorder, TextRenderer component )
	{
		CaptureTextRenderingScope( recorder.Property( nameof( TextRenderer.TextScope ) ) );

		recorder.Property( nameof( TextRenderer.Scale ) ).Capture();
		recorder.Property( nameof( TextRenderer.HorizontalAlignment ) ).Capture();
		recorder.Property( nameof( TextRenderer.VerticalAlignment ) ).Capture();
		recorder.Property( nameof( TextRenderer.BlendMode ) ).Capture();
		recorder.Property( nameof( TextRenderer.FogStrength ) ).Capture();
	}
}

[Expose]
file sealed class ParticleEffectCapturer : ComponentCapturer<ParticleEffect>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, ParticleEffect component )
	{
		recorder.Property( nameof( ParticleEffect.MaxParticles ) ).Capture();
		recorder.Property( nameof( ParticleEffect.Lifetime ) ).Capture();
		recorder.Property( nameof( ParticleEffect.TimeScale ) ).Capture();
		recorder.Property( nameof( ParticleEffect.PreWarm ) ).Capture();
		recorder.Property( nameof( ParticleEffect.StartDelay ) ).Capture();
		recorder.Property( nameof( ParticleEffect.PerParticleTimeScale ) ).Capture();
		recorder.Property( nameof( ParticleEffect.Timing ) ).Capture();

		recorder.Property( nameof( ParticleEffect.InitialVelocity ) ).Capture();
		recorder.Property( nameof( ParticleEffect.StartVelocity ) ).Capture();
		recorder.Property( nameof( ParticleEffect.Damping ) ).Capture();
		recorder.Property( nameof( ParticleEffect.ConstantMovement ) ).Capture();
		recorder.Property( nameof( ParticleEffect.LocalSpace ) ).Capture();

		recorder.Property( nameof( ParticleEffect.ApplyRotation ) ).Capture();

		if ( component.ApplyRotation )
		{
			recorder.Property( nameof( ParticleEffect.Pitch ) ).Capture();
			recorder.Property( nameof( ParticleEffect.Yaw ) ).Capture();
			recorder.Property( nameof( ParticleEffect.Roll ) ).Capture();
		}

		recorder.Property( nameof( ParticleEffect.ApplyColor ) ).Capture();

		if ( component.ApplyColor )
		{
			recorder.Property( nameof( ParticleEffect.ApplyAlpha ) ).Capture();
			recorder.Property( nameof( ParticleEffect.Tint ) ).Capture();
			recorder.Property( nameof( ParticleEffect.Gradient ) ).Capture();
			recorder.Property( nameof( ParticleEffect.Brightness ) ).Capture();
			recorder.Property( nameof( ParticleEffect.Alpha ) ).Capture();
		}

		recorder.Property( nameof( ParticleEffect.ApplyShape ) ).Capture();

		if ( component.ApplyShape )
		{
			recorder.Property( nameof( ParticleEffect.Scale ) ).Capture();
			recorder.Property( nameof( ParticleEffect.Stretch ) ).Capture();
		}

		recorder.Property( nameof( ParticleEffect.Force ) ).Capture();

		if ( component.Force )
		{
			recorder.Property( nameof( ParticleEffect.ForceDirection ) ).Capture();
			recorder.Property( nameof( ParticleEffect.ForceScale ) ).Capture();
			recorder.Property( nameof( ParticleEffect.OrbitalForce ) ).Capture();
			recorder.Property( nameof( ParticleEffect.OrbitalPull ) ).Capture();
			recorder.Property( nameof( ParticleEffect.ForceSpace ) ).Capture();
		}

		recorder.Property( nameof( ParticleEffect.Collision ) ).Capture();

		if ( component.Collision )
		{
			recorder.Property( nameof( ParticleEffect.DieOnCollisionChance ) ).Capture();
			recorder.Property( nameof( ParticleEffect.CollisionRadius ) ).Capture();
			recorder.Property( nameof( ParticleEffect.Bounce ) ).Capture();
			recorder.Property( nameof( ParticleEffect.Friction ) ).Capture();
			recorder.Property( nameof( ParticleEffect.Bumpiness ) ).Capture();
			recorder.Property( nameof( ParticleEffect.PushStrength ) ).Capture();
		}

		recorder.Property( nameof( ParticleEffect.SheetSequence ) ).Capture();

		if ( component.SheetSequence )
		{
			recorder.Property( nameof( ParticleEffect.SequenceId ) ).Capture();
			recorder.Property( nameof( ParticleEffect.SequenceTime ) ).Capture();
			recorder.Property( nameof( ParticleEffect.SequenceSpeed ) ).Capture();
		}
	}
}

[Expose]
file sealed class TemporaryEffectCapturer : ComponentCapturer<ITemporaryEffect>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, ITemporaryEffect component )
	{
		recorder.Property( nameof( ITemporaryEffect.IsActive ) ).Capture();
	}
}

[Expose]
file sealed class ParticleEmitterCapturer : ComponentCapturer<ParticleEmitter>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, ParticleEmitter component )
	{
		recorder.Property( nameof( ParticleEmitter.Loop ) ).Capture();
		recorder.Property( nameof( ParticleEmitter.Duration ) ).Capture();
		recorder.Property( nameof( ParticleEmitter.Delay ) ).Capture();
		recorder.Property( nameof( ParticleEmitter.Burst ) ).Capture();
		recorder.Property( nameof( ParticleEmitter.Rate ) ).Capture();
		recorder.Property( nameof( ParticleEmitter.RateOverDistance ) ).Capture();
	}
}

[Expose]
file sealed class ParticleSphereEmitterCapturer : ComponentCapturer<ParticleSphereEmitter>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, ParticleSphereEmitter component )
	{
		recorder.Property( nameof( ParticleSphereEmitter.Radius ) ).Capture();
		recorder.Property( nameof( ParticleSphereEmitter.Velocity ) ).Capture();
		recorder.Property( nameof( ParticleSphereEmitter.OnEdge ) ).Capture();
	}
}

[Expose]
file sealed class ParticleConeEmitterCapturer : ComponentCapturer<ParticleConeEmitter>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, ParticleConeEmitter component )
	{
		recorder.Property( nameof( ParticleConeEmitter.OnEdge ) ).Capture();
		recorder.Property( nameof( ParticleConeEmitter.InVolume ) ).Capture();
		recorder.Property( nameof( ParticleConeEmitter.ConeAngle ) ).Capture();
		recorder.Property( nameof( ParticleConeEmitter.ConeNear ) ).Capture();
		recorder.Property( nameof( ParticleConeEmitter.ConeFar ) ).Capture();
		recorder.Property( nameof( ParticleConeEmitter.VelocityRandom ) ).Capture();
		recorder.Property( nameof( ParticleConeEmitter.CenterBias ) ).Capture();
		recorder.Property( nameof( ParticleConeEmitter.CenterBiasVelocity ) ).Capture();
		recorder.Property( nameof( ParticleConeEmitter.VelocityMultiplier ) ).Capture();
	}
}

[Expose]
file sealed class ParticleModelEmitterCapturer : ComponentCapturer<ParticleModelEmitter>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, ParticleModelEmitter component )
	{
		recorder.Property( nameof( ParticleModelEmitter.Target ) ).Capture();
		recorder.Property( nameof( ParticleModelEmitter.OnEdge ) ).Capture();
	}
}

[Expose]
file sealed class ParticleSpriteRendererCapturer : ComponentCapturer<ParticleSpriteRenderer>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, ParticleSpriteRenderer component )
	{
		recorder.Property( nameof( ParticleSpriteRenderer.Sprite ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.Scale ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.Additive ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.Shadows ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.Lighting ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.Opaque ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.TextureFilter ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.Alignment ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.SortMode ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.DepthFeather ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.FogStrength ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.FaceVelocity ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.RotationOffset ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.MotionBlur ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.LeadingTrail ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.BlurAmount ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.BlurSpacing ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.BlurOpacity ) ).Capture();
	}
}

[Expose]
file sealed class ParticleTextRendererCapturer : ComponentCapturer<ParticleTextRenderer>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, ParticleTextRenderer component )
	{
		TextRendererCapturer.CaptureTextRenderingScope( recorder.Property( nameof( ParticleTextRenderer.Text ) ) );

		recorder.Property( nameof( ParticleTextRenderer.Pivot ) ).Capture();
		recorder.Property( nameof( ParticleTextRenderer.Scale ) ).Capture();
		recorder.Property( nameof( ParticleTextRenderer.DepthFeather ) ).Capture();
		recorder.Property( nameof( ParticleTextRenderer.FogStrength ) ).Capture();
		recorder.Property( nameof( ParticleTextRenderer.Additive ) ).Capture();
		recorder.Property( nameof( ParticleTextRenderer.Shadows ) ).Capture();
		recorder.Property( nameof( ParticleTextRenderer.Lighting ) ).Capture();
		recorder.Property( nameof( ParticleTextRenderer.Opaque ) ).Capture();
		recorder.Property( nameof( ParticleTextRenderer.TextureFilter ) ).Capture();

		recorder.Property( nameof( ParticleTextRenderer.FaceVelocity ) ).Capture();

		if ( component.FaceVelocity )
		{
			recorder.Property( nameof( ParticleTextRenderer.RotationOffset ) ).Capture();
		}

		recorder.Property( nameof( ParticleTextRenderer.MotionBlur ) ).Capture();

		if ( component.MotionBlur )
		{
			recorder.Property( nameof( ParticleTextRenderer.LeadingTrail ) ).Capture();
			recorder.Property( nameof( ParticleTextRenderer.BlurAmount ) ).Capture();
			recorder.Property( nameof( ParticleTextRenderer.BlurSpacing ) ).Capture();
			recorder.Property( nameof( ParticleTextRenderer.BlurOpacity ) ).Capture();
		}

		recorder.Property( nameof( ParticleTextRenderer.Alignment ) ).Capture();
		recorder.Property( nameof( ParticleTextRenderer.SortMode ) ).Capture();
	}
}

[Expose]
file sealed class MapInstanceCapturer : ComponentCapturer<MapInstance>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, MapInstance component )
	{
		recorder.Property( nameof( MapInstance.EnableCollision ) ).Capture();
		recorder.Property( nameof( MapInstance.MapName ) ).Capture();
	}
}

[Expose]
file sealed class LightCapturer : ComponentCapturer<Light>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, Light component )
	{
		recorder.Property( nameof( Light.LightColor ) ).Capture();
		recorder.Property( nameof( Light.FogMode ) ).Capture();
		recorder.Property( nameof( Light.FogStrength ) ).Capture();
		recorder.Property( nameof( Light.Shadows ) ).Capture();

		if ( component.Shadows )
		{
			recorder.Property( nameof( Light.ShadowBias ) ).Capture();
			recorder.Property( nameof( Light.ShadowHardness ) ).Capture();
		}
	}
}

[Expose]
file sealed class PointLightCapturer : ComponentCapturer<PointLight>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, PointLight component )
	{
		recorder.Property( nameof( PointLight.Radius ) ).Capture();
		recorder.Property( nameof( PointLight.Attenuation ) ).Capture();
	}
}

[Expose]
file sealed class SpotLightCapturer : ComponentCapturer<SpotLight>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, SpotLight component )
	{
		recorder.Property( nameof( SpotLight.Radius ) ).Capture();
		recorder.Property( nameof( SpotLight.ConeOuter ) ).Capture();
		recorder.Property( nameof( SpotLight.ConeInner ) ).Capture();
		recorder.Property( nameof( SpotLight.Attenuation ) ).Capture();
		recorder.Property( nameof( SpotLight.Cookie ) ).Capture();
	}
}

[Expose]
file sealed class DirectionalLightCapturer : ComponentCapturer<DirectionalLight>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, DirectionalLight component )
	{
		recorder.Property( nameof( DirectionalLight.SkyColor ) ).Capture();

		if ( component.Shadows )
		{
			recorder.Property( nameof( DirectionalLight.ShadowCascadeCount ) ).Capture();
			recorder.Property( nameof( DirectionalLight.ShadowCascadeSplitRatio ) ).Capture();
		}
	}
}

[Expose]
file sealed class AmbientLightCapturer : ComponentCapturer<AmbientLight>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, AmbientLight component )
	{
		recorder.Property( nameof( AmbientLight.Color ) ).Capture();
	}
}
