using Sandbox.Rendering;
using System.Runtime.InteropServices;
using static Sandbox.ParticleSpriteRenderer;
using static Sandbox.Rendering.SpriteBatchSceneObject;

namespace Sandbox;

/// <summary>
/// Interface for batched particle renderers that can be processed by SceneSpriteSystem
/// </summary>
internal interface IBatchedParticleSpriteRenderer : ISpriteRenderGroup
{
	internal enum ParticleType
	{
		Sprite,
		Text
	}

	ParticleType Type => ParticleType.Sprite;
	ParticleEffect ParticleEffect { get; }
	float Scale { get; }
	Vector3 WorldScale { get; }
	Rotation WorldRotation { get; }
	bool FaceVelocity { get; }
	float RotationOffset { get; }
	bool MotionBlur { get; }
	bool LeadingTrail { get; }
	float BlurAmount { get; }
	float BlurSpacing { get; }
	float BlurOpacity { get; }
	bool Lighting { get; }
	Vector2 Pivot { get; }
	float DepthFeather { get; }
	float FogStrength { get; }
	FilterMode TextureFilter { get; }

	// Implemented by derived classes
	Texture RenderTexture { get; }

	// Additional properties needed for some renderers
	BillboardAlignment Alignment { get; }

	/// <summary>
	/// Result of particle processing operation
	/// </summary>
	internal readonly record struct ParticleProcessResult( int SpriteCount, int SplotCount );

	/// <summary>
	/// Efficiently converts particle data directly into GPU-ready SpriteData format.
	/// This is the hot path for particle rendering - bypasses intermediate allocations
	/// by writing directly to the destination sprite buffer.
	/// </summary>
	internal unsafe ParticleProcessResult ProcessParticlesDirectly( Span<SpriteData> destinationBuffer )
	{
		SamplerState sampler = new() { AddressModeU = TextureAddressMode.Clamp, AddressModeV = TextureAddressMode.Clamp };

		var particles = CollectionsMarshal.AsSpan( ParticleEffect.Particles );
		var count = particles.Length;

		if ( count == 0 || destinationBuffer.Length < count )
			return new( 0, 0 );

		// Get texture from the renderer-specific implementation
		var texture = RenderTexture ?? Texture.White;

		// Precompute constants
		var scale = MathF.Abs( (Scale / 2f) * WorldScale.x );
		var objectAngles = WorldRotation;
		var billboardModeUint = (uint)(SpriteRenderer.BillboardMode)Alignment;
		var isObjectAlignment = Alignment == BillboardAlignment.Object;

		var blurAmountRemapped = BlurAmount.Remap( 0, 1, 0, 6, false );
		var blurSpacingRemapped = BlurSpacing.Remap( 0, 1, 0, 1, false );
		var leadingTrailMultiplier = LeadingTrail ? 2f : 1f;
		var rotationOffsetValue = FaceVelocity ? RotationOffset : -1.0f;

		var samplerIndex = SamplerState.GetBindlessIndex( sampler with { Filter = TextureFilter } );
		var sequenceData = texture.SequenceData;
		var lightingValue = Lighting ? 1 : 0;
		var textureHandle = texture?.Index ?? Texture.Invalid.Index;

		var motionBlurEnabled = MotionBlur;
		const float blurReciprocal = 0.02f;

		var packedFogAndAlpha = SpriteData.PackFogAndAlphaCutout( this.FogStrength, 0.001f );
		var depthFeather = DepthFeather;
		var blurOpacity = BlurOpacity;
		var origin = Pivot;

		// Calculate aspect ratio - different for text vs sprite
		var aspect = texture.Size.x / texture.Size.y;

		int validCount = 0;
		int totalSplotCount = 0;

		fixed ( SpriteData* destinationPtr = destinationBuffer )
		{
			for ( int index = 0; index < count; index++ )
			{
				ref var p = ref particles[index];

				ref readonly var pos = ref p.Position;
				ref readonly var vel = ref p.Velocity;

				var angles = p.Angles;
				if ( isObjectAlignment )
				{
					var newRotation = (objectAngles * angles.ToRotation()).Angles();
					angles.pitch = newRotation.pitch;
					angles.yaw = newRotation.yaw;
					angles.roll = newRotation.roll;
				}

				float sequenceTime = p.SequenceTime.y + p.SequenceTime.z;
				if ( p.SequenceTime.x > 0f )
					sequenceTime += p.SequenceTime.x;

				int splots = 0;
				if ( motionBlurEnabled )
				{
					float speed = vel.Length * blurAmountRemapped;
					splots = Math.Clamp( (int)(speed * blurReciprocal), 0, 8 );
				}

				// Accumulate total splot count for precomputation optimization
				totalSplotCount += splots * (int)leadingTrailMultiplier;

				var scaleX = p.Size.x * scale;
				var scaleY = p.Size.y * scale;

				if ( Type == ParticleType.Text || (sequenceData == Vector4.Zero && aspect != 1) )
				{
					scaleX *= aspect;
				}

				var rgbe = p.Color.ToRgbe();
				var alpha = (byte)((p.Color.a * p.Alpha).Clamp( 0.0f, 1.0f ) * 255.0f);
				var tintColor = new Color32( rgbe.r, rgbe.g, rgbe.b, alpha );

				var overlayRgbe = p.OverlayColor.ToRgbe();
				var overlayAlpha = (byte)(p.OverlayColor.a.Clamp( 0.0f, 1.0f ) * 255.0f);
				var overlayColor = new Color32( overlayRgbe.r, overlayRgbe.g, overlayRgbe.b, overlayAlpha );

				// we are packing the exponent in the second half of the lighting flag
				uint packedExponent = (uint)((byte)lightingValue | rgbe.a << 16);

				var spritePtr = destinationPtr + validCount;

				spritePtr->Position = new Vector3( pos.x, pos.y, pos.z );
				spritePtr->Rotation = new Vector3( angles.pitch, angles.yaw, angles.roll );
				spritePtr->Scale = new Vector2( scaleX, scaleY );
				spritePtr->Velocity = new Vector3( vel.x, vel.y, vel.z );
				spritePtr->MotionBlur = new Vector4( leadingTrailMultiplier, blurAmountRemapped, blurSpacingRemapped, blurOpacity );
				spritePtr->TextureHandle = textureHandle;
				spritePtr->TintColor = tintColor.RawInt;
				spritePtr->OverlayColor = overlayColor.RawInt;
				spritePtr->RenderFlags = 0;
				spritePtr->BillboardMode = billboardModeUint;
				spritePtr->FogStrengthCutout = packedFogAndAlpha;
				spritePtr->Lighting = packedExponent;
				spritePtr->DepthFeather = depthFeather;
				spritePtr->SamplerIndex = samplerIndex;
				spritePtr->Splots = splots;
				spritePtr->RotationOffset = rotationOffsetValue;
				spritePtr->Sequence = p.Sequence & 255;
				spritePtr->SequenceTime = sequenceTime;
				spritePtr->BlendSheetUV = sequenceData;
				spritePtr->Offset = origin;

				validCount++;
			}
		}

		return new( validCount, totalSplotCount );
	}
}
