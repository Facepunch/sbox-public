using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// Environment map probes: packed as native <c>EnvironmentMapConstants</c>, in native's blend order.
/// </summary>
internal sealed partial class LightBinnerFeature
{
	/// <summary>
	/// 128-byte mirror of <c>EnvironmentMapConstants</c>/<c>BinnedEnvMap</c> (lightbinner_standard.h, common/lightbinner.hlsl).
	/// </summary>
	[StructLayout( LayoutKind.Sequential, Pack = 4 )]
	internal struct GpuEnvMap
	{
		/// <summary>
		/// World-to-probe <c>matrix3x4a_t</c>, read as a row-vector <c>float4x3</c> by shaders.
		/// </summary>
		public Vector4 Row0, Row1, Row2;
		public Vector4 BoxMins;
		public Vector4 BoxMaxs;

		/// <summary>
		/// Tint, and feathering in w.
		/// </summary>
		public Vector4 Color;

		/// <summary>
		/// Native fills it, no shader reads it.
		/// </summary>
		public Vector4 NormalizationSH;

		/// <summary>
		/// The cubemap's bindless index, flags (unused), priority (sorting only), unused.
		/// </summary>
		public uint CubemapIndex, Flags, Priority, Unused;

		public const int NativeSize = 128;

		internal static void ValidateLayout()
		{
			var size = System.Runtime.CompilerServices.Unsafe.SizeOf<GpuEnvMap>();
			if ( size != NativeSize )
				throw new InvalidOperationException( $"GpuEnvMap is {size} bytes, native EnvironmentMapConstants is {NativeSize}" );
		}
	}

	GpuEnvMap[] envMaps = new GpuEnvMap[8];
	EnvMapObject[] envMapObjects = new EnvMapObject[8];
	(uint Priority, float Size)[] envMapKeys = new (uint, float)[8];
	GpuTexture blackCube;

	/// <summary>
	/// Extract and pack probes by descending priority, then ascending size (<c>CLightBinnerStandard</c>).
	/// </summary>
	void PrepareEnvMaps( ReadOnlySpan<RenderObject> objects, List<int> visible )
	{
		EnvMapCount = 0;

		var span = CollectionsMarshal.AsSpan( visible );
		var lightCount = 0;
		foreach ( var index in span )
		{
			if ( objects[index] is not EnvMapObject envMap )
			{
				span[lightCount++] = index;
				continue;
			}

			if ( EnvMapCount == MaxEnvMaps ) continue;
			if ( EnvMapCount == envMaps.Length )
			{
				var size = Math.Min( envMaps.Length * 2, MaxEnvMaps );
				Array.Resize( ref envMaps, size );
				Array.Resize( ref envMapObjects, size );
				Array.Resize( ref envMapKeys, size );
			}

			envMapObjects[EnvMapCount] = envMap;
			envMapKeys[EnvMapCount] = (PriorityKey( envMap.Priority ), envMap.ProjectionSize);
			EnvMapCount++;
		}

		// What's left is the lights
		visible.RemoveRange( lightCount, visible.Count - lightCount );

		// Sort only the object references, then copy each packed record once in GPU order.
		var probes = envMapObjects.AsSpan( 0, EnvMapCount );
		if ( probes.Length > 1 ) envMapKeys.AsSpan( 0, probes.Length ).Sort( probes );
		for ( int i = 0; i < probes.Length; i++ ) envMaps[i] = probes[i].Packed;

		envMapObjects.AsSpan( EnvMapCount ).Clear();
	}

	/// <summary>
	/// Match <c>CLightBinnerStandard</c> packing; defer cubemap indices until setup.
	/// </summary>
	internal static GpuEnvMap Pack( EnvMapObject envMap )
	{
		// Match native InverseTR (MatrixInvert): diag(scale) * R^T * (x - t), divided by the first axis's squared scale unless
		// that is 1 - the inverse for a uniform scale. Map probes can use scale -1, reversing box projection.
		var transform = envMap.Transform;
		var scale = transform.Scale;
		var scaleSquared = scale.x * scale.x;
		if ( MathF.Abs( scaleSquared - 1.0f ) >= 1e-3f && scaleSquared != 0.0f ) scale /= scaleSquared;
		var worldToLocal = Matrix.CreateTranslation( -transform.Position )
			* Matrix.CreateRotation( transform.Rotation ).Transpose()
			* Matrix.CreateScale( scale );

		var bounds = envMap.ProjectionBounds;
		var tint = envMap.Tint;

		return new GpuEnvMap
		{
			// Row vector matrix in, the rows of the column vector one out
			Row0 = new( worldToLocal.M11, worldToLocal.M21, worldToLocal.M31, worldToLocal.M41 ),
			Row1 = new( worldToLocal.M12, worldToLocal.M22, worldToLocal.M32, worldToLocal.M42 ),
			Row2 = new( worldToLocal.M13, worldToLocal.M23, worldToLocal.M33, worldToLocal.M43 ),
			BoxMins = new( bounds.Mins, 0 ),
			BoxMaxs = new( bounds.Maxs, 0 ),
			Color = new( tint.r, tint.g, tint.b, envMap.Feathering ),

			// Offset because it's unsigned, and flipped so higher priority sorts first
			Priority = PriorityKey( envMap.Priority ),
		};
	}

	static uint PriorityKey( int priority ) => (uint)(10000 + (10000 - Math.Clamp( priority, -10000, 10000 )));

	/// <summary>
	/// Resolve cubemap indices during setup; missing cubemaps reflect black.
	/// </summary>
	void ResolveCubemaps()
	{
		for ( int i = 0; i < EnvMapCount; i++ )
		{
			var cubemap = envMapObjects[i].Cubemap;
			if ( !GpuContext.HasData( cubemap ) ) cubemap = blackCube ??= RenderHost.Current.Device.LoadTexture( "dev/env_cubemap_black.vtex" );

			cubemap.KeepResident();
			envMaps[i].CubemapIndex = GpuContext.BindlessIndex( cubemap );
		}
	}
}
