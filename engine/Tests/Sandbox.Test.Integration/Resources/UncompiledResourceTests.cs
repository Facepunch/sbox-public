using Sandbox.Engine;
using Sandbox.Tasks;
using System;
using System.IO;

namespace ResourceTests;

/// <summary>
/// Exercises source loading through the ordinary resource API using an asset type unrelated to scripting.
/// </summary>
[TestClass, DoNotParallelize]
public partial class UncompiledResourceTests
{
	private GlobalContext previous;
	private GlobalContext context;
	private MemoryFileSystem files;

	/// <summary>
	/// A test asset that consumes the original file rather than a compiled JSON container.
	/// </summary>
	[Expose, AssetType( Name = "Test Source", Extension = "rawtest", Flags = AssetTypeFlags.Uncompiled )]
	public sealed class TestSource : Resource
	{
		/// <summary>
		/// Text delivered through the resource load context.
		/// </summary>
		public string Text { get; private set; }

		/// <summary>
		/// Original byte count, including any UTF-8 byte order mark.
		/// </summary>
		public int ByteCount { get; private set; }

		/// <summary>
		/// Number of times this instance has received source data.
		/// </summary>
		public int Loads { get; private set; }

		/// <inheritdoc />
		public override bool IsValid => Text is not null;

		/// <inheritdoc />
		protected internal override void OnLoaded( ResourceLoadContext context )
		{
			Assert.IsTrue( context.IsUncompiled );
			Assert.AreSame( this, ResourceLibrary.Get<TestSource>( ResourcePath ) );
			ByteCount = context.ReadSource().Length;
			Text = context.ReadSourceAsString();
			if ( Text == "fail" )
				throw new InvalidDataException( "Expected source load failure." );

			Loads++;
		}
	}

	/// <summary>
	/// Creates an isolated mounted filesystem and resource registry.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		ThreadSafe.MarkMainThread();
		previous = GlobalContext.Current;
		files = new MemoryFileSystem();
		context = new GlobalContext
		{
			TypeLibrary = SharedTypeLibrary.EngineAndTests,
			FileMount = files,
			JsonSerializerOptions = previous.JsonSerializerOptions
		};
		GlobalContext.Current = context;
	}

	/// <summary>
	/// Releases the test resources and restores the previous context.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		context.Shutdown();
		GlobalContext.Current = previous;
		files.Dispose();
	}

	/// <summary>
	/// Typed, base-type and async loads share one instance and preserve the original bytes.
	/// </summary>
	[TestMethod]
	public void SourceLoadsThroughNormalResourceApis()
	{
		files.WriteAllText( "hello.rawtest", "\uFEFFplain source" );
		var resource = Resource.Load( typeof( Resource ), "hello.rawtest" ) as TestSource;
		Assert.IsNotNull( resource );
		Assert.AreEqual( "plain source", resource.Text );
		Assert.AreEqual( 15, resource.ByteCount );
		Assert.AreSame( resource, Resource.Load( typeof( TestSource ), "hello.rawtest" ) );
		Assert.AreSame( resource, AsyncPump.RunSynchronously( () => ResourceLibrary.LoadAsync<Resource>( "hello.rawtest" ) ) );
		Assert.AreEqual( 1, resource.Loads );
		Assert.IsNull( Resource.Load( typeof( SoundFile ), "hello.rawtest" ) );
		Assert.IsNull( Resource.Load( typeof( TestSource ), "hello.rawtest_c" ) );
		Assert.IsNull( AsyncPump.RunSynchronously( () => ResourceLibrary.LoadAsync<TestSource>( "hello.rawtest_c" ) ) );
		Assert.IsFalse( files.FileExists( "hello.rawtest_c" ) );
	}

	/// <summary>
	/// Missing source does not unload a resource; recreating the file reloads the same cached instance.
	/// </summary>
	[TestMethod]
	public void MissingSourceRetainsTheLoadedResource()
	{
		files.WriteAllText( "retained.rawtest", "before" );
		var resource = (TestSource)Resource.Load( typeof( TestSource ), "retained.rawtest" );
		var assetType = context.TypeLibrary.GetType<TestSource>().GetAttribute<AssetTypeAttribute>();

		files.DeleteFile( "retained.rawtest" );
		Assert.AreSame( resource, Game.Resources.LoadUncompiledResource( assetType, "retained.rawtest", files, reloadExisting: true ) );
		Assert.AreSame( resource, ResourceLibrary.Get<TestSource>( "retained.rawtest" ) );
		Assert.AreEqual( "before", resource.Text );
		Assert.AreEqual( 1, resource.Loads );

		files.WriteAllText( "retained.rawtest", "after" );
		Assert.AreSame( resource, Game.Resources.LoadUncompiledResource( assetType, "retained.rawtest", files, reloadExisting: true ) );
		Assert.AreEqual( "after", resource.Text );
		Assert.AreEqual( 2, resource.Loads );
	}

	/// <summary>
	/// An async request can load source before any synchronous request has populated the cache.
	/// </summary>
	[TestMethod]
	public void AsyncLoadsUncachedSource()
	{
		files.WriteAllText( "async.rawtest", "async source" );
		var resource = AsyncPump.RunSynchronously( () => ResourceLibrary.LoadAsync<TestSource>( "async.rawtest" ) );
		Assert.IsNotNull( resource );
		Assert.AreEqual( "async source", resource.Text );
		Assert.AreSame( resource, ResourceLibrary.Get<TestSource>( "async.rawtest" ) );
	}

	/// <summary>
	/// JSON paths load source on demand and GUID references reuse the registered instance.
	/// </summary>
	[TestMethod]
	public void JsonReferencesLoadAndReuseSource()
	{
		files.WriteAllText( "reference.rawtest", "json source" );
		var resource = Json.Deserialize<TestSource>( "\"reference.rawtest\"" );
		Assert.IsNotNull( resource );
		Assert.AreEqual( "json source", resource.Text );
		Game.Resources.AssignGuid( resource, Guid.NewGuid() );
		Assert.AreSame( resource, Json.Deserialize<TestSource>( Json.Serialize( resource ) ) );
		Assert.AreEqual( 1, resource.Loads );
	}

	/// <summary>
	/// A known GUID remains authoritative when the serialized path is stale or has a different extension.
	/// </summary>
	[TestMethod]
	public void GuidReferenceTakesPrecedenceOverStalePath()
	{
		files.WriteAllText( "current.rawtest", "source" );
		var resource = Resource.Load( typeof( TestSource ), "current.rawtest" );
		var guid = Guid.NewGuid();
		Game.Resources.AssignGuid( resource, guid );
		var reference = Json.Serialize( new { Id = guid, Path = "old.vmdl" } );
		Assert.AreSame( resource, Json.Deserialize<TestSource>( reference ) );
	}

	/// <summary>
	/// Failed loads leave no cached instance and can be retried after the source is corrected.
	/// </summary>
	[TestMethod]
	public void FailedSourceCanBeRetried()
	{
		files.WriteAllText( "retry.rawtest", "fail" );
		Assert.IsNull( Resource.Load( typeof( TestSource ), "retry.rawtest" ) );
		Assert.IsNull( Json.Deserialize<TestSource>( "\"retry.rawtest\"" ) );
		Assert.IsNull( ResourceLibrary.Get<TestSource>( "retry.rawtest" ) );
		files.WriteAllText( "retry.rawtest", "repaired" );
		var resource = Resource.Load( typeof( TestSource ), "retry.rawtest" ) as TestSource;
		Assert.IsNotNull( resource );
		Assert.AreEqual( "repaired", resource.Text );
		Assert.AreEqual( 1, resource.Loads );
	}

	/// <summary>
	/// Source assets retain their strong registry lifetime when assigned a GUID or moved.
	/// </summary>
	[TestMethod]
	public void IdentityChangesPreserveTheResource()
	{
		files.WriteAllText( "before.rawtest", "source" );
		var resource = Resource.Load( typeof( TestSource ), "before.rawtest" );
		var first = Guid.NewGuid();
		var second = Guid.NewGuid();
		Game.Resources.AssignGuid( resource, first );
		Game.Resources.AssignGuid( resource, second );
		Assert.IsNull( Game.Resources.Get<TestSource>( first ) );
		Assert.AreSame( resource, Game.Resources.Get<TestSource>( second ) );

		Game.Resources.MoveResource( resource, "after.rawtest" );
		Assert.IsNull( ResourceLibrary.Get<TestSource>( "before.rawtest" ) );
		Assert.AreSame( resource, ResourceLibrary.Get<TestSource>( "after.rawtest" ) );
		Assert.AreSame( resource, Game.Resources.Get<TestSource>( second ) );
	}
}
