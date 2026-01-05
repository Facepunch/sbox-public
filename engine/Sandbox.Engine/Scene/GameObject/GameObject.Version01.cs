using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

public partial class GameObject
{
	/// <summary>
	/// Converts Legacy Prefab Instance Data and variables to a patch.
	/// - Root level properties: Transform, Flags, Network... are converted to property overrides
	/// - Prefab Variables are converted to component property overrides
	/// - Prefab to instance guid lookup is created
	/// </summary>
	[Expose, JsonUpgrader( typeof( GameObject ), 1 )]
	internal static void Upgrader_v1( JsonObject obj )
	{
		if ( obj[JsonKeys.PrefabInstanceSource] is JsonValue __Prefab && __Prefab.TryGetValue( out string prefabSource ) )
		{
			if ( !obj.ContainsKey( JsonKeys.PrefabInstancePatch ) )
			{
				var prefabFile = ResourceLibrary.Get<PrefabFile>( prefabSource );

				if ( !prefabFile.IsValid() )
				{
					var name = obj.TryGetPropertyValue( JsonKeys.Name, out var nameNode ) ? nameNode.Deserialize<string>() : null;

					Log.Warning( $"GameObject {name ?? "unknown"} Upgrader failed to upgrade prefab instance, prefab '{prefabSource}' couldn't be loaded and is likely missing." );
					return;
				}

				// Convert the existing isntance data into a patch
				var instancePatch = ConvertLegacyPrefabInstanceToPatch( obj, prefabFile );
				var instanceGuid = obj[JsonKeys.Id].Deserialize<Guid>();

				var prefabScene = (PrefabCacheScene)SceneUtility.GetPrefabScene( prefabFile );

				// We wont have a lookup table stored yet so create a new one
				var prefabLookup = SceneUtility.CreateUniqueGuidLookup( prefabScene.FullPrefabInstanceJson, instanceGuid );

				// It's easier to clear the object and start from scratch
				obj.Clear();

				obj[JsonKeys.PrefabInstanceSource] = JsonValue.Create( prefabSource );
				obj[JsonKeys.Id] = instanceGuid;
				obj[JsonKeys.PrefabInstancePatch] = Json.ToNode( instancePatch );
				obj[JsonKeys.PrefabIdToInstanceId] = Json.ToNode( prefabLookup );
			}
		}
	}

	/// <summary>
	/// Backwards combatibility for old prefab instances.
	/// </summary>
	private static Json.Patch ConvertLegacyPrefabInstanceToPatch( JsonObject legacyData, PrefabFile prefabFile )
	{
		var instancePatch = new Json.Patch();

		var prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		var variables = legacyData[JsonKeys.PrefabInstanceVariables] as JsonObject;

		if ( variables is not null )
		{
			foreach ( (string name, JsonNode value) in variables )
			{
#pragma warning disable CS0612
				var variable = prefabScene.Variables.Where( x => x.Id == name ).FirstOrDefault();
#pragma warning restore CS0612
				if ( variable is null )
				{
					Log.Warning( $"Prefab Variable not in prefab: {name}" );
					continue;
				}

				foreach ( var target in variable.Targets )
				{
					instancePatch.PropertyOverrides.Add( new Json.PropertyOverride
					{
						Target = new Json.ObjectIdentifier
						{
							Type = "Component", // we  only ever support components!
							IdValue = target.Id.ToString()
						},
						Property = target.Property,
						Value = value
					} );
				}
			}
		}


		void TryAddOverrideTargetingPrefab( JsonObject legacyData, string propertyName, JsonNode defaultValue = null )
		{
			if ( !legacyData.TryGetPropertyValue( propertyName, out var propertyNode ) && defaultValue is null ) return;

			instancePatch.PropertyOverrides.Add( new Json.PropertyOverride
			{
				Target = new Json.ObjectIdentifier
				{
					Type = "GameObject",
					IdValue = prefabScene.Id.ToString()
				},
				Property = propertyName,
				Value = propertyNode ?? defaultValue
			} );
		}

		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.Position, Json.ToNode( Vector3.Zero ) );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.Rotation, Json.ToNode( Rotation.Identity ) );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.Scale, Json.ToNode( Vector3.One ) );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.Name );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.Enabled, false );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.Flags, 0 );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.Tags, prefabFile.RootObject[JsonKeys.Tags] ?? "" );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.NetworkMode, prefabFile.RootObject[JsonKeys.NetworkMode] ?? (int)NetworkMode.Snapshot );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.AlwaysTransmit, prefabFile.RootObject[JsonKeys.AlwaysTransmit] ?? true );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.NetworkOrphaned, prefabFile.RootObject[JsonKeys.NetworkOrphaned] ?? (int)NetworkOrphaned.Destroy );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.OwnerTransfer, prefabFile.RootObject[JsonKeys.OwnerTransfer] ?? (int)OwnerTransfer.Fixed );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.NetworkFlags, prefabFile.RootObject[JsonKeys.NetworkFlags] ?? 0 );
		TryAddOverrideTargetingPrefab( legacyData, JsonKeys.NetworkInterpolation, prefabFile.RootObject[JsonKeys.NetworkInterpolation] ?? false );

		return instancePatch;
	}
}
