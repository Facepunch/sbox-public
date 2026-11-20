using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Collections.Generic;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// A chunk load cannot silently discard the named-file registry or its execution state.
	/// </summary>
	[TestMethod]
	public void ChunkLoadCannotDiscardNamedOwnership()
	{
		var sources = new Dictionary<string, string> { ["owned.csx"] = "class OwnedBatch {}" };
		Assert.IsTrue( Game.Scripting.LoadFiles( sources ).Success );
		Assert.ThrowsException<InvalidOperationException>( () => Game.Scripting.Load( "" ) );
		sources["owned.csx"] = "class OwnedBatch { public int Read() { return 42; } }";
		Assert.IsTrue( Game.Scripting.LoadFiles( sources ).Success );
		Assert.AreEqual( 42, Game.Scripting.Run<int>( "return new OwnedBatch().Read();" ) );
	}

	/// <summary>
	/// A publication interrupted after commit keeps its diagnostic and can be repaired by loading source again.
	/// </summary>
	[TestMethod]
	[DataRow( false, false, false )]
	[DataRow( true, false, false )]
	[DataRow( false, true, false )]
	[DataRow( false, true, true )]
	public void InterruptedPublicationRecoversInPlace( bool removeAll, bool tick, bool invalidEdit )
	{
		var replace = context.ReplaceScriptAssembly;
		var scripting = Game.Scripting;
		Assert.IsTrue( scripting.LoadFile( "existing.csx", "int Existing() { return 41; }" ).Success );
		try
		{
			context.ReplaceScriptAssembly = ( _, _ ) => throw new InvalidOperationException( "Injected assembly migration failure" );
			var result = Game.Scripting.LoadFiles( new Dictionary<string, string> { ["fault.csx"] = "class BatchFault {}" } );
			Assert.IsFalse( result.Success );
			Assert.IsTrue( result.Published );
			Assert.IsTrue( result.RequiresReload );
			Assert.AreEqual( "Injected assembly migration failure", result.Diagnostics.Single().Message );
			Assert.AreEqual( result.Diagnostics.Single().Message, Game.Scripting.LastError.Message );
			Assert.IsTrue( Game.Scripting.RequiresReload );
			Assert.ThrowsException<InvalidOperationException>( () => Game.Scripting.CreateScript( "return 1;" ) );
		}
		finally
		{
			context.ReplaceScriptAssembly = replace;
		}

		if ( tick )
		{
			if ( invalidEdit )
			{
				Assert.IsFalse( scripting.LoadFile( "fault.csx", "invalid !!!" ).Success );
			}
			context.TickScripting();
			Assert.IsFalse( scripting.RequiresReload );
			Assert.AreEqual( 41, scripting.Run<int>( "return Existing();" ) );
			Assert.IsNull( Game.TypeLibrary.GetType( "BatchFault" ) );
		}

		var repaired = removeAll ? scripting.LoadFiles( new Dictionary<string, string>() )
			: scripting.LoadFile( "fault.csx", "int Repaired() { return Existing() + 1; }" );
		Assert.IsTrue( repaired.Success, string.Join( "\n", repaired.Diagnostics ) );
		Assert.IsFalse( scripting.RequiresReload );
		if ( removeAll )
		{
			Assert.IsTrue( scripting.Load( "int Repaired() { return 42; }" ) );
		}
		Assert.AreEqual( 42, scripting.Run<int>( "return Repaired();" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "BatchFault" ) );
	}

	/// <summary>
	/// If the loader queues a replacement but migration cannot start, recovery follows the queued assembly chain.
	/// </summary>
	[TestMethod]
	public void InterruptedReferenceMigrationRecoversLiveInstances()
	{
		var scripting = Game.Scripting;
		const string original = "class Editable { public int Saved; }";
		const string edited = "class Editable { public int Saved; public int Added = 7; }";
		Assert.IsTrue( scripting.LoadFile( "edit.csx", original ).Success );
		reloadRoots = [scripting.Run<object>( "return new Editable { Saved = 42 };" )];

		void Interrupt() => throw new InvalidOperationException( "Interrupted before migration" );
		scriptPackageLoader.HotloadManager.PreSwap += Interrupt;
		try
		{
			var result = scripting.LoadFile( "edit.csx", edited );
			Assert.IsFalse( result.Success );
			Assert.IsTrue( result.Published );
			Assert.IsTrue( result.RequiresReload );
			Assert.AreEqual( "Interrupted before migration", scripting.LastError.Message );
		}
		finally
		{
			scriptPackageLoader.HotloadManager.PreSwap -= Interrupt;
		}

		context.TickScripting();
		var restored = reloadRoots[0];
		Assert.IsFalse( scripting.RequiresReload );
		Assert.AreSame( Game.TypeLibrary.GetType( "Editable" ).TargetType, restored.GetType() );
		Assert.IsNull( restored.GetType().GetField( "Added" ) );
		Assert.AreEqual( 42, restored.GetType().GetField( "Saved" ).GetValue( restored ) );

		Assert.IsTrue( scripting.LoadFile( "edit.csx", edited ).Success );
		var updated = reloadRoots[0];
		Assert.AreEqual( 42, updated.GetType().GetField( "Saved" ).GetValue( updated ) );
		Assert.AreEqual( 7, updated.GetType().GetField( "Added" ).GetValue( updated ) );
	}

	/// <summary>
	/// A persistent publication failure is retried once automatically. The next source edit can still repair it.
	/// </summary>
	[TestMethod]
	public void FailedAutomaticRestoreWaitsForNextEdit()
	{
		var scripting = Game.Scripting;
		Assert.IsTrue( scripting.LoadFile( "existing.csx", "class Existing {}" ).Success );
		var replace = context.ReplaceScriptAssembly;
		var attempts = 0;
		context.ReplaceScriptAssembly = ( _, _ ) =>
		{
			attempts++;
			throw new InvalidOperationException( "Injected publication failure" );
		};
		try
		{
			Assert.IsFalse( scripting.LoadFile( "existing.csx", "class Existing { public int Value; }" ).Success );
			context.TickScripting();
			context.TickScripting();
			Assert.AreEqual( 2, attempts );
		}
		finally
		{
			context.ReplaceScriptAssembly = replace;
		}

		Assert.IsTrue( scripting.LoadFile( "existing.csx", "class Existing { public int Value = 42; }" ).Success );
		Assert.AreEqual( 42, scripting.Run<int>( "return new Existing().Value;" ) );
	}

	/// <summary>
	/// An initializer that throws on existing objects leaves their other fields and the next source edit usable.
	/// </summary>
	[TestMethod]
	public void SourceEditSurvivesFailedFieldInitializer()
	{
		var scripting = Game.Scripting;
		Assert.IsTrue( scripting.LoadFile( "edit.csx", "class Editable { public int Saved; }" ).Success );
		reloadRoots = [scripting.Run<object>( "return new Editable { Saved = 42 };" )];
		var result = scripting.LoadFile( "edit.csx", "class Editable { public int Broken = int.Parse(\"bad\"); public int Saved; }" );
		Assert.IsTrue( result.Success, string.Join( "\n", result.Diagnostics ) );
		var instance = reloadRoots[0];
		Assert.AreSame( Game.TypeLibrary.GetType( "Editable" ).TargetType, instance.GetType() );
		Assert.AreEqual( 42, instance.GetType().GetField( "Saved" ).GetValue( instance ) );
		Assert.AreEqual( 0, instance.GetType().GetField( "Broken" ).GetValue( instance ) );
		Assert.IsTrue( scripting.LoadFile( "edit.csx", "class Editable { public int Saved; public int Fixed = 7; }" ).Success );
		instance = reloadRoots[0];
		Assert.AreEqual( 7, instance.GetType().GetField( "Fixed" ).GetValue( instance ) );
	}

	/// <summary>
	/// Forward references and simultaneous caller/member renames publish coherently.
	/// </summary>
	[TestMethod]
	public void RelatedEditsPublishTogetherAndKeepUnchangedState()
	{
		var files = new Dictionary<string, string>
		{
			["a-caller.csx"] = "class BatchCaller { public int Read() { return BatchArt.Old(); } }",
			["z-art.csx"] = "class BatchArt { public static int Old() { return 17; } }",
			["state.csx"] = "int count = 0; int NextBatchCount() { return ++count; }"
		};
		var first = Game.Scripting.LoadFiles( files );
		Assert.IsTrue( first.Success, string.Join( "; ", first.Diagnostics ) );
		Assert.AreEqual( 17, Game.Scripting.Run<int>( "return new BatchCaller().Read();" ) );
		Assert.AreEqual( 1, Game.Scripting.Run<int>( "return NextBatchCount();" ) );
		files["a-caller.csx"] = "class BatchCaller { public int Read() { return BatchArt.New(); } }";
		files["z-art.csx"] = "class BatchArt { public static int New() { return 42; } }";
		var second = Game.Scripting.LoadFiles( files );
		Assert.IsTrue( second.Success, string.Join( "; ", second.Diagnostics ) );
		Assert.AreEqual( 42, Game.Scripting.Run<int>( "return new BatchCaller().Read();" ) );
		Assert.AreEqual( 2, Game.Scripting.Run<int>( "return NextBatchCount();" ) );
		Assert.IsFalse( Game.Scripting.LoadFiles( files ).Published );
	}

	/// <summary>
	/// A syntax rejection preserves the prior generation and reports the physical source file.
	/// </summary>
	[TestMethod]
	public void RejectedBatchKeepsPriorGenerationAndSourceLocation()
	{
		Assert.IsTrue( Game.Scripting.LoadFiles( new Dictionary<string, string> { ["live.csx"] = "class BatchLive { public int Read() { return 7; } }" } ).Success );
		var failed = Game.Scripting.LoadFiles( new Dictionary<string, string>
		{
			["live.csx"] = "class BatchLive { public int Read() { return 9; } }",
			["broken.csx"] = "\n\nclass Broken { public int = ; }"
		} );
		Assert.IsFalse( failed.Success );
		Assert.IsFalse( failed.Published );
		Assert.IsFalse( failed.RequiresReload );
		Assert.IsTrue( failed.Diagnostics.Any( x => x.FilePath == "broken.csx" && x.Line == 3 ), string.Join( "; ", failed.Diagnostics ) );
		Assert.AreEqual( 7, Game.Scripting.Run<int>( "return new BatchLive().Read();" ) );
	}

	/// <summary>
	/// Removing a complete dependency group removes its declarations from the catalog.
	/// </summary>
	[TestMethod]
	public void OmittedFilesAreRemovedTogether()
	{
		Assert.IsTrue( Game.Scripting.LoadFiles( new Dictionary<string, string>
		{
			["parent.csx"] = "class BatchParent {}",
			["child.csx"] = "class BatchChild : BatchParent {}"
		} ).Success );
		var result = Game.Scripting.LoadFiles( new Dictionary<string, string>() );
		Assert.IsTrue( result.Success, string.Join( "; ", result.Diagnostics ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "BatchParent" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "BatchChild" ) );
	}

	/// <summary>
	/// The source limit applies to each document, not to a concatenation of the workspace.
	/// </summary>
	[TestMethod]
	public void CombinedSourceCanExceedChunkLimit()
	{
		var padding = "/*" + new string( ' ', 40000 ) + "*/";
		var result = Game.Scripting.LoadFiles( new Dictionary<string, string>
		{
			["large-a.csx"] = padding + "class LargeBatchA {}",
			["large-b.csx"] = padding + "class LargeBatchB {}"
		} );
		Assert.IsTrue( result.Success, string.Join( "; ", result.Diagnostics ) );
	}

	/// <summary>
	/// Startup failures retain the publication and remain failures on unchanged retries.
	/// </summary>
	[TestMethod]
	public void StartupFailureDoesNotPretendToRollBackOrReplay()
	{
		var files = new Dictionary<string, string> { ["startup.csx"] = "int zero = 0; var value = 1 / zero; class BatchStarted {}" };
		var failed = Game.Scripting.LoadFiles( files );
		Assert.IsFalse( failed.Success );
		Assert.IsTrue( failed.Published );
		Assert.IsFalse( failed.RequiresReload );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "BatchStarted" ) );
		var retry = Game.Scripting.LoadFiles( files );
		Assert.IsFalse( retry.Success );
		Assert.IsFalse( retry.Published );
		files["startup.csx"] = "class BatchStarted {}";
		Assert.IsTrue( Game.Scripting.LoadFiles( files ).Success );
	}

	/// <summary>
	/// A scene notification failure leaves the published script types usable on this and the next edit.
	/// </summary>
	[TestMethod]
	public void NotificationFailureLeavesScriptEditsUsable()
	{
		var scripting = Game.Scripting;
		Assert.IsTrue( scripting.LoadFile( "edit.csx", "class Example { public int Value; }" ).Success );
		var notifications = 0;
		scriptPackageLoader.OnAfterHotload = () =>
		{
			notifications++;
			throw new InvalidOperationException( "Injected hotload notification failure" );
		};

		Assert.IsTrue( scripting.LoadFile( "edit.csx", "class Example { public int Second = 2; }" ).Success );
		Assert.AreEqual( 2, scripting.Run<int>( "return new Example().Second;" ) );
		Assert.IsTrue( scripting.LoadFile( "edit.csx", "class Example { public int Third = 3; }" ).Success );
		Assert.AreEqual( 3, scripting.Run<int>( "return new Example().Third;" ) );
		Assert.AreEqual( 2, notifications );
	}
}
