using Sandbox.Resources;
using System;
using System.Threading;

namespace ResourceTests;

/// <summary>
/// Resource compilers can call native child compilers, so synchronous work and captured
/// continuations must stay on the thread that entered the native compiler.
/// </summary>
[TestClass]
public class ResourceCompilerThreadTests
{
	sealed class CallbackCompiler( Func<Task<bool>> callback ) : ResourceCompiler
	{
		protected override Task<bool> Compile() => callback();
	}

	sealed class CallbackTextureGenerator( Func<Task> callback ) : TextureGenerator
	{
		protected override async ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
		{
			await callback();
			return null;
		}
	}

	/// <summary>
	/// Texture generation retains the caller's context, with or without a compiler in its options.
	/// </summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	[Timeout( 5000 )]
	public void TextureGenerationRetainsCallingThread( bool compiling )
	{
		var caller = Environment.CurrentManagedThreadId;
		var before = 0;
		var after = 0;
		var generator = new CallbackTextureGenerator( async () =>
		{
			before = Environment.CurrentManagedThreadId;
			await Task.Yield();
			after = Environment.CurrentManagedThreadId;
		} );
		CallbackCompiler compiler = null;
		compiler = new CallbackCompiler( async () =>
		{
			var options = compiling
				? new ResourceGenerator.Options { ForDisk = true, Compiler = compiler }
				: ResourceGenerator.Options.Default;

			await generator.CreateAsync( options, default );
			return true;
		} );

		Assert.IsTrue( compiler.CompileInternal() );
		Assert.AreEqual( caller, before );
		Assert.AreEqual( caller, after );
	}

	/// <summary>
	/// Both the initial call and a yielded continuation retain the native caller's thread.
	/// </summary>
	[TestMethod]
	[Timeout( 5000 )]
	public void CompilationRetainsCallingThread()
	{
		var caller = Environment.CurrentManagedThreadId;
		var before = 0;
		var after = 0;
		var previous = SynchronizationContext.Current;
		var compiler = new CallbackCompiler( async () =>
		{
			before = Environment.CurrentManagedThreadId;
			await Task.Yield();
			after = Environment.CurrentManagedThreadId;
			return true;
		} );

		Assert.IsTrue( compiler.CompileInternal() );
		Assert.AreEqual( caller, before );
		Assert.AreEqual( caller, after );
		Assert.AreSame( previous, SynchronizationContext.Current );
	}

	/// <summary>
	/// A child compiler can yield while its parent continuation is executing, without
	/// pumping unrelated editor work or losing the parent's synchronization context.
	/// </summary>
	[TestMethod]
	[Timeout( 5000 )]
	public void NestedCompilationRestoresParentContext()
	{
		var caller = Environment.CurrentManagedThreadId;
		var childThread = 0;
		SynchronizationContext parent = null;
		SynchronizationContext restored = null;
		var child = new CallbackCompiler( async () =>
		{
			await Task.Yield();
			childThread = Environment.CurrentManagedThreadId;
			return true;
		} );
		var compiler = new CallbackCompiler( async () =>
		{
			await Task.Yield();
			parent = SynchronizationContext.Current;
			var result = child.CompileInternal();
			restored = SynchronizationContext.Current;
			return result;
		} );

		Assert.IsTrue( compiler.CompileInternal() );
		Assert.AreEqual( caller, childThread );
		Assert.IsNotNull( parent );
		Assert.AreSame( parent, restored );
	}

	/// <summary>
	/// Completion on a worker wakes the caller even when no continuation was posted.
	/// </summary>
	[TestMethod]
	[Timeout( 5000 )]
	public void WorkerCompletionDoesNotNeedAQueuedContinuation()
	{
		var compiler = new CallbackCompiler( () => Task.Run( () => false ) );
		Assert.IsFalse( compiler.CompileInternal() );
	}

	/// <summary>
	/// A failed compile must restore the caller's context as well as return failure.
	/// </summary>
	[TestMethod]
	[Timeout( 5000 )]
	public void FailureRestoresCallingContext()
	{
		var previous = SynchronizationContext.Current;
		var compiler = new CallbackCompiler( async () =>
		{
			await Task.Yield();
			throw new InvalidOperationException( "Expected compiler failure" );
		} );

		Assert.IsFalse( compiler.CompileInternal() );
		Assert.AreSame( previous, SynchronizationContext.Current );
	}
}
