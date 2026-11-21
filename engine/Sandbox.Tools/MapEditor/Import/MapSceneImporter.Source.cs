using System;
using System.Text.Json.Nodes;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	enum SceneMeshPhysics { None, Default, SingleHull, MultipleHulls, Mesh, Sphere, Capsule, ConvexDecomposition }
	internal readonly record struct TileCornerPaint( System.Numerics.Vector4 Tint, System.Numerics.Vector4 Blend );
	sealed record SceneMeshData( PolygonMesh Mesh, float Smoothing, SceneMeshPhysics Physics, Color Tint, bool Shadows, string Tags,
		bool HideInGame = false, bool ShadowsOnly = false, bool EditorOnly = false, int RenderOrder = 0, bool DisabledInLowQuality = false,
		bool DisableHeightDisplacement = false, bool TransitionBlending = false, bool IndexedSecondaryBlend = false,
		Dictionary<HalfEdgeMesh.HalfEdgeHandle, TileCornerPaint> SourcePaint = null, string[] TileMaterialPaths = null, float CollisionError = 0, Surface CollisionSurface = null );

	enum NodeKind { World, Instance, GameObject, Entity, GroupOrMesh, Other }

	// A VMAP can reference the same node from its tree and from multiple instance placements.
	sealed class SourceNode
	{
		public string Name, TypeString, TargetMapPath, ImportWarning;
		public string PrefabTargetName;
		public bool FixupEntityNames, StaticWorldMesh, CulledProp;
		public int NodeId;
		public float GlassMass;
		public (string Map, string Path, float Opacity)? ImagePlane;
		public IReadOnlyDictionary<string, string> TileMaterials;
		public Dictionary<string, Transform> BonePose;
		public NodeKind Kind;
		public bool HiddenWorldLayer, ProxyOrientUp, EditorOnly, ForceHidden, SelectionHidden;
		public bool Hidden => ForceHidden || SelectionHidden;
		public Transform Transform;
		public SourceNode[] Children;
		public SourceNode Target;
		public Element Overlay;
		public Element[] Connections;
		public SourceNode SkyboxWorld;
		public bool IsSkyboxWorld;
		public Func<string, SourceNode> ResolveSkyboxOverride;
		public string SkyboxPath;
		public JsonObject PostProcessing;
		public bool OwnsTarget => Target is not null && TypeString != "CMapProxyInstance";
		public List<SourceNode> ProjectedOverlays;
		public Dictionary<string, string> VariableOverrides, EntityKeys;
		public (string Name, string Key)[] VariableBindings;
		public Element[] ChoiceGroups;
		public Guid ObjectId;
		public MapLoader.ObjectEntry Entity;
		public IReadOnlyList<SceneMeshData> Meshes;
		public IEnumerable<JsonObject> EmbeddedObjects = [];

		public SourceNode ShallowCopy() => (SourceNode)MemberwiseClone();
	}
}
