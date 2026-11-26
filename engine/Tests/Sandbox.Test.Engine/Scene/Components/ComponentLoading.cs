using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;

namespace SceneTests.Components;

/// <summary>
/// Cloning invokes each component's load callback, including callbacks that finish after the clone returns.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComponentLoadingTest : SceneTest
{
	/// <summary>
	/// An immediate or deferred addon failure is reported once with its original exception.
	/// The component remains eligible to start.
	/// </summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void FailedLoadReportsOriginalException( bool failImmediately )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var source = scene.CreateObject();
		source.Components.Create<LoadProbe>().FailImmediately = failImmediately;
		var errors = new List<LogEvent>();

		void OnLog( LogEvent entry )
		{
			if ( entry.Exception is UnauthorizedAccessException )
				errors.Add( entry );
		}

		Logging.OnEmitted += OnLog;
		try
		{
			var clone = source.Clone();
			var probe = clone.Components.Get<LoadProbe>();

			if ( !failImmediately )
				probe.Completion.SetException( probe.Failure );

			Assert.AreEqual( 1, errors.Count );
			Assert.AreSame( probe.Failure, errors[0].Exception );
			StringAssert.Contains( errors[0].Message, nameof( LoadProbe ) );
			Assert.IsFalse( clone.Flags.Contains( GameObjectFlags.Loading ) );

			scene.RunPendingStarts();
			Assert.IsTrue( probe.Active && probe.StartCalled );
		}
		finally
		{
			Logging.OnEmitted -= OnLog;
			scene.Destroy();
		}
	}

	/// <summary>
	/// A successful sibling cannot clear the object's loading state early. The last load must
	/// still release that state when its component was removed while awaiting completion.
	/// </summary>
	[TestMethod]
	public void SiblingAndRemovedComponentFinishIndependently()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var source = scene.CreateObject();
		source.Components.Create<LoadProbe>();
		source.Components.Create<LoadProbe>();

		var clone = source.Clone();
		var probes = clone.Components.GetAll<LoadProbe>().ToArray();
		probes[0].Completion.SetResult();
		Assert.IsTrue( clone.Flags.Contains( GameObjectFlags.Loading ) );

		probes[1].Destroy();
		probes[1].Completion.SetCanceled();
		Assert.IsFalse( clone.Flags.Contains( GameObjectFlags.Loading ) );
		scene.RunPendingStarts();
		Assert.IsTrue( probes[0].StartCalled );
		Assert.IsFalse( probes[1].IsValid );
		scene.Destroy();
	}
}

/// <summary>
/// Provides a controllable load for a cloned component.
/// </summary>
public sealed class LoadProbe : Component
{
	/// <summary>
	/// Return an already-faulted task when the clone begins loading.
	/// </summary>
	[Property] public bool FailImmediately { get; set; }

	internal TaskCompletionSource Completion { get; } = new();
	internal UnauthorizedAccessException Failure { get; } = new( "Rejected probe load" );

	protected override Task OnLoad()
	{
		return FailImmediately ? System.Threading.Tasks.Task.FromException( Failure ) : Completion.Task;
	}
}
