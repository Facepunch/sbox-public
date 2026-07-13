namespace Sandbox;

/// <summary>
/// The standard set of 15 lipsync visemes (mouth shapes), in OVRLipSync order.
/// Models express them as morph frames with these names ("viseme_PP" etc), and
/// lipsync - realtime or baked - produces a weight for each.
/// </summary>
public static class Visemes
{
	// The raw table, for the engine's per-frame loops - indexing an array is free,
	// going through the IReadOnlyList interface isn't
	internal static readonly string[] MorphNameArray =
	{
		"viseme_sil",
		"viseme_PP",
		"viseme_FF",
		"viseme_TH",
		"viseme_DD",
		"viseme_KK",
		"viseme_CH",
		"viseme_SS",
		"viseme_NN",
		"viseme_RR",
		"viseme_AA",
		"viseme_E",
		"viseme_I",
		"viseme_O",
		"viseme_U",
	};

	/// <summary>
	/// The viseme morph names, e.g. "viseme_PP". Index matches OVRLipSync
	/// viseme order and the viseme indices baked into sounds.
	/// </summary>
	public static IReadOnlyList<string> MorphNames { get; } = Array.AsReadOnly( MorphNameArray );

	/// <summary>
	/// Number of visemes.
	/// </summary>
	public static int Count => MorphNameArray.Length;
}
