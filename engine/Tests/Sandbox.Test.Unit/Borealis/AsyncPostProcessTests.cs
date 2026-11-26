using Sandbox.Rendering;

namespace BorealisTests;

/// <summary>
/// The engine's post-process layers, which hand Borealis the effects it runs early on the async compute and graphics queues.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AsyncPostProcessTests
{
	[TestMethod]
	public void EarlyEffectsAreQueueSpecificAndSkippedExactlyOnceAtTheirStage()
	{
		var effects = new PostProcessLayers();
		var ao = effects.CreateLayer( Stage.AfterDepthPrepass );
		ao.EarlyQueue = PostProcessQueue.AsyncCompute;
		ao.CommandList = new CommandList { Enabled = false };
		var ssr = effects.CreateLayer( Stage.AfterDepthPrepass );
		ssr.EarlyQueue = PostProcessQueue.Graphics;
		ssr.CommandList = new CommandList { Enabled = false };
		var ordinary = effects.CreateLayer( Stage.AfterDepthPrepass );

		effects.RenderEarlyPostProcess( Stage.AfterDepthPrepass, PostProcessQueue.AsyncCompute );
		Assert.IsTrue( ao.RanEarly );
		Assert.IsFalse( ssr.RanEarly );
		Assert.IsFalse( ordinary.RanEarly );
		effects.RenderEarlyPostProcess( Stage.AfterDepthPrepass, PostProcessQueue.Graphics );
		Assert.IsTrue( ssr.RanEarly );

		// A repeated early dispatch and the normal stage must skip these lists, even if they are no longer executable.
		ao.CommandList = ssr.CommandList = null;
		effects.RenderEarlyPostProcess( Stage.AfterDepthPrepass, PostProcessQueue.Graphics );
		ordinary.CommandList = new CommandList { Enabled = false };
		effects.OnRenderStage( Stage.AfterDepthPrepass );
		Assert.IsFalse( ao.RanEarly );
		Assert.IsFalse( ssr.RanEarly );
		Assert.IsFalse( effects.HasEarlyPostProcess( Stage.AfterOpaque, PostProcessQueue.Graphics ) );
	}
}
