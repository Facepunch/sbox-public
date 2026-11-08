using Sandbox.Rendering;
using System;

namespace BasaltTests;

/// <summary>
/// The engine's post-process layers, which hand Basalt the effects it runs early on the async compute and graphics queues.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AsyncPostProcessTests
{
	[TestMethod]
	public void NativeEffectsAreAssignedBeforeWorkersRecordTheirStage()
	{
		var effects = new PostProcessLayers();
		var ao = effects.CreateLayer( Stage.AfterDepthPrepass );
		ao.EarlyQueue = PostProcessQueue.AsyncCompute;
		var ssr = effects.CreateLayer( Stage.AfterDepthPrepass );
		ssr.EarlyQueue = PostProcessQueue.Graphics;
		var ordinary = effects.CreateLayer( Stage.AfterDepthPrepass );
		ordinary.CommandList = new CommandList { Enabled = false };

		effects.ScheduleEarlyPostProcess( Stage.AfterDepthPrepass, true );
		// The hook may record first. Null lists make an accidental early execution fail.
		effects.OnRenderStage( Stage.AfterDepthPrepass );
		ao.CommandList = ssr.CommandList = new CommandList { Enabled = false };
		ordinary.CommandList = null;
		System.Threading.Tasks.Parallel.Invoke(
			() => effects.RenderEarlyPostProcess( Stage.AfterDepthPrepass, PostProcessQueue.AsyncCompute ),
			() => effects.RenderEarlyPostProcess( Stage.AfterDepthPrepass, PostProcessQueue.Graphics ) );
		Assert.IsFalse( ao.RanEarly );
		Assert.IsFalse( ssr.RanEarly );

		// Returning to serial rendering restores these effects to the ordinary stage.
		effects.ScheduleEarlyPostProcess( Stage.AfterDepthPrepass, false );
		ordinary.CommandList = new CommandList { Enabled = false };
		ao.CommandList = null;
		Assert.ThrowsException<NullReferenceException>( () => effects.OnRenderStage( Stage.AfterDepthPrepass ) );
	}

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
