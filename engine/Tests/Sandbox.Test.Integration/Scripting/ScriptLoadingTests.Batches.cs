using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.Reflection;

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
	/// A failed assembly migration keeps its original message and explicitly requires recovery.
	/// </summary>
	[TestMethod]
	public void BatchPublicationFailureRetainsOriginalDiagnostic()
	{
		var replace = context.ReplaceScriptAssembly;
		try
		{
			context.ReplaceScriptAssembly = ( _, _, _ ) => throw new InvalidOperationException( "Injected assembly migration failure" );
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
	/// The engine hotload-failure flag blocks execution while preserving the original diagnostics.
	/// </summary>
	[TestMethod]
	public void FaultedContextStillExposesFailureAndReloadState()
	{
		var scripting = Game.Scripting;
		Assert.IsFalse( scripting.Load( "class {", "fault.csx" ) );
		var original = scripting.LastError;
		typeof( ScriptSystem ).GetField( "_hotloadFailed", BindingFlags.Instance | BindingFlags.NonPublic ).SetValue( scripting, true );
		Assert.IsTrue( scripting.RequiresReload );
		Assert.IsTrue( scripting.Diagnostics.Any( x => x.Message == original.Message ) );
		Assert.ThrowsException<InvalidOperationException>( () => scripting.CreateScript( "return 1;" ) );
	}
}
