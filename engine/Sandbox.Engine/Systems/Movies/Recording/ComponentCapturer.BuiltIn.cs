using System.Collections;
using System.Runtime.CompilerServices;
using Sandbox.MovieMaker.Properties;
using static Sandbox.Component;

namespace Sandbox.MovieMaker;

#nullable enable

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
		recorder.Property( nameof( CameraComponent.RenderTags ) ).Capture( component.RenderTags );
		recorder.Property( nameof( CameraComponent.RenderExcludeTags ) ).Capture( component.RenderExcludeTags );
	}
}

[Expose]
file sealed class RendererCapturer : ComponentCapturer<Renderer>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, Renderer component )
	{
		var renderOptions = recorder.Property( nameof( Renderer.RenderOptions ) );

		renderOptions.Property( nameof( RenderOptions.Game ) ).Capture();
		renderOptions.Property( nameof( RenderOptions.Overlay ) ).Capture();
		renderOptions.Property( nameof( RenderOptions.Bloom ) ).Capture();
		renderOptions.Property( nameof( RenderOptions.AfterUI ) ).Capture();
	}
}

[Expose]
file sealed class ModelRendererCapturer : ComponentCapturer<ModelRenderer>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, ModelRenderer component )
	{
		recorder.Property( nameof( ModelRenderer.Model ) ).Capture();
		recorder.Property( nameof( ModelRenderer.Tint ) ).Capture();
		recorder.Property( nameof( ModelRenderer.MaterialOverride ) ).Capture( component.MaterialOverride );
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

		CaptureProceduralBones( recorder, component );

		recorder.Property( nameof( SkinnedModelRenderer.UseAnimGraph ) ).Capture();

		var sceneModel = component.SceneModel;

		if ( sceneModel.IsValid() && sceneModel.HasBoneOverrides() )
		{
			if ( component.Model is not { } model ) return;

			// Record bones if HasBoneOverrides

			var bonesTrack = recorder.Property( "Bones" );

			for ( var i = 0; i < model.BoneCount; i++ )
			{
				var boneName = model.GetBoneName( i );

				bonesTrack.Property( boneName ).Capture();
			}
		}
		else if ( component is { UseAnimGraph: true, Animation: { IsValid: true, Graph: { Parameters.Count: > 0 } graph2 } animation } )
		{
			// Record Animation.Parameters if using AG2

			var parametersTrack = recorder
				.Property( nameof( SkinnedModelRenderer.Animation ) )
				.Property( nameof( Sandbox.Animation.AnimationAccessor.Parameters ) );

			foreach ( var parameter in graph2.Parameters )
			{
				// Targets can be unset, or follow a bone - neither of which a Transform track can play back

				if ( parameter.Type == typeof( Transform ) ) continue;

				if ( animation.Parameters.TryGetKind( parameter.Name, out _ ) )
				{
					parametersTrack.Property( parameter.Name ).Capture();
				}
			}

			CaptureTriggers( recorder, graph2, animation.Parameters );
		}
		else if ( component.UseAnimGraph )
		{
			// Legacy AG fallback

#pragma warning disable CS0618
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
#pragma warning restore CS0618
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

	/// <summary>
	/// Bone objects posed by code, like a vehicle's wheels, are flagged with <see cref="GameObjectFlags.ProceduralBone"/>
	/// and feed the model from their transforms. Record those transforms, since nothing else will pose them on playback.
	/// </summary>
	private static void CaptureProceduralBones( IMovieTrackRecorder recorder, SkinnedModelRenderer component )
	{
		if ( !component.CreateBoneObjects ) return;
		if ( component.Model is not { } model ) return;
		if ( recorder is not IMovieTrackRecorderInternal { MovieRecorder: { } movieRecorder } ) return;

		foreach ( var bone in model.Bones.AllBones )
		{
			if ( component.GetBoneObject( bone ) is not { IsValid: true } boneObject ) continue;
			if ( !boneObject.Flags.Contains( GameObjectFlags.ProceduralBone ) ) continue;

			// Absolute bones are physics bones, which we don't record

			if ( boneObject.Flags.Contains( GameObjectFlags.Absolute ) ) continue;

			movieRecorder.GetTrackRecorder( boneObject )?.Capture();
		}
	}

	/// <summary>
	/// Trigger tracks that have seen a pulse. Most Bool parameters are never triggered, so we only start recording
	/// a trigger track when it first fires, then keep recording it so the pulse ends.
	/// </summary>
	private static readonly ConditionalWeakTable<IMovieTrackRecorder, object> _firedTriggers = new();

	/// <summary>
	/// Records one-shot pulses queued with <see cref="Sandbox.Animation.GraphParameters.Trigger"/>, which don't change
	/// the parameter's own value. We capture before the graph is evaluated, so pulses queued this frame are still pending.
	/// </summary>
	private static void CaptureTriggers( IMovieTrackRecorder recorder, Sandbox.Animation.Graph graph, Sandbox.Animation.GraphParameters parameters )
	{
		var triggersTrack = recorder
			.Property( nameof( SkinnedModelRenderer.Animation ) )
			.Property( GraphTriggerAccessor.PropertyName );

		foreach ( var parameter in graph.Parameters )
		{
			if ( parameter.Type != typeof( bool ) ) continue;

			var triggerTrack = triggersTrack.Property( parameter.Name );

			if ( parameters.IsTriggerPending( parameter.Name ) )
			{
				_firedTriggers.TryAdd( triggerTrack, triggerTrack );
			}
			else if ( !_firedTriggers.TryGetValue( triggerTrack, out _ ) )
			{
				continue;
			}

			triggerTrack.Capture();
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
			recorder.Property( nameof( LineRenderer.VectorPoints ) ).Capture( component.VectorPoints );
		}
		else
		{
			recorder.Property( nameof( LineRenderer.Points ) ).Capture( component.Points );
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
		recorder.Property( nameof( ParticleEffect.AngularVelocity ) ).Capture();
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
file sealed class DecalCapturer : ComponentCapturer<Decal>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, Decal component )
	{
		// Decals pick a random seed when enabled, which decides which of Decals is used and where it lands in each
		// random range below. We don't record it, so playback can pick a different variation.

		recorder.Property( nameof( Decal.Decals ) ).Capture( component.Decals );
		recorder.Property( nameof( Decal.LifeTime ) ).Capture();
		recorder.Property( nameof( Decal.Looped ) ).Capture();
		recorder.Property( nameof( Decal.Size ) ).Capture();
		recorder.Property( nameof( Decal.Scale ) ).Capture();
		recorder.Property( nameof( Decal.Rotation ) ).Capture();
		recorder.Property( nameof( Decal.Depth ) ).Capture();
		recorder.Property( nameof( Decal.Parallax ) ).Capture();
		recorder.Property( nameof( Decal.ColorTint ) ).Capture();
		recorder.Property( nameof( Decal.ColorMix ) ).Capture();
		recorder.Property( nameof( Decal.AttenuationAngle ) ).Capture();
		recorder.Property( nameof( Decal.SortLayer ) ).Capture();
		recorder.Property( nameof( Decal.SheetSequence ) ).Capture();

		if ( component.SheetSequence )
		{
			recorder.Property( nameof( Decal.SequenceId ) ).Capture();
		}
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
		recorder.Property( nameof( ParticleSpriteRenderer.CameraFadeNear ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.CameraFadeFar ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.FogStrength ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.FaceVelocity ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.VelocityStretch ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.StretchTime ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.StretchLengthLimits ) ).Capture();
		recorder.Property( nameof( ParticleSpriteRenderer.StretchFadeIn ) ).Capture();
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
		recorder.Property( nameof( ParticleTextRenderer.CameraFadeNear ) ).Capture();
		recorder.Property( nameof( ParticleTextRenderer.CameraFadeFar ) ).Capture();
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
		recorder.Property( nameof( Light.Intensity ) ).Capture();
		recorder.Property( nameof( Light.Temperature ) ).Capture();
		recorder.Property( nameof( Light.AnimationEnabled ) ).Capture();
		recorder.Property( nameof( Light.AnimationPattern ) ).Capture();
		recorder.Property( nameof( Light.AnimationSpeed ) ).Capture();
		recorder.Property( nameof( Light.AnimationTimeOffset ) ).Capture();
		recorder.Property( nameof( Light.AnimationDuration ) ).Capture();
		recorder.Property( nameof( Light.AnimationCurve ) ).Capture();
		recorder.Property( nameof( Light.AnimationGradient ) ).Capture();
		recorder.Property( nameof( Light.FogMode ) ).Capture();
		recorder.Property( nameof( Light.FogStrength ) ).Capture();
		recorder.Property( nameof( Light.Shadows ) ).Capture();

		if ( component.Shadows )
		{
			recorder.Property( nameof( Light.ShadowBias ) ).Capture();
			recorder.Property( nameof( Light.ShadowHardness ) ).Capture();
		}

		recorder.Property( nameof( Light.Contribution ) ).Capture();
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
		recorder.Property( nameof( DirectionalLight.ContactShadows ) ).Capture();

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

/// <summary>
/// Records the complete authored fog settings, including inactive layers that may be enabled during playback.
/// </summary>
[Expose]
file sealed class FogCapturer : ComponentCapturer<Fog>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, Fog component )
	{
		recorder.Property( nameof( Fog.FogEnabled ) ).Capture();
		recorder.Property( nameof( Fog.DistanceEnabled ) ).Capture();
		recorder.Property( nameof( Fog.Mode ) ).Capture();
		recorder.Property( nameof( Fog.StartDistance ) ).Capture();
		recorder.Property( nameof( Fog.DistanceRange ) ).Capture();
		recorder.Property( nameof( Fog.FalloffExponent ) ).Capture();
		recorder.Property( nameof( Fog.AttenuationDistance ) ).Capture();
		recorder.Property( nameof( Fog.MaximumOpacity ) ).Capture();
		recorder.Property( nameof( Fog.HeightEnabled ) ).Capture();
		recorder.Property( nameof( Fog.HeightOffset ) ).Capture();
		recorder.Property( nameof( Fog.HeightGradientLength ) ).Capture();
		recorder.Property( nameof( Fog.HeightExponent ) ).Capture();
		recorder.Property( nameof( Fog.HeightColorSource ) ).Capture();
		recorder.Property( nameof( Fog.HeightSky ) ).Capture();
		recorder.Property( nameof( Fog.HeightSkyBlur ) ).Capture();
		recorder.Property( nameof( Fog.HeightColor ) ).Capture();
		recorder.Property( nameof( Fog.ColorSource ) ).Capture();
		recorder.Property( nameof( Fog.Color ) ).Capture();
		recorder.Property( nameof( Fog.Sky ) ).Capture();
		recorder.Property( nameof( Fog.SkyBlur ) ).Capture();
		recorder.Property( nameof( Fog.SkyBlurDistanceEnabled ) ).Capture();
		recorder.Property( nameof( Fog.SkyBlurDistanceRange ) ).Capture();
		recorder.Property( nameof( Fog.SkyBlurFar ) ).Capture();
		recorder.Property( nameof( Fog.SunLightingEnabled ) ).Capture();
		recorder.Property( nameof( Fog.SunIntensity ) ).Capture();
		recorder.Property( nameof( Fog.SunLobeWidth ) ).Capture();
		recorder.Property( nameof( Fog.SunStartDistance ) ).Capture();
		recorder.Property( nameof( Fog.SunFadeDistance ) ).Capture();
		recorder.Property( nameof( Fog.SunSaturation ) ).Capture();
		recorder.Property( nameof( Fog.SkyAffect ) ).Capture();
		recorder.Property( nameof( Fog.MaxDistance ) ).Capture();
	}
}

#pragma warning disable CS0618 // Existing movies still capture the legacy fog components.
[Expose]
file sealed class GradientFogCapturer : ComponentCapturer<GradientFog>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, GradientFog component )
	{
		recorder.Property( nameof( GradientFog.Color ) ).Capture();
		recorder.Property( nameof( GradientFog.Height ) ).Capture();
		recorder.Property( nameof( GradientFog.VerticalFalloffExponent ) ).Capture();
		recorder.Property( nameof( GradientFog.StartDistance ) ).Capture();
		recorder.Property( nameof( GradientFog.EndDistance ) ).Capture();
		recorder.Property( nameof( GradientFog.FalloffExponent ) ).Capture();
	}
}

[Expose]
file sealed class CubemapFogCapturer : ComponentCapturer<CubemapFog>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, CubemapFog component )
	{
		recorder.Property( nameof( CubemapFog.Sky ) ).Capture();
		recorder.Property( nameof( CubemapFog.Blur ) ).Capture();
		recorder.Property( nameof( CubemapFog.StartDistance ) ).Capture();
		recorder.Property( nameof( CubemapFog.EndDistance ) ).Capture();
		recorder.Property( nameof( CubemapFog.FalloffExponent ) ).Capture();
		recorder.Property( nameof( CubemapFog.HeightWidth ) ).Capture();
		recorder.Property( nameof( CubemapFog.HeightStart ) ).Capture();
		recorder.Property( nameof( CubemapFog.HeightExponent ) ).Capture();
		recorder.Property( nameof( CubemapFog.Tint ) ).Capture();
	}
}

#pragma warning restore CS0618

[Expose]
file sealed class VolumetricFogVolumeCapturer : ComponentCapturer<VolumetricFogVolume>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, VolumetricFogVolume component )
	{
		recorder.Property( nameof( VolumetricFogVolume.Bounds ) ).Capture();
		recorder.Property( nameof( VolumetricFogVolume.Strength ) ).Capture();
		recorder.Property( nameof( VolumetricFogVolume.FalloffExponent ) ).Capture();
		recorder.Property( nameof( VolumetricFogVolume.Color ) ).Capture();
	}
}

[Expose]
file sealed class AmbientOcclusionCapturer : ComponentCapturer<AmbientOcclusion>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, AmbientOcclusion component )
	{
		recorder.Property( nameof( AmbientOcclusion.Intensity ) ).Capture();
		recorder.Property( nameof( AmbientOcclusion.Radius ) ).Capture();
		recorder.Property( nameof( AmbientOcclusion.FalloffRange ) ).Capture();
		recorder.Property( nameof( AmbientOcclusion.ThinCompensation ) ).Capture();
	}
}

[Expose]
file sealed class VignetteCapturer : ComponentCapturer<Vignette>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, Vignette component )
	{
		recorder.Property( nameof( Vignette.Color ) ).Capture();
		recorder.Property( nameof( Vignette.Intensity ) ).Capture();
		recorder.Property( nameof( Vignette.Smoothness ) ).Capture();
		recorder.Property( nameof( Vignette.Roundness ) ).Capture();
		recorder.Property( nameof( Vignette.Center ) ).Capture();
	}
}

[Expose]
file sealed class TonemappingCapturer : ComponentCapturer<Tonemapping>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, Tonemapping component )
	{
		recorder.Property( nameof( Tonemapping.Mode ) ).Capture();

		if ( component.Mode == Tonemapping.TonemappingMode.HableFilmic )
		{
			recorder.Property( nameof( Tonemapping.ExposureMethod ) ).Capture();
		}

		recorder.Property( nameof( Tonemapping.AutoExposureEnabled ) ).Capture();

		if ( component.AutoExposureEnabled )
		{
			recorder.Property( nameof( Tonemapping.MinimumExposure ) ).Capture();
			recorder.Property( nameof( Tonemapping.MaximumExposure ) ).Capture();
			recorder.Property( nameof( Tonemapping.ExposureCompensation ) ).Capture();
			recorder.Property( nameof( Tonemapping.Rate ) ).Capture();
		}
	}
}

[Expose]
file sealed class BloomCapturer : ComponentCapturer<Bloom>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, Bloom component )
	{
		recorder.Property( nameof( Bloom.Mode ) ).Capture();
		recorder.Property( nameof( Bloom.Strength ) ).Capture();
		recorder.Property( nameof( Bloom.Threshold ) ).Capture();
		recorder.Property( nameof( Bloom.Tint ) ).Capture();
	}
}

[Expose]
file sealed class SkyBox2DCapturer : ComponentCapturer<SkyBox2D>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, SkyBox2D component )
	{
		recorder.Property( nameof( SkyBox2D.SkyMaterial ) ).Capture( component.SkyMaterial );
		recorder.Property( nameof( SkyBox2D.Tint ) ).Capture();
		recorder.Property( nameof( SkyBox2D.SkyIndirectLighting ) ).Capture();
	}
}

[Expose]
file sealed class BeamEffectCapturer : ComponentCapturer<BeamEffect>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, BeamEffect component )
	{
		recorder.Property( nameof( BeamEffect.Scale ) ).Capture();
		recorder.Property( nameof( BeamEffect.TargetGameObject ) ).Capture();

		if ( !component.TargetGameObject.IsValid() )
		{
			recorder.Property( nameof( BeamEffect.TargetPosition ) ).Capture();
		}

		recorder.Property( nameof( BeamEffect.TargetRandom ) ).Capture();
		recorder.Property( nameof( BeamEffect.FollowPoints ) ).Capture();
		recorder.Property( nameof( BeamEffect.BeamsPerSecond ) ).Capture();
		recorder.Property( nameof( BeamEffect.MaxBeams ) ).Capture();
		recorder.Property( nameof( BeamEffect.InitialBurst ) ).Capture();
		recorder.Property( nameof( BeamEffect.BeamLifetime ) ).Capture();
		recorder.Property( nameof( BeamEffect.Looped ) ).Capture();
		recorder.Property( nameof( BeamEffect.Material ) ).Capture( component.Material );
		recorder.Property( nameof( BeamEffect.TextureOffset ) ).Capture();
		recorder.Property( nameof( BeamEffect.TextureScale ) ).Capture();
		recorder.Property( nameof( BeamEffect.TextureScrollSpeed ) ).Capture();
		recorder.Property( nameof( BeamEffect.TextureScroll ) ).Capture();
		recorder.Property( nameof( BeamEffect.FilterMode ) ).Capture();
		recorder.Property( nameof( BeamEffect.BeamColor ) ).Capture();
		recorder.Property( nameof( BeamEffect.Alpha ) ).Capture();
		recorder.Property( nameof( BeamEffect.Brightness ) ).Capture();
		recorder.Property( nameof( BeamEffect.Additive ) ).Capture();
		recorder.Property( nameof( BeamEffect.Shadows ) ).Capture();
		recorder.Property( nameof( BeamEffect.Lighting ) ).Capture();
		recorder.Property( nameof( BeamEffect.Opaque ) ).Capture();
		recorder.Property( nameof( BeamEffect.DepthFeather ) ).Capture();
		recorder.Property( nameof( BeamEffect.TravelBetweenPoints ) ).Capture();

		if ( component.TravelBetweenPoints )
		{
			recorder.Property( nameof( BeamEffect.TravelLerp ) ).Capture();
		}
	}
}

[Expose]
file sealed class TracerEffectCapturer : ComponentCapturer<TracerEffect>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, TracerEffect component )
	{
		// The line's progress isn't a property: it restarts when the effect is enabled, and travels by itself

		var endPointTrack = recorder.Property( nameof( TracerEffect.EndPoint ) );

		endPointTrack.Property( nameof( SceneAnchor.Parent ) ).Capture();
		endPointTrack.Property( nameof( SceneAnchor.LocalPosition ) ).Capture();

		recorder.Property( nameof( TracerEffect.DistancePerSecond ) ).Capture();
		recorder.Property( nameof( TracerEffect.Length ) ).Capture();
		recorder.Property( nameof( TracerEffect.StartDistance ) ).Capture();

		recorder.Property( nameof( TracerEffect.LineColor ) ).Capture();
		recorder.Property( nameof( TracerEffect.LineWidth ) ).Capture();
		recorder.Property( nameof( TracerEffect.StartCap ) ).Capture();
		recorder.Property( nameof( TracerEffect.EndCap ) ).Capture();
		recorder.Property( nameof( TracerEffect.Opaque ) ).Capture();
		recorder.Property( nameof( TracerEffect.CastShadows ) ).Capture();
		recorder.Property( nameof( TracerEffect.Material ) ).Capture( component.Material );

		recorder.Property( nameof( TracerEffect.EnableLight ) ).Capture();

		if ( component.EnableLight )
		{
			recorder.Property( nameof( TracerEffect.LightColor ) ).Capture();
			recorder.Property( nameof( TracerEffect.LightRadius ) ).Capture();
			recorder.Property( nameof( TracerEffect.LightPosition ) ).Capture();
		}
	}
}

[Expose]
file sealed class HighlightOutlineCapturer : ComponentCapturer<HighlightOutline>
{
	protected override void OnCapture( IMovieTrackRecorder recorder, HighlightOutline component )
	{
		recorder.Property( nameof( HighlightOutline.Material ) ).Capture( component.Material );
		recorder.Property( nameof( HighlightOutline.Color ) ).Capture();
		recorder.Property( nameof( HighlightOutline.ObscuredColor ) ).Capture();
		recorder.Property( nameof( HighlightOutline.InsideColor ) ).Capture();
		recorder.Property( nameof( HighlightOutline.InsideObscuredColor ) ).Capture();
		recorder.Property( nameof( HighlightOutline.Width ) ).Capture();
		recorder.Property( nameof( HighlightOutline.OverrideTargets ) ).Capture();

		if ( component.OverrideTargets )
		{
			recorder.Property( nameof( HighlightOutline.Targets ) ).Capture( component.Targets );
		}
	}
}

/// <summary>
/// Helpers for capturing collections and materials. Ideally we'd have these happen automatically
/// when <see cref="IMovieTrackRecorder.Capture"/> is called on a matching type eventually.
/// </summary>
internal static class CapturerExtensions
{
	[SkipHotload]
	private static ConditionalWeakTable<ITagSet, HashSet<string>> KnownTags { get; } = new();

	extension( IMovieTrackRecorder recorder )
	{
		/// <summary>
		/// Capture a <see cref="Material"/> property. Materials made at runtime, like with <see cref="Material.Create(string, string, bool)"/>
		/// or <see cref="Material.CreateCopy"/>, can't be saved as a reference, so their shader is recorded instead and
		/// playback makes a material from that. That material only has the shader's defaults: any parameters the original
		/// material had, from <c>Material.Set</c> or a <see cref="Material.CreateCopy"/> source, aren't recorded.
		/// </summary>
		public void Capture( Material? material )
		{
			if ( material is { IsRuntime: true } )
			{
				recorder.Property( MaterialShaderProperty.PropertyName ).Capture();

				// Playback replaces the material while the shader track plays. If the property goes back to its
				// default later, we need the material track to restore it, even though it only ever saw the default.

				(recorder as MoviePropertyTrackRecorder<Material>)?.KeepTrack();
			}
			else
			{
				recorder.Capture();
			}
		}

		public void Capture( IList? collection )
		{
			if ( collection is null ) return;

			recorder.Property( nameof( IList.Count ) ).Capture();

			for ( var i = 0; i < collection.Count; i++ )
			{
				recorder.Property( i.ToString() ).Capture();
			}
		}

		public void Capture( ITagSet? collection )
		{
			if ( collection is null ) return;

			var known = KnownTags.GetOrAdd( collection, _ => new HashSet<string>( StringComparer.OrdinalIgnoreCase ) );

			foreach ( var name in collection.LocalTags )
			{
				known.Add( name );
			}

			foreach ( var name in known )
			{
				recorder.Property( name ).Capture();
			}
		}
	}
}
