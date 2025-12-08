using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using BepuUtilities;
using System.Numerics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Engine.Emulation.Physics;

/// <summary>
/// Wrapper around BepuPhysics Simulation. Provides methods used by the NativeAOT exports.
/// </summary>
public class BepuPhysicsWorld : IDisposable
{
    private readonly BufferPool _bufferPool;
    private readonly SimpleThreadDispatcher _threadDispatcher;
    public Simulation Simulation { get; private set; }
    private StaticHandle _referenceBody;

    public Vector3 Gravity { get; set; } = new Vector3(0, 0, -800);
    
    // Collision rules storage
    private Dictionary<string, CollisionRuleResult> _defaultRules = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<(string, string), CollisionRuleResult> _pairRules = new();
    
    // Static mapping from Simulation to BepuPhysicsWorld for callbacks
    private static readonly ConcurrentDictionary<Simulation, BepuPhysicsWorld> _simulationToWorld = new();
    
    // Static mapping from handles to world (for callbacks to find world from collidables)
    // Using ConcurrentDictionary for thread-safe access from native callbacks
    private static readonly ConcurrentDictionary<BodyHandle, BepuPhysicsWorld> _bodyHandleToWorld = new();
    private static readonly ConcurrentDictionary<StaticHandle, BepuPhysicsWorld> _staticHandleToWorld = new();
    
    // Mapping from collidables to their tags
    // BodyHandle -> HashSet<string> tags
    private readonly Dictionary<BodyHandle, HashSet<string>> _bodyTags = new();
    // StaticHandle -> HashSet<string> tags
    private readonly Dictionary<StaticHandle, HashSet<string>> _staticTags = new();
    
    /// <summary>
    /// Register this world's simulation for callback access.
    /// </summary>
    internal void RegisterForCallbacks()
    {
        _simulationToWorld[Simulation] = this;
    }
    
    /// <summary>
    /// Unregister this world's simulation.
    /// </summary>
    internal void UnregisterForCallbacks()
    {
        _simulationToWorld.TryRemove(Simulation, out _);
    }
    
    /// <summary>
    /// Get BepuPhysicsWorld from Simulation (for use in callbacks).
    /// </summary>
    public static BepuPhysicsWorld? GetWorldFromSimulation(Simulation simulation)
    {
        _simulationToWorld.TryGetValue(simulation, out var world);
        return world;
    }
    
    /// <summary>
    /// Get BepuPhysicsWorld from a collidable reference (for use in callbacks).
    /// Thread-safe, but avoids locks in hot path by using TryGetValue directly.
    /// </summary>
    public static BepuPhysicsWorld? GetWorldFromCollidable(CollidableReference collidable)
    {
        // Try without lock first (most common case)
        if (collidable.Mobility == CollidableMobility.Dynamic || collidable.Mobility == CollidableMobility.Kinematic)
        {
            var bodyHandle = collidable.BodyHandle;
            // Dictionary.TryGetValue is thread-safe for reads in .NET
            if (_bodyHandleToWorld.TryGetValue(bodyHandle, out var world))
                return world;
        }
        else if (collidable.Mobility == CollidableMobility.Static)
        {
            var staticHandle = collidable.StaticHandle;
            if (_staticHandleToWorld.TryGetValue(staticHandle, out var world))
                return world;
        }
        return null;
    }
    
    /// <summary>
    /// Collision rule result enum matching Sandbox.Physics.CollisionRules.Result
    /// </summary>
    public enum CollisionRuleResult
    {
        Unset = 0,
        Collide = 1,
        Trigger = 2,
        Ignore = 3
    }

    public BepuPhysicsWorld()
    {
        _bufferPool = new BufferPool();
        // Use a single thread dispatcher to avoid NativeAOT issues with multithreaded callbacks
        _threadDispatcher = new SimpleThreadDispatcher(1);
        
        // Create callbacks as local variables to ensure they're not collected
        // This is important for NativeAOT - the structs must remain valid
        var narrowPhaseCallbacks = new NarrowPhaseCallbacks();
        var poseIntegratorCallbacks = new PoseIntegratorCallbacks(Gravity);
        
        // Initialise simulation with default callbacks.
        // Note: BepuPhysics will copy the structs, so they must be unmanaged
        Simulation = Simulation.Create(
            _bufferPool,
            narrowPhaseCallbacks,
            poseIntegratorCallbacks,
            // Single threaded solve to minimize complexity in callbacks
            new SolveDescription(1, 1)
        );
        
        // Register this world for callback access
        RegisterForCallbacks();
        
        // Crée un body statique de référence
        var boxIndex = Simulation.Shapes.Add(new Box(1, 1, 1));
        var staticDesc = new StaticDescription(
            new RigidPose(new Vector3(0, 0, 0)),
            boxIndex
        );
        _referenceBody = Simulation.Statics.Add(staticDesc);
        RegisterStaticHandle(_referenceBody);
    }

    /// <summary>
    /// Adds a dynamic body with a simple box shape. In a full implementation this would be more flexible.
    /// </summary>
    public BodyHandle AddBody()
    {
        var bodyDesc = BodyDescription.CreateDynamic(
            new RigidPose(Vector3.Zero),
            new BodyInertia { InverseMass = 1f },
            new CollidableDescription(Simulation.Shapes.Add(new Box(1, 1, 1)), 0.1f),
            new BodyActivityDescription(0.01f)
        );
        var bodyHandle = Simulation.Bodies.Add(bodyDesc);
        
        // Register body handle for callback access
        _bodyHandleToWorld[bodyHandle] = this;
        
        return bodyHandle;
    }
    
    /// <summary>
    /// Remove a body from the world mapping.
    /// </summary>
    internal void RemoveBodyMapping(BodyHandle bodyHandle)
    {
        _bodyHandleToWorld.TryRemove(bodyHandle, out _);
    }
    
    /// <summary>
    /// Register a static handle for callback access.
    /// </summary>
    internal void RegisterStaticHandle(StaticHandle staticHandle)
    {
        _staticHandleToWorld[staticHandle] = this;
    }

    public StaticHandle GetReferenceBody() => _referenceBody;

    public void Step(float dt)
    {
        Simulation?.Timestep(dt, _threadDispatcher);
    }
    
    /// <summary>
    /// Set collision rules from JSON string.
    /// </summary>
    public void SetCollisionRulesFromJson(string jsonString)
    {
        if (string.IsNullOrEmpty(jsonString))
        {
            _defaultRules.Clear();
            _pairRules.Clear();
            return;
        }
        
        try
        {
            var jsonDoc = JsonDocument.Parse(jsonString);
            var root = jsonDoc.RootElement;
            
            // Parse Defaults
            _defaultRules.Clear();
            if (root.TryGetProperty("Defaults", out var defaultsElement))
            {
                foreach (var prop in defaultsElement.EnumerateObject())
                {
                    var result = ParseCollisionResult(prop.Value.GetString() ?? "Collide");
                    _defaultRules[prop.Name] = result;
                }
            }
            
            // Parse Pairs
            _pairRules.Clear();
            if (root.TryGetProperty("Pairs", out var pairsElement))
            {
                foreach (var pairElement in pairsElement.EnumerateArray())
                {
                    if (pairElement.TryGetProperty("a", out var aElement) &&
                        pairElement.TryGetProperty("b", out var bElement) &&
                        pairElement.TryGetProperty("r", out var rElement))
                    {
                        string tagA = aElement.GetString() ?? "";
                        string tagB = bElement.GetString() ?? "";
                        var result = ParseCollisionResult(rElement.GetString() ?? "Collide");
                        
                        // Store both (a,b) and (b,a) for case-insensitive lookup
                        var key1 = (tagA, tagB);
                        var key2 = (tagB, tagA);
                        _pairRules[key1] = result;
                        _pairRules[key2] = result;
                    }
                }
            }
            
            Console.WriteLine($"[NativeAOT] BepuPhysicsWorld.SetCollisionRulesFromJson: loaded {_defaultRules.Count} defaults, {_pairRules.Count / 2} pairs");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] BepuPhysicsWorld.SetCollisionRulesFromJson: error parsing JSON: {ex.Message}");
        }
    }
    
    private static CollisionRuleResult ParseCollisionResult(string resultStr)
    {
        return resultStr.ToLowerInvariant() switch
        {
            "collide" => CollisionRuleResult.Collide,
            "trigger" => CollisionRuleResult.Trigger,
            "ignore" => CollisionRuleResult.Ignore,
            _ => CollisionRuleResult.Collide
        };
    }
    
    /// <summary>
    /// Get collision rule for a pair of tags.
    /// Returns the rule result, or Collide as default.
    /// </summary>
    public CollisionRuleResult GetCollisionRule(string tagA, string tagB)
    {
        // Check pair rules first
        var key1 = (tagA, tagB);
        var key2 = (tagB, tagA);
        if (_pairRules.TryGetValue(key1, out var pairResult))
            return pairResult;
        if (_pairRules.TryGetValue(key2, out pairResult))
            return pairResult;
        
        // Check default rules
        var defaultA = _defaultRules.GetValueOrDefault(tagA, CollisionRuleResult.Collide);
        var defaultB = _defaultRules.GetValueOrDefault(tagB, CollisionRuleResult.Collide);
        
        // Return the "least colliding" (highest enum value)
        return defaultA > defaultB ? defaultA : defaultB;
    }
    
    /// <summary>
    /// Get tags for a collidable reference.
    /// </summary>
    public HashSet<string> GetTagsForCollidable(CollidableReference collidable)
    {
        if (collidable.Mobility == CollidableMobility.Dynamic || collidable.Mobility == CollidableMobility.Kinematic)
        {
            var bodyHandle = collidable.BodyHandle;
            if (_bodyTags.TryGetValue(bodyHandle, out var tags))
                return tags;
        }
        else if (collidable.Mobility == CollidableMobility.Static)
        {
            var staticHandle = collidable.StaticHandle;
            if (_staticTags.TryGetValue(staticHandle, out var tags))
                return tags;
        }
        
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Set tags for a body (called when tags are added to shapes).
    /// </summary>
    public void SetBodyTags(BodyHandle bodyHandle, HashSet<string> tags)
    {
        _bodyTags[bodyHandle] = tags;
    }
    
    /// <summary>
    /// Set tags for a static (called when tags are added to static shapes).
    /// </summary>
    public void SetStaticTags(StaticHandle staticHandle, HashSet<string> tags)
    {
        _staticTags[staticHandle] = tags;
    }
    
    /// <summary>
    /// Get collision rule result for two collidables.
    /// Checks all tag pairs and returns the "least colliding" result.
    /// </summary>
    public CollisionRuleResult GetCollisionRuleForCollidables(CollidableReference a, CollidableReference b)
    {
        var tagsA = GetTagsForCollidable(a);
        var tagsB = GetTagsForCollidable(b);
        
        // If no tags, default to Collide
        if (tagsA.Count == 0 && tagsB.Count == 0)
            return CollisionRuleResult.Collide;
        
        // If one has no tags, use defaults
        if (tagsA.Count == 0)
        {
            var result = CollisionRuleResult.Collide;
            foreach (var tagB in tagsB)
            {
                var rule = GetCollisionRule("", tagB);
                if (rule > result) result = rule;
            }
            return result;
        }
        
        if (tagsB.Count == 0)
        {
            var result = CollisionRuleResult.Collide;
            foreach (var tagA in tagsA)
            {
                var rule = GetCollisionRule(tagA, "");
                if (rule > result) result = rule;
            }
            return result;
        }
        
        // Check all tag pairs and return the "least colliding" (highest enum value)
        var finalResult = CollisionRuleResult.Collide;
        foreach (var tagA in tagsA)
        {
            foreach (var tagB in tagsB)
            {
                var rule = GetCollisionRule(tagA, tagB);
                if (rule > finalResult) finalResult = rule;
            }
        }
        
        return finalResult;
    }

    public void Dispose()
    {
        UnregisterForCallbacks();
        
        // Clean up handle mappings
        var bodiesToRemove = _bodyHandleToWorld.Where(kvp => kvp.Value == this).Select(kvp => kvp.Key).ToList();
        foreach (var handle in bodiesToRemove)
        {
            _bodyHandleToWorld.TryRemove(handle, out _);
        }
        
        var staticsToRemove = _staticHandleToWorld.Where(kvp => kvp.Value == this).Select(kvp => kvp.Key).ToList();
        foreach (var handle in staticsToRemove)
        {
            _staticHandleToWorld.TryRemove(handle, out _);
        }
        
        Simulation?.Dispose();
        _bufferPool?.Clear();
        _threadDispatcher?.Dispose();
    }
}
