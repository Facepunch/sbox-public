using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs;

public partial class Npc : Component
{
	/// <summary>
	/// Senses layer - handles environmental awareness and target detection
	/// </summary>
	[RequireComponent]
	public SensesLayer Senses { get; set; }

	/// <summary>
	/// Navigation layer - handles pathfinding and movement
	/// </summary>
	[RequireComponent]
	public NavigationLayer Navigation { get; set; }

	/// <summary>
	/// Animation layer - handles look-at and animation parameters
	/// </summary>
	[RequireComponent]
	public AnimationLayer Animation { get; set; }

	/// <summary>
	/// Speech layer - handles talking, lipsync, etc..
	/// </summary>
	[RequireComponent]
	public SpeechLayer Speech { get; set; }
}
