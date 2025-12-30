
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System;
using static Sandbox.ModelRenderer;

namespace Prefab.Diff;

public static class GameObjectTestDataGenerator
{
	// Component mutation delegates
	private static readonly Dictionary<Type, Action<Component, Random>> ComponentMutators = new()
	{
		{ typeof(ModelRenderer), MutateModelRenderer },
		{ typeof(LineRenderer), MutateLineRenderer },
		{ typeof(BoxCollider), MutateBoxCollider },
		{ typeof(SphereCollider), MutateSphereCollider },
		{ typeof(SkinnedModelRenderer), MutateSkinnedModelRenderer },
		{ typeof(TextRenderer), MutateTextRenderer },
	};

	/// <summary>
	/// Generates a structured game object hierarchy with exact counts
	/// </summary>
	public static JsonObject GenerateStructured( int goCount, int componentCount, int seed )
	{
		using var scope = new Scene().Push();

		var random = new Random( seed );
		var root = new GameObject( false, GenerateName( random ) );

		// We'll always have a root, so adjust goCount
		var remainingGos = goCount - 1;

		// First, create all GameObjects in a flat structure
		var allObjects = new List<GameObject> { root };
		for ( int i = 0; i < remainingGos; i++ )
		{
			allObjects.Add( new GameObject( root, false, GenerateName( random ) ) );
		}

		// Randomly distribute them in the hierarchy
		for ( int i = 1; i < allObjects.Count; i++ )
		{
			var obj = allObjects[i];
			var newParent = allObjects[random.Next( i )]; // Only consider objects created before this one
			obj.SetParent( newParent );
		}

		// Distribute components across all objects
		DistributeComponents( allObjects, componentCount, random );

		return root.Serialize();
	}

	[Flags]
	public enum MutationFlags
	{
		None = 0,
		ModifyComponent = 1 << 0,
		AddComponent = 1 << 1,
		RemoveComponent = 1 << 2,
		AddGameObject = 1 << 3,
		RemoveGameObject = 1 << 4,
		AddPrefab = 1 << 5,
		MoveGameObject = 1 << 6,
		All = ModifyComponent | AddComponent | RemoveComponent | AddGameObject | RemoveGameObject | AddPrefab | MoveGameObject
	}

	/// <summary>
	/// Creates a modified copy with random mutations
	/// </summary>
	public static JsonObject Mutate( JsonNode source, int mutationCount, int seed, List<string> prefabs = null, MutationFlags flags = MutationFlags.All )
	{
		using var scope = new Scene().Push();

		var random = new Random( seed );
		var root = new GameObject( false, GenerateName( random ) );
		root.Deserialize( source.AsObject() );

		for ( int i = 0; i < mutationCount; i++ )
		{
			ApplyRandomMutation( root, random, prefabs, flags );
		}

		return root.Serialize();
	}

	private static void ApplyRandomMutation( GameObject root, Random random, List<string> prefabs, MutationFlags flags )
	{
		var remainingMutations = new HashSet<MutationFlags>();

		for ( int i = 0; i < 7; i++ )
		{
			if ( flags.HasFlag( (MutationFlags)(1 << i) ) )
			{
				remainingMutations.Add( (MutationFlags)(1 << i) );
			}
		}

		while ( remainingMutations.Count > 0 )
		{
			var mutationIdx = random.Next( remainingMutations.Count );
			var mutationType = remainingMutations.ToArray()[mutationIdx];

			bool success = mutationType switch
			{
				MutationFlags.ModifyComponent => ModifyRandomComponent( root, random ),
				MutationFlags.AddGameObject => AddRandomGameObject( root, random ),
				MutationFlags.RemoveGameObject => RemoveRandomGameObject( root, random ),
				MutationFlags.AddComponent => AddRandomComponent( root, random ),
				MutationFlags.RemoveComponent => RemoveRandomComponent( root, random ),
				MutationFlags.AddPrefab => AddRandomPrefab( root, random, prefabs ),
				MutationFlags.MoveGameObject => MoveRandomGameObject( root, random ),
				_ => false
			};

			if ( success ) return;
			remainingMutations.Remove( mutationType );
		}
	}

	private static bool ModifyRandomComponent( GameObject root, Random random )
	{
		if ( !TryGetRandomComponent( root, random, out var component ) ) return false;
		if ( !ComponentMutators.TryGetValue( component.GetType(), out var mutator ) ) return false;

		mutator( component, random );
		return true;
	}

	private static bool MoveRandomGameObject( GameObject root, Random random )
	{
		if ( !TryGetRandomGameObject( root, random, out var source ) ) return false;
		if ( !TryGetRandomGameObject( root, random, out var target ) ) return false;
		if ( source == root ) return false;
		if ( source == target || target.IsAncestor( source ) ) return false;

		source.SetParent( target );
		return true;
	}

	private static bool AddRandomGameObject( GameObject root, Random random )
	{
		if ( !TryGetRandomGameObject( root, random, out var parent ) ) return false;

		var newObj = new GameObject( parent, false, GenerateName( random ) );
		var componentCount = random.Next( 1, 4 );

		for ( int i = 0; i < componentCount; i++ )
		{
			CreateRandomComponent( newObj, random );
		}

		return true;
	}

	private static bool AddRandomPrefab( GameObject root, Random random, List<string> prefabs )
	{
		if ( prefabs == null || prefabs.Count == 0 ) return false;

		if ( !TryGetRandomGameObject( root, random, out var parent ) ) return false;
		var prefabName = prefabs[random.Next( prefabs.Count )];
		var prefab = ResourceLibrary.Get<PrefabFile>( prefabName );

		if ( prefab == null ) return false;

		var prefbInstance = SceneUtility.GetPrefabScene( prefab ).Clone( global::Transform.Zero, parent );
		if ( prefbInstance == null ) return false;

		return true;
	}

	private static bool RemoveRandomGameObject( GameObject root, Random random )
	{
		if ( !TryGetRandomGameObject( root, random, out var obj ) ) return false;
		if ( obj == root ) return false;

		obj.DestroyImmediate();
		return true;
	}

	private static bool AddRandomComponent( GameObject root, Random random )
	{
		if ( !TryGetRandomGameObject( root, random, out var target ) ) return false;
		CreateRandomComponent( target, random );
		return true;
	}

	private static bool RemoveRandomComponent( GameObject root, Random random )
	{
		if ( !TryGetRandomComponent( root, random, out var component ) ) return false;
		component.Destroy();
		return true;
	}

	private static bool TryGetRandomComponent( GameObject root, Random random, out Component component )
	{
		var allComponents = new List<Component>();
		foreach ( var obj in root.GetAllObjects( false ) )
		{
			allComponents.AddRange( obj.Components.GetAll() );
		}

		if ( allComponents.Count == 0 )
		{
			component = null;
			return false;
		}

		component = allComponents[random.Next( allComponents.Count )];
		return true;
	}

	private static bool TryGetRandomGameObject( GameObject root, Random random, out GameObject obj )
	{
		var objects = root.GetAllObjects( false ).ToList();
		if ( objects.Count == 0 )
		{
			obj = null;
			return false;
		}
		obj = objects[random.Next( objects.Count )];
		return true;
	}

	// Component Mutators
	private static void MutateModelRenderer( Component component, Random random )
	{
		var renderer = (ModelRenderer)component;
		renderer.Tint = new Color( random.Float(), random.Float(), random.Float() );
		renderer.RenderType = (ShadowRenderType)random.Next( 0, 2 );
	}

	private static void MutateLineRenderer( Component component, Random random )
	{
		var renderer = (LineRenderer)component;
		renderer.VectorPoints = new List<Vector3>();

		if ( random.Next( 2 ) == 0 )
		{
			renderer.UseVectorPoints = true;
			// add a random number of points
			var pointCount = random.Next( 1, 6 );
			for ( int i = 0; i < pointCount; i++ )
			{
				renderer.VectorPoints.Add( new Vector3( random.Float(), random.Float(), random.Float() ) );
			}
		}
		else
		{
			renderer.UseVectorPoints = false;
			// add a random number of gos
			var pointCount = random.Next( 1, 6 );
			renderer.Points = new();
			for ( int i = 0; i < pointCount; i++ )
			{
				renderer.Points.Add( TryGetRandomGameObject( renderer.GameObject, random, out var go ) ? go : renderer.GameObject );
			}
		}


	}

	private static void MutateBoxCollider( Component component, Random random )
	{
		var collider = (BoxCollider)component;

		collider.Center = new Vector3( random.Float(), random.Float(), random.Float() );
		collider.Scale = new Vector3( random.Float(), random.Float(), random.Float() );
		collider.Static = random.Next( 2 ) == 0;
	}

	private static void MutateSphereCollider( Component component, Random random )
	{
		var collider = (SphereCollider)component;

		collider.Center = new Vector3( random.Float(), random.Float(), random.Float() );
		collider.Radius = random.Float();
		collider.Static = random.Next( 2 ) == 0;
	}

	private static void MutateSkinnedModelRenderer( Component component, Random random )
	{
		var renderer = (SkinnedModelRenderer)component;

		renderer.Tint = new Color( random.Float(), random.Float(), random.Float() );
		renderer.PlaybackRate = random.Float();
		renderer.RenderType = (ShadowRenderType)random.Next( 0, 2 );
	}

	private static void MutateTextRenderer( Component component, Random random )
	{
		var renderer = (TextRenderer)component;

		renderer.Text = GenerateName( random );
		renderer.FontSize = random.Next( 8, 24 );
		renderer.Color = new Color( random.Float(), random.Float(), random.Float() );
	}

	private static void DistributeComponents( List<GameObject> objects, int totalComponents, Random random )
	{
		var remainingComponents = totalComponents;
		var remainingObjects = new List<GameObject>( objects );

		while ( remainingComponents > 0 && remainingObjects.Count > 0 )
		{
			// Pick a random object
			var objectIndex = random.Next( remainingObjects.Count );
			var targetObject = remainingObjects[objectIndex];

			// Decide how many components to add to this object
			var maxComponentsForObject = Math.Min( remainingComponents,
				Math.Max( 1, remainingComponents / remainingObjects.Count ) );
			var componentsForObject = random.Next( 1, maxComponentsForObject + 1 );

			// Add components
			for ( int i = 0; i < componentsForObject; i++ )
			{
				CreateRandomComponent( targetObject, random );
			}

			remainingComponents -= componentsForObject;

			// Remove object from pool if it got enough components
			if ( random.Next( 2 ) == 0 || remainingComponents <= remainingObjects.Count )
			{
				remainingObjects.RemoveAt( objectIndex );
			}
		}

		// If we still have components to distribute, add them to random objects
		while ( remainingComponents > 0 )
		{
			var targetObject = objects[random.Next( objects.Count )];
			CreateRandomComponent( targetObject, random );
			remainingComponents--;
		}
	}

	private static void CreateRandomComponent( GameObject go, Random random )
	{
		var type = ComponentMutators.Keys.ElementAt( random.Next( ComponentMutators.Count ) );
		var comp = go.Components.Create( type );
		ComponentMutators[type]( comp, random );
	}

	private static string GenerateName( Random random )
	{
		return $"GameObject_{random.Next( 10000 ):D4}";
	}
}
