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
}
