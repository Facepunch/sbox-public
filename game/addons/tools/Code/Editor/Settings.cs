namespace Editor;

public static class SettingsMenus
{
	[Menu( "Editor", "Settings/Frame Rate Limit/10fps" )]
	public static bool Frame10
	{
		get => EditorUtility.RenderSettings.MaxFrameRate == 10;
		set => EditorUtility.RenderSettings.MaxFrameRate = 10;
	}


	[Menu( "Editor", "Settings/Frame Rate Limit/30fps" )]
	public static bool Frame30
	{
		get => EditorUtility.RenderSettings.MaxFrameRate == 30;
		set => EditorUtility.RenderSettings.MaxFrameRate = 30;
	}

	[Menu( "Editor", "Settings/Frame Rate Limit/60fps" )]
	public static bool Frame60
	{
		get => EditorUtility.RenderSettings.MaxFrameRate == 60;
		set => EditorUtility.RenderSettings.MaxFrameRate = 60;
	}

	[Menu( "Editor", "Settings/Frame Rate Limit/120fps" )]
	public static bool Frame120
	{
		get => EditorUtility.RenderSettings.MaxFrameRate == 120;
		set => EditorUtility.RenderSettings.MaxFrameRate = 120;
	}

	[Menu( "Editor", "Settings/Frame Rate Limit/240fps" )]
	public static bool Frame240
	{
		get => EditorUtility.RenderSettings.MaxFrameRate == 240;
		set => EditorUtility.RenderSettings.MaxFrameRate = 240;
	}

	[Menu( "Editor", "Settings/Frame Rate Limit/1000fps" )]
	public static bool Frame1000
	{
		get => EditorUtility.RenderSettings.MaxFrameRate == 1000;
		set => EditorUtility.RenderSettings.MaxFrameRate = 1000;
	}

	[Menu( "Editor", "Settings/VSync" )]
	public static bool VSync
	{
		get => EditorUtility.RenderSettings.VSync;
		set
		{
			EditorUtility.RenderSettings.VSync = value;
			EditorUtility.RenderSettings.Apply();
		}
	}

	[Menu( "Editor", "Settings/VR" )]
	public static bool VR
	{
		get => EditorUtility.VR.Enabled;
		set => EditorUtility.VR.Enabled = value;
	}
}
