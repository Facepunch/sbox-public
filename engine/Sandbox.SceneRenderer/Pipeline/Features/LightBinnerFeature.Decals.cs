using System.Runtime.InteropServices;
using NativeEngine;

namespace Sandbox.SceneRenderer.Features;

/// <summary>
/// Sorts and packs clustered decals and extras (<c>CLightBinnerStandard::UploadLightingBuffers</c>, <c>Decals.hlsl</c>).
/// </summary>
internal sealed partial class LightBinnerFeature
{
	/// <summary>
	/// Native's <c>kMaxDecals</c>, and the decal slots per cluster the engine's <c>ClusteredCullingLayer</c> gives.
	/// </summary>
	public const int MaxDecals = 1024;
	const int MaxDecalsPerCluster = 64;

	static readonly StringToken DecalBufferName = new( "DecalsBuffer" );
	static readonly StringToken DecalExtraBufferName = new( "DecalsExtraDataBuffer" );

	/// <summary>
	/// 64-byte <c>GPUDecal</c>/<c>Decal</c> mirror (lightbinner_standard.h, Decals.hlsl).
	/// Stores negated position, conjugate rotation and reciprocal scale for world-to-unit-cube projection.
	/// </summary>
	[StructLayout( LayoutKind.Sequential, Pack = 4 )]
	internal struct GpuDecal
	{
		public Vector3 Position;
		public Vector4 Rotation;
		public Vector3 Scale;

		/// <summary>
		/// Colour, normal and RMO bindless indices, 16 bits each, then the colour's exponent and the attenuation angle.
		/// </summary>
		public ulong PackedTextureIndex;
		public uint SortOrder;
		public uint ExclusionBitMask;

		/// <summary>
		/// The tint's RGB mantissas, biased by 128 (the exponent is in <see cref="PackedTextureIndex"/>), and its alpha.
		/// </summary>
		public uint ColorTint;

		/// <summary>
		/// Byte offset of this decal's extras in <c>DecalsExtraDataBuffer</c>, or -1 for none.
		/// </summary>
		public int ExtraDataOffset;

		public const int NativeSize = 64;

		internal static void ValidateLayout()
		{
			var size = System.Runtime.CompilerServices.Unsafe.SizeOf<GpuDecal>();
			if ( size != NativeSize )
				throw new InvalidOperationException( $"GpuDecal is {size} bytes, native GPUDecal is {NativeSize}" );
		}
	}

	/// <summary>
	/// Native <c>GPUDecalExtraData</c>'s size: 13 words, read by byte offset (Decals.hlsl).
	/// </summary>
	const int ExtraWords = 13;

	readonly UploadRing<GpuDecal> decalRing = new( "SceneRenderer decals" );
	readonly UploadRing<uint> decalExtraRing = new( "SceneRenderer decal extras", GpuBuffer.UsageFlags.ByteAddress );
	GpuDecal[] decals = new GpuDecal[8];
	uint[] decalExtras = new uint[8 * ExtraWords];
	DecalObject[] decalObjects = new DecalObject[8];
	int decalExtraWords;
	GpuBuffer<uint> decalIndices;

	/// <summary>
	/// Decals binned for the current view.
	/// </summary>
	public int DecalCount { get; private set; }

	/// <summary>
	/// The binned decals, in the order they apply.
	/// </summary>
	internal ReadOnlySpan<DecalObject> Decals => decalObjects.AsSpan( 0, DecalCount );

	/// <summary>
	/// Extract decals with a stable sort. Defer packing until setup can resolve texture indices.
	/// </summary>
	void PrepareDecals( ReadOnlySpan<RenderObject> objects, List<int> visible )
	{
		DecalCount = 0;

		var span = CollectionsMarshal.AsSpan( visible );
		var kept = 0;
		foreach ( var index in span )
		{
			if ( objects[index] is not DecalObject decal )
			{
				span[kept++] = index;
				continue;
			}

			if ( !decal.Visible || DecalCount == MaxDecals ) continue;
			if ( DecalCount == decalObjects.Length ) Array.Resize( ref decalObjects, Math.Min( decalObjects.Length * 2, MaxDecals ) );

			// Insertion sort on sort order, stable: there are few, and they're mostly in order
			var slot = DecalCount++;
			while ( slot > 0 && decalObjects[slot - 1].SortOrder > decal.SortOrder )
			{
				decalObjects[slot] = decalObjects[slot - 1];
				slot--;
			}

			decalObjects[slot] = decal;
		}

		CollectionsMarshal.SetCount( visible, kept );

		// Don't keep removed decals alive
		decalObjects.AsSpan( DecalCount ).Clear();
	}

	/// <summary>
	/// Pack native decal data: sRGB colour, linear auxiliary textures and optional height/emission/sheet/mix/sampler extras.
	/// </summary>
	void PackDecals()
	{
		if ( decals.Length < DecalCount ) Array.Resize( ref decals, decalObjects.Length );
		if ( decalExtras.Length < DecalCount * ExtraWords ) Array.Resize( ref decalExtras, decalObjects.Length * ExtraWords );
		decalExtraWords = 0;

		for ( int i = 0; i < DecalCount; i++ )
		{
			var decal = decalObjects[i];
			var transform = decal.Transform;
			var rotation = transform.Rotation;
			var scale = transform.Scale;

			var rgbe = decal.Tint.ToRgbe();
			var alpha = (uint)(byte)(decal.Tint.a * 255.0f);

			var colorIndex = ViewIndex( decal.Color, srgb: true );
			var normalIndex = ViewIndex( decal.Normal, srgb: false );
			var rmoIndex = ViewIndex( decal.RoughnessMetalnessOcclusion, srgb: false );
			var attenuation = (ulong)(byte)(decal.AttenuationAngle * 255.0f + 0.5f);

			ref var d = ref decals[i];
			d.Position = -transform.Position;
			d.Rotation = new( -rotation.x, -rotation.y, -rotation.z, rotation.w );
			d.Scale = new( 1.0f / scale.x, 1.0f / scale.y, 1.0f / scale.z );
			d.ColorTint = (uint)rgbe.r | (uint)rgbe.g << 8 | (uint)rgbe.b << 16 | alpha << 24;
			d.SortOrder = decal.SortOrder;
			d.ExclusionBitMask = decal.ExclusionBitMask;
			d.PackedTextureIndex = (colorIndex & 0xFFFF) | (normalIndex & 0xFFFF) << 16 | (rmoIndex & 0xFFFF) << 32 | (ulong)rgbe.a << 48 | attenuation << 56;
			d.ExtraDataOffset = -1;

			// GPUDecalExtraData, with its defaults
			var sheet = Vector4.Zero;
			uint sequence = 0xFFFFFF;
			var colorMix = 1.0f;
			uint heightIndex = 0;
			var parallax = 1.0f;
			var sampler = -1;
			uint emissionIndex = 0;
			var emissionEnergy = 1.0f;
			uint coverage = 0xFF;
			var hasExtra = false;

			if ( decal.Height is { } height )
			{
				heightIndex = (uint)ViewIndex( height, srgb: false );
				parallax = decal.ParallaxStrength;
				var amount = (uint)(byte)(Math.Clamp( decal.CoverageAmount, 0, 1 ) * 255.0f + 0.5f);
				var range = (uint)(byte)(Math.Clamp( decal.CoverageRange, 0, 0.5f ) * 255.0f + 0.5f);
				coverage = amount | range << 8;
				hasExtra = true;
			}

			if ( decal.Emission is { } emission )
			{
				emissionIndex = (uint)ViewIndex( emission, srgb: false );
				emissionEnergy = MathF.Max( 0, decal.EmissionEnergy );
				hasExtra = true;
			}

			if ( decal.Color is { } color )
			{
				// A sheeted texture's frame data (GetSheetSequenceTextureData), as Texture.SequenceData reads it
				var data = color.SequenceData;
				if ( data != Vector4.Zero )
				{
					sheet = new( data.x, data.y, data.z, data.w );
					sequence = decal.SequenceIndex;
					hasExtra = true;
				}

				if ( decal.ColorMix < 1.0f )
				{
					colorMix = decal.ColorMix;
					hasExtra = true;
				}

				if ( decal.SamplerIndex >= 0 )
				{
					sampler = decal.SamplerIndex;
					hasExtra = true;
				}
			}

			if ( !hasExtra ) continue;

			d.ExtraDataOffset = decalExtraWords * 4;
			var extra = decalExtras.AsSpan( decalExtraWords, ExtraWords );
			extra[0] = BitConverter.SingleToUInt32Bits( sheet.x );
			extra[1] = BitConverter.SingleToUInt32Bits( sheet.y );
			extra[2] = BitConverter.SingleToUInt32Bits( sheet.z );
			extra[3] = BitConverter.SingleToUInt32Bits( sheet.w );
			extra[4] = sequence;
			extra[5] = BitConverter.SingleToUInt32Bits( colorMix );
			extra[6] = 0; // FeatureFlags
			extra[7] = heightIndex;
			extra[8] = BitConverter.SingleToUInt32Bits( parallax );
			extra[9] = (uint)sampler;
			extra[10] = emissionIndex;
			extra[11] = BitConverter.SingleToUInt32Bits( emissionEnergy );
			extra[12] = coverage;
			decalExtraWords += ExtraWords;
		}
	}

	/// <summary>
	/// A texture's bindless index through its sRGB or linear 2D view - <c>GetTextureViewIndex</c>, as native packs decals.
	/// </summary>
	static ulong ViewIndex( Texture texture, bool srgb ) => (ulong)(uint)g_pRenderDevice.GetTextureViewIndex( texture?.native ?? default, srgb ? (byte)1 : (byte)0, RenderTextureDimension.RENDER_TEXTURE_DIMENSION_2D );
}
