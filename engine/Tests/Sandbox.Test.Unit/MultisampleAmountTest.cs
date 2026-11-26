using NativeEngine;

namespace SystemTest;

[TestClass]
public class MultisampleAmountTest
{
	/// <summary>
	/// The screen's amount outside a render is no multisampling: the window's swap chain is never multisampled.
	/// </summary>
	[TestMethod]
	public void ScreenAmountOutsideARenderIsNone()
	{
		Assert.AreEqual( RenderMultisampleType.RENDER_MULTISAMPLE_NONE, MultisampleAmount.MultisampleScreen.ToEngine() );
		Assert.AreEqual( RenderMultisampleType.RENDER_MULTISAMPLE_4X, MultisampleAmount.Multisample4x.ToEngine() );
	}
}
