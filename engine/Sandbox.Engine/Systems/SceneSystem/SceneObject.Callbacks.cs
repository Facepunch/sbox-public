namespace Sandbox;

public partial class SceneObject
{
	internal interface SceneObjectCallbacks : IValid
	{
		void OnBeforeObjectRender();
		void OnAfterObjectRender();
	}

	internal SceneObjectCallbacks CallbackTarget;

	internal void ExecuteBefore()
	{
		if ( !CallbackTarget.IsValid() ) return;
		CallbackTarget.OnBeforeObjectRender();
	}

	internal void ExecuteAfter()
	{
		if ( !CallbackTarget.IsValid() ) return;

		CallbackTarget.OnAfterObjectRender();
	}

	/// <summary>
	/// Count the samples this object's draws pass, a few frames behind (an occlusion query around them), for
	/// <see cref="IsCountedHidden"/>. Only objects with a before or after callback are counted.
	/// </summary>
	internal bool CountsSamples;

	// The frame a count of no samples came back in, or 0
	ulong countedHiddenAt;

	internal void Counted( ulong samples ) => countedHiddenAt = samples == 0 ? Application.FrameCount : 0;

	/// <summary>
	/// Nothing of the object passed the depth test in any view when last counted. False once counts stop coming (it isn't
	/// drawn), so it's taken as visible when it draws again.
	/// </summary>
	internal bool IsCountedHidden => countedHiddenAt != 0 && Application.FrameCount - countedHiddenAt < 8;
}
