using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// AI profiles whose models are selected by the backend.
/// </summary>
[JsonConverter( typeof( JsonStringEnumConverter<AiProfile> ) )]
public enum AiProfile
{
	/// <summary>
	/// Fast, inexpensive requests through the sbox-fast preset.
	/// </summary>
	Fast,

	/// <summary>
	/// More demanding requests through the sbox-capable preset.
	/// </summary>
	Capable,

	/// <summary>
	/// Tool-driven creation and iteration through the sbox-agent preset.
	/// </summary>
	Agent
}
