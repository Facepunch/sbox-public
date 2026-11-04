using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Features;

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
	Texture blackCube;

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
			envMaps[EnvMapCount] = Pack( envMap );
			envMapKeys[EnvMapCount] = (envMaps[EnvMapCount].Priority, envMap.ProjectionBounds.Size.Length);
			EnvMapCount++;
		}

		// What's left is the lights
		visible.RemoveRange( lightCount, visible.Count - lightCount );

		if ( EnvMapCount > 1 )
		{
			var keys = envMapKeys.AsSpan( 0, EnvMapCount );
			var order = envMapOrder.Length < EnvMapCount ? envMapOrder = new int[envMaps.Length] : envMapOrder;
			for ( int i = 0; i < EnvMapCount; i++ ) order[i] = i;
			keys.Sort( order.AsSpan( 0, EnvMapCount ) );

			for ( int i = 0; i < EnvMapCount; i++ ) sortedEnvMaps[i] = envMaps[order[i]];
			for ( int i = 0; i < EnvMapCount; i++ ) sortedEnvMapObjects[i] = envMapObjects[order[i]];
			sortedEnvMaps.AsSpan( 0, EnvMapCount ).CopyTo( envMaps );
			sortedEnvMapObjects.AsSpan( 0, EnvMapCount ).CopyTo( envMapObjects );
		}

		envMapObjects.AsSpan( EnvMapCount ).Clear();
	}

	int[] envMapOrder = new int[8];
	GpuEnvMap[] sortedEnvMaps = new GpuEnvMap[MaxEnvMaps];
	EnvMapObject[] sortedEnvMapObjects = new EnvMapObject[MaxEnvMaps];

	/// <summary>
	/// Match <c>CLightBinnerStandard</c> packing; defer cubemap indices until setup.
	/// </summary>
	internal static GpuEnvMap Pack( EnvMapObject envMap )
	{
		// Match native InverseTR: diag(scale) * R^T * (x - t), not inverse scale.
		// Map probes can use scale -1, reversing box projection.
		var transform = envMap.Transform;
		var worldToLocal = Matrix.CreateTranslation( -transform.Position )
			* Matrix.CreateRotation( transform.Rotation ).Transpose()
			* Matrix.CreateScale( transform.Scale );

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
			Priority = (uint)(10000 + (10000 - Math.Clamp( envMap.Priority, -10000, 10000 ))),
		};
	}

	/// <summary>
	/// Resolve cubemap indices during setup; missing cubemaps reflect black.
	/// </summary>
	void ResolveCubemaps()
	{
		for ( int i = 0; i < EnvMapCount; i++ )
		{
			var cubemap = envMapObjects[i].Cubemap;
			if ( !RenderContext.HasData( cubemap ) ) cubemap = blackCube ??= Texture.Load( "dev/env_cubemap_black.vtex" );

			cubemap.MarkUsed( ushort.MaxValue );
			envMaps[i].CubemapIndex = RenderContext.BindlessIndex( cubemap );
		}
	}
}
