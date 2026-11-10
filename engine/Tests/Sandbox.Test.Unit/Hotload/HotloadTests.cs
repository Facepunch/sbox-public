extern alias After;
extern alias Before;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Sandbox;
using Sandbox.ActionGraphs;
using Sandbox.Internal;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NodeLibrary = Facepunch.ActionGraphs.NodeLibrary;

namespace HotloadTests
{
	[AttributeUsage( AttributeTargets.Field )]
	public sealed class ResetAttribute : Attribute { }

	public abstract class HotloadTestBase
	{
		/// <summary>
		/// Static fields marked with each reset attribute. Scanning every type in the assembly
		/// for them is slow, so do it once per attribute rather than once per test. Hotload must
		/// not walk this: it would swap the Before attribute keys for After ones.
		/// </summary>
		[SkipHotload]
		private static readonly ConcurrentDictionary<Type, FieldInfo[]> _resetFields = new();

		private static void ResetStaticFields<TAttrib>()
			where TAttrib : Attribute
		{
			var fields = _resetFields.GetOrAdd( typeof( TAttrib ), static attribType => attribType.Assembly.GetTypes()
				.SelectMany( type => type.GetFields( BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic ) )
				.Where( fieldInfo => fieldInfo.IsDefined( attribType ) )
				.ToArray() );

			foreach ( var fieldInfo in fields )
			{
				fieldInfo.SetValue( null, fieldInfo.FieldType.IsValueType ? Activator.CreateInstance( fieldInfo.FieldType ) : null );
			}
		}

		protected static Sandbox.Hotload CreateHotload()
		{
			Sandbox.Hotload.AssemblyNameFormatter = name => name.Name;

			var hotload = new Sandbox.Hotload { TraceRoots = true, TracePaths = true, IncludeTypeTimings = true, IncludeProcessorTimings = true };

			// Only the hotload tests' own statics are roots. Watching all of Sandbox.Test.Unit walks
			// every cached test fixture too (the layout conformance XML alone is ~180k objects).
			hotload.WatchAssembly( "Sandbox.Test.Unit", t => t.Namespace == nameof( HotloadTests ) );
			hotload.WatchAssembly( "Sandbox.Engine", t => t.Name == nameof( ReflectionQueryCache ) );
			hotload.ReplacingAssembly( typeof( Before.TestClass1 ).Assembly, typeof( After.TestClass1 ).Assembly );
			hotload.AssemblyResolver = new DefaultAssemblyResolver();

			hotload.AddUpgrader<Sandbox.Upgraders.AutoSkipUpgrader>();
			hotload.AddUpgraders( typeof( TypeDescription ).Assembly );

			return hotload;
		}

		protected static HotloadResult Hotload( bool allowErrors = false )
		{
			var hotload = CreateHotload();
			var result = hotload.UpdateReferences();

			Assert.IsFalse( result.NoAction );

			Console.WriteLine( $"NoAction = {result.NoAction}" );
			Console.WriteLine( $"InstancesProcessed = {result.InstancesProcessed}" );
			Console.WriteLine( $"ProcessingTime = {result.ProcessingTime:F2}ms" );
			Console.WriteLine( $"result.Entries = {result.Entries.Count}" );

			foreach ( var entry in result.Entries )
			{
				Console.WriteLine( $"  [{entry.Type}] {entry.ToString().Replace( "\n", "\n    " )}" );
			}

			Console.WriteLine( "Type Timings:" );
			foreach ( var pair in result.TypeTimings.OrderByDescending( x => x.Value.Milliseconds ) )
			{
				Console.WriteLine( $"  {pair.Key}:" );
				Console.WriteLine( $"    Instances = {pair.Value.Instances}" );
				Console.WriteLine( $"    TimeSpan = {pair.Value.Milliseconds:F2}ms" );
				Console.WriteLine( "    Roots:" );

				foreach ( var rootPair in pair.Value.Roots.OrderByDescending( x => x.Value.Milliseconds ) )
				{
					Console.WriteLine( $"      {rootPair.Key}:" );
					Console.WriteLine( $"        Instances = {rootPair.Value.Instances}" );
					Console.WriteLine( $"        TimeSpan = {rootPair.Value.Milliseconds:F2}ms" );
				}
			}

			Console.WriteLine( "Processor Timings:" );
			foreach ( var pair in result.ProcessorTimings.OrderByDescending( x => x.Value.Milliseconds ) )
			{
				Console.WriteLine( $"  {pair.Key}:" );
				Console.WriteLine( $"    Instances = {pair.Value.Instances}" );
				Console.WriteLine( $"    TimeSpan = {pair.Value.Milliseconds:F2}ms" );
				Console.WriteLine( "    Roots:" );

				foreach ( var rootPair in pair.Value.Roots.OrderByDescending( x => x.Value.Milliseconds ) )
				{
					Console.WriteLine( $"      {rootPair.Key}:" );
					Console.WriteLine( $"        Instances = {rootPair.Value.Instances}" );
					Console.WriteLine( $"        TimeSpan = {rootPair.Value.Milliseconds:F2}ms" );
				}
			}

			if ( !allowErrors )
			{
				Assert.IsFalse( result.HasErrors );
			}

			return result;
		}

		private TypeLibrary _oldTypeLibrary;
		private NodeLibrary _oldNodeLibrary;

		public TypeLibrary TypeLibrary => Game.TypeLibrary;
		public NodeLibrary Nodes => Game.NodeLibrary;

		/// <summary>
		/// Reflecting over Sandbox.Engine takes most of a second, so build the libraries once and
		/// share them. Hotload doesn't need to walk them.
		/// </summary>
		[SkipHotload]
		private static readonly Lazy<(TypeLibrary Types, NodeLibrary Nodes)> _libraries = new( () =>
		{
			var typeLibrary = new TypeLibrary();
			typeLibrary.AddAssembly( typeof( LogNodes ).Assembly, false );

			var nodeLibrary = new NodeLibrary( new TypeLoader( () => typeLibrary ), new GraphLoader() );
			var result = nodeLibrary.AddAssembly( typeof( LogNodes ).Assembly );

			foreach ( var (method, e) in result.Errors )
			{
				Debug.WriteLine( $"{method}: {e}" );
			}

			Assert.IsFalse( result.AlreadyAdded );
			Assert.AreEqual( 0, result.Errors.Count );

			return (typeLibrary, nodeLibrary);
		} );

		[TestInitialize]
		public void Initialize()
		{
			_oldTypeLibrary = Game.TypeLibrary;
			_oldNodeLibrary = Nodes;

			ResetStaticFields<Before.ResetAttribute>();
			ResetStaticFields<After.ResetAttribute>();
			ResetStaticFields<ResetAttribute>();

			(Game.TypeLibrary, Game.NodeLibrary) = _libraries.Value;
		}

		[TestCleanup]
		public void Cleanup()
		{
			Game.TypeLibrary = _oldTypeLibrary;
			Game.NodeLibrary = _oldNodeLibrary;
		}
	}
}
