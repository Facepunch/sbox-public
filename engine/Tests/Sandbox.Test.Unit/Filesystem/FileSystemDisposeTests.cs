using System;
using System.Threading.Tasks;

namespace FilesystemTests;

/// <summary>
/// Filesystem shutdown must allow background change callbacks to finish.
/// </summary>
[TestClass]
public class FileSystemDisposeTests
{
	private sealed class CallbackFileSystem : Zio.FileSystems.MemoryFileSystem
	{
		internal Action BeforeDispose { get; set; }

		/// <inheritdoc />
		protected override void Dispose( bool disposing )
		{
			if ( disposing )
				BeforeDispose?.Invoke();

			base.Dispose( disposing );
		}
	}

	/// <summary>
	/// Models a filesystem joining its event worker while a final change callback is in flight.
	/// The callback must finish during disposal and must not requeue the disposed filesystem.
	/// </summary>
	[TestMethod]
	public void DisposalDoesNotHoldTheChangeCallbackLock()
	{
		using var backend = new CallbackFileSystem();
		var filesystem = new BaseFileSystem( backend );
		filesystem.AddChangedFile( "pending.txt" );

		Task callback = null;
		var completedDuringDispose = false;
		backend.BeforeDispose = () =>
		{
			callback = Task.Run( () =>
			{
				// Model a callback that already passed the initial WatchEnabled check.
				lock ( FileWatch.WithChanges )
				{
					filesystem.AddChangedFile( "late.txt" );
				}
			} );
			completedDuringDispose = callback.Wait( TimeSpan.FromSeconds( 2 ) );
		};

		filesystem.Dispose();
		callback.GetAwaiter().GetResult();
		Assert.IsTrue( completedDuringDispose, "The event worker must not need a lock held by Dispose." );
		Assert.IsFalse( filesystem.IsValid );

		lock ( FileWatch.WithChanges )
		{
			Assert.AreEqual( 0, filesystem.changedFiles.Count );
			Assert.IsFalse( FileWatch.WithChanges.Contains( filesystem ) );
		}
	}
}
