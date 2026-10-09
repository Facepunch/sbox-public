namespace Editor;

/// <summary>
/// Editor integration for components that control skinned models.
/// Implement the relevant methods to customize how those models are previewed and edited.
/// </summary>
public interface ISkinnedModelEditor
{
	/// <summary>
	/// Add zero or more models to preview in bind pose while this component is selected.
	/// Called each editor animation frame; requests are temporary. The editor resolves bone-merge sources automatically.
	/// Only add to the supplied set; do not clear it, remove entries, or retain it between calls.
	/// Invalid, inactive and cross-scene targets are ignored.
	/// </summary>
	void AddBindPosePreviewTargets( HashSet<SkinnedModelRenderer> targets ) { }
}
