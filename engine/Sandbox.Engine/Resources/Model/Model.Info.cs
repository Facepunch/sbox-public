namespace Sandbox;

partial class Model
{
	/// <summary>
	/// Contains all mesh and draw call information for a model.
	/// </summary>
	public class ModelMeshInfo
	{
		internal ModelMeshInfo() { }

		public int TotalVertices { get; init; }
		public int TotalTriangles { get; init; }
		public int TotalDrawCalls { get; init; }
		public int LodCount { get; init; }
		public float[] LodSwitchDistances { get; init; }
		public MeshData[] Meshes { get; init; }

		public class MeshData
		{
			internal MeshData() { }

			public string Name { get; init; }
			public int Vertices { get; init; }
			public int Triangles { get; init; }
			public int LodMask { get; init; }
			public int TranslucencyType { get; init; }
			public Vector3 BoundsMin { get; init; }
			public Vector3 BoundsMax { get; init; }
			public DrawCallData[] DrawCalls { get; init; }
		}

		public class DrawCallData
		{
			internal DrawCallData() { }

			public int Vertices { get; init; }
			public int Indices { get; init; }
			public int PrimitiveType { get; init; }
			public float UvDensity { get; init; }
			public int InstanceCount { get; init; }
			public bool AlphaBlended { get; init; }
			public Material Material { get; init; }
		}
	}

	/// <summary>
	/// All mesh and draw call information for this model.
	/// </summary>
	public ModelMeshInfo MeshInfo
	{
		get
		{
			if ( field is not null )
				return field;

			native.GetTotalMeshCounts( out int totalVerts, out int totalTris, out int totalDraws );

			int meshCount = MeshCount;
			var meshes = new ModelMeshInfo.MeshData[meshCount];

			for ( int i = 0; i < meshCount; i++ )
			{
				native.GetMeshInfo( i, out int verts, out int tris, out int draws, out int lodMask, out int translucencyType, out var mins, out var maxs );

				var drawCalls = new ModelMeshInfo.DrawCallData[draws];
				for ( int d = 0; d < draws; d++ )
				{
					native.GetDrawCallInfo( i, d, out int dcVerts, out int indices, out int primType, out float uvDensity, out int instanceCount, out int alphaBlendedInt );

					drawCalls[d] = new ModelMeshInfo.DrawCallData
					{
						Vertices = dcVerts,
						Indices = indices,
						PrimitiveType = primType,
						UvDensity = uvDensity,
						InstanceCount = instanceCount,
						AlphaBlended = alphaBlendedInt != 0,
						Material = Material.FromNative( native.GetDrawCallMaterial( i, d ) )
					};
				}

				meshes[i] = new ModelMeshInfo.MeshData
				{
					Name = native.GetMeshName( i ),
					Vertices = verts,
					Triangles = tris,
					LodMask = lodMask,
					TranslucencyType = translucencyType,
					BoundsMin = mins,
					BoundsMax = maxs,
					DrawCalls = drawCalls
				};
			}

			var lodDistCount = native.GetLODSwitchDistanceCount();
			var lodDistances = new float[lodDistCount];
			for ( int i = 0; i < lodDistCount; i++ )
				lodDistances[i] = native.GetLODSwitchDistance( i );

			field = new ModelMeshInfo
			{
				TotalVertices = totalVerts,
				TotalTriangles = totalTris,
				TotalDrawCalls = totalDraws,
				LodCount = MaxLodLevel + 1,
				LodSwitchDistances = lodDistances,
				Meshes = meshes
			};

			return field;
		}

		private set;
	}
}
