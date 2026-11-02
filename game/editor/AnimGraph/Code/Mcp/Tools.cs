using Editor.Mcp;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph;

[McpToolset( "animgraph",
	"Create and edit animation graph (.ag) assets without opening the AnimGraph window. " +
	"Discover node schemas, create assets, inspect every nested graph and variation, list the bone, bone mask and clip names a graph can reference, apply transactional edits, and compile." )]
public static partial class McpTools
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter( null, true ) }
	};
}
