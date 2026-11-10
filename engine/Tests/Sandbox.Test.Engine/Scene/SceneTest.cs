namespace SceneTests;

/// <summary>
/// Base class for tests that create scenes. The gameobject hierarchy asserts main-thread
/// access and scene push scopes swap global time state, so these tests mark their thread
/// as the main thread and should be [DoNotParallelize] so that state isn't shared.
/// </summary>
public abstract class SceneTest
{
	[TestInitialize]
	public void MarkTestThreadAsMain()
	{
		ThreadSafe.MarkMainThread();
	}
}
