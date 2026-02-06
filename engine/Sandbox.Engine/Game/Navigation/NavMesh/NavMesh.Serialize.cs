using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Navigation;

public sealed partial class NavMesh
{
	/// <summary>
	/// The relative path to the baked navdata file (without _c suffix)
	/// </summary>
	private string _bakedDataPath;

	/// <summary>
	/// Get the navdata filename for this scene
	/// </summary>
	private readonly string _navDataFilename = "/navmesh/baked.navdata";

	/// <summary>
	/// Data saved in a Scene file
	/// </summary>
	internal JsonObject Serialize()
	{
		JsonObject jso = new JsonObject();

		jso["Enabled"] = IsEnabled;
		jso["IncludeStaticBodies"] = IncludeStaticBodies;
		jso["IncludeKeyframedBodies"] = IncludeKeyframedBodies;
		jso["EditorAutoUpdate"] = EditorAutoUpdate;
		jso["AgentHeight"] = AgentHeight;
		jso["AgentRadius"] = AgentRadius;
		jso["AgentStepSize"] = AgentStepSize;
		jso["AgentMaxSlope"] = AgentMaxSlope;
		jso["ExcludedBodies"] = Json.ToNode( ExcludedBodies, typeof( TagSet ) );
		jso["IncludedBodies"] = Json.ToNode( IncludedBodies, typeof( TagSet ) );

		// Store reference to the baked data file as a RawFileReference
		if ( !string.IsNullOrWhiteSpace( _bakedDataPath ) )
		{
			jso["BakedDataPath"] = JsonSerializer.SerializeToNode( FileReference.FromPath( _bakedDataPath ) );
		}

		return jso;
	}

	/// <summary>
	/// Data loaded from a Scene file
	/// </summary>
	internal void Deserialize( JsonObject jso )
	{
		if ( jso is null )
			return;

		IsEnabled = (bool)(jso["Enabled"] ?? IsEnabled);
		IncludeStaticBodies = (bool)(jso["IncludeStaticBodies"] ?? IncludeStaticBodies);
		IncludeKeyframedBodies = (bool)(jso["IncludeKeyframedBodies"] ?? IncludeKeyframedBodies);
		EditorAutoUpdate = (bool)(jso["EditorAutoUpdate"] ?? EditorAutoUpdate);
		AgentHeight = (float)(jso["AgentHeight"] ?? AgentHeight);
		AgentRadius = (float)(jso["AgentRadius"] ?? AgentRadius);
		AgentStepSize = (float)(jso["AgentStepSize"] ?? AgentStepSize);
		AgentMaxSlope = (float)(jso["AgentMaxSlope"] ?? AgentMaxSlope);

		ExcludedBodies = Json.FromNode<TagSet>( jso["ExcludedBodies"] ) ?? ExcludedBodies;
		IncludedBodies = Json.FromNode<TagSet>( jso["IncludedBodies"] ) ?? IncludedBodies;

		// Load baked data path from RawFileReference
		if ( jso["BakedDataPath"] is JsonObject bakedDataObj )
		{
			_bakedDataPath = bakedDataObj["path"]?.ToString();
		}
	}
}
