namespace Editor;

public static class CustomIconStorage
{
	// Session-only storage of custom icons keyed by GameObject reference
	public static Dictionary<GameObject, string> Icons = new();
}
