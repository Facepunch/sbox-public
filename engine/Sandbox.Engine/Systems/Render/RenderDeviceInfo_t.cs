using Sandbox;

internal struct RenderDeviceInfo_t
{
	public RenderDisplayMode_t m_DisplayMode;
	public NativeEngine.RenderMultisampleType m_nMultisampleType;
	public RenderDisplayModeUsage m_nModeUsage;
	public byte m_bWaitForVSync;           // Would we not present until vsync?
	public byte m_bIsMainWindow;
}

// RENDER_DISPLAY_MODE_* usage flags in renderdevicetypes.h.
[Flags]
internal enum RenderDisplayModeUsage : byte
{
	ExclusiveFullscreen = 0x01,
	CooperativeFullscreen = 0x02,
	BorderlessWindow = 0x04,
	BorderedWindow = 0x08
}

internal struct RenderDisplayMode_t
{
	public int m_nWidth;                   // Swapchain dimensions in pixels.
	public int m_nHeight;
	public ImageFormat m_Format;           // Backbuffer image format.
	public int m_nRefreshRateNumerator;    // Refresh rate. Use 0 in numerator + denominator for a default setting.
	public int m_nRefreshRateDenominator;  // Refresh rate = numerator / denominator.
}
