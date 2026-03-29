extern alias After;
extern alias Before;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Sandbox;
using Sandbox.ActionGraphs;
using Sandbox.Internal;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NodeLibrary = Facepunch.ActionGraphs.NodeLibrary;

namespace Hotload
{
	[AttributeUsage( AttributeTargets.Field )]
	public sealed class ResetAttribute : Attribute { }

	public abstract class HotloadTests
	{
		private static void ResetStaticFields<TAttrib>()
			where TAttrib : Attribute
		{
			foreach ( var type in typeof( TAttrib ).Assembly.GetTypes() )
			{
				foreach ( var fieldInfo in type.GetFields( BindingFlags.Static | BindingFlags.Public ) )
				{
					if ( fieldInfo.GetCustomAttribute<TAttrib>() != null )
					{
						fieldInfo.SetValue( null, fieldInfo.FieldType.IsValueType ? Activator.CreateInstance( fieldInfo.FieldType ) : null );
					}
				}
			}
		}

		protected static Sandbox.Hotload CreateHotload()
		{
			Sandbox.Hotload.AssemblyNameFormatter = name => name.Name;

			var hotload = new Sandbox.Hotload { TraceRoots = true, TracePaths = true, IncludeTypeTimings = true, IncludeProcessorTimings = true };

			hotload.WatchAssembly( "Sandbox.Hotload.Test" );
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

		[TestInitialize]
		public void Initialize()
		{
			_oldTypeLibrary = Game.TypeLibrary;
			_oldNodeLibrary = Nodes;

			ResetStaticFields<Before.ResetAttribute>();
			ResetStaticFields<After.ResetAttribute>();
			ResetStaticFields<ResetAttribute>();

			Game.TypeLibrary = new TypeLibrary();
			Game.TypeLibrary.AddAssembly( typeof( LogNodes ).Assembly, false );

			Game.NodeLibrary = new NodeLibrary( new TypeLoader( () => TypeLibrary ), new GraphLoader() );

			var result = Nodes.AddAssembly( typeof( LogNodes ).Assembly );

			foreach ( var (method, e) in result.Errors )
			{
				Debug.WriteLine( $"{method}: {e}" );
			}

			Assert.IsFalse( result.AlreadyAdded );
			Assert.AreEqual( 0, result.Errors.Count );
		}

		[TestCleanup]
		public void Cleanup()
		{
			Game.TypeLibrary = _oldTypeLibrary;
			Game.NodeLibrary = _oldNodeLibrary;
		}
	}
}
