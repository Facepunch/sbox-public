# s&box Anticheat Sistem - Detaylı İnceleme & Öneriler

**İnceleme Tarihi:** 2025-04-02  
**Sistem:** Valorant-benzeri Server-Side Visibility FOW (Fog of War)

---

## 📊 Genel Değerlendirme

| Kriter | Puan | Açıklama |
|--------|------|----------|
| **Mimarisi** | 9/10 | Layered pipeline çok iyi tasarlanmış |
| **Networking** | 6/10 | Bazı desync riskleri ve race condition sorunları |
| **Performance** | 8/10 | Spatial grid ve caching iyi ama 9-point raycast pahalı |
| **Security** | 7/10 | Wallhack önler ama exploit vektörleri var |
| **Kodları Kalitesi** | 9/10 | Clean, well-documented, testable |
| **Production-Ready** | 6/10 | Kritik fix'ler gerekli |

---

## 🔴 KRITIK SORUNLAR (Hemen Fix Gerekir)

### 1. **Network Race Condition - Cache Invalidation**

```csharp
❌ PROBLEM (VisibilityManager.cs line 207-286)

if ( _cache.TryGetValue( pairKey, out var cached ) )
{
    var tickAge = _currentTick - cached.TickStamp;
    if ( tickAge < VisibilityConfig.CacheValidTicks )
    {
        return cached.IsVisible;  // STALE DATA!
    }
}
```

**Risk Senaryosu:**
- Oyuncu Teleport ile duvarın diğer tarafına gidiyor (LoS artık false)
- Cache 3 tick boyunca stale kalıyor
- Client 3 tick boyunca invisible oyuncuyu görebiliyor
- **ESP/Wallhack Açığı**

**Çözüm:**
```csharp
// ✅ FIXED: Position-aware cache invalidation
private bool IsCacheValid( CachedVisibility cached, Vector3 targetPos, Vector3 lastCachedPos )
{
    var tickAge = _currentTick - cached.TickStamp;
    if ( tickAge >= VisibilityConfig.CacheValidTicks )
        return false;
    
    // Invalidate if target moved too far (teleport detection)
    var posDelta = targetPos.Distance( lastCachedPos );
    if ( posDelta > VisibilityConfig.MaxTeleportDistance )
        return false;
    
    return true;
}
```

---

### 2. **Null Ping - Prediction Crash**

```csharp
❌ PROBLEM (VisibilityManager.cs line 267)

var pingMs = observer.Ping;  // Can be null/undefined!

if ( Prediction.ShouldPrefetch( Scene, observerPos, pingMs, ... ) )
```

**Risk:** Düşük ping sunucuları bozabilir. NaN değerleri ile prediction hatalı olur.

**Çözüm:**
```csharp
✅ FIXED:
var pingMs = (observer.Ping ?? 50f);  // Default fallback
var safePingMs = MathF.Max( 10f, MathF.Min( 500f, pingMs ) );  // Clamp
```

---

### 3. **Sound Visibility Network Desync**

```csharp
❌ PROBLEM (PredictiveVisibility.cs)

public void RegisterNoise( GameObject source )
{
    _lastNoiseTime[source.Id] = RealTime.Now;  // LOCAL SERVER TIME!
}
```

**Risk:** 
- Client A oyuncu atış sesi duyuyor 
- Server başka görüşte LoS fail vermişe bile transmit ediyor
- Client'ta entity "görünsem de" sesini duyamıyor → desync

**Çözüm: Sound events'i network mesajlarla gönder:**
```csharp
✅ FIXED: Network-synced sound visibility

[Flags]
public enum SoundEventType : byte
{
    Footstep = 1,
    Gunshot = 2,
    Explosion = 4,
    LoudAction = 8
}

// Server broadcasts to clients hearing range
[Broadcast]
public void PlaySoundEvent( SoundEventType type, Vector3 position, float hearingRange )
{
    var manager = Scene.GetSystem<VisibilityManager>();
    manager?.Prediction.RegisterNoise( GameObject.Id, type, hearingRange );
}
```

---

### 4. **Thread-Unsafe Dictionary Access**

```csharp
❌ PROBLEM (VisibilityManager.cs)

// No locks, but multiple threads can call IsVisibleTo()
private readonly Dictionary<(Guid, Guid), CachedVisibility> _cache = new( 512 );

// In IsVisibleTo() - RACE CONDITION
if ( _cache.TryGetValue( pairKey, out var cached ) )
    ...
_cache[pairKey] = ...  // Can throw from concurrent modification
```

**Bu sadece multiplayer server'da sorun yaratır.**

**Çözüm:**
```csharp
✅ FIXED: Use ConcurrentDictionary

private readonly ConcurrentDictionary<(Guid, Guid), CachedVisibility> _cache 
    = new( 512, Environment.ProcessorCount * 2 );
```

---

## 🟠 UYARI-LEVEL SORUNLAR

### 5. **Owner Bypass Kontrol Eksik**

```csharp
⚠️ PARTIAL ISSUE (VisibilityComponent.cs line 109)

if ( GameObject.Network.OwnerId == connection.Id )
    return true;  // Owner always sees own stuff - DOĞRU
```

**Ama:** Eğer owner false return etseyse cevabını kimse kontrol etmemiş.

**Önerilen Fix:**
```csharp
// ✅ AUDIT: Owner bypass always force-visible
if ( connection.Id == GameObject.Network.OwnerId )
{
    // Log for audit trail
    if ( VisibilityConfig.AuditLoggingEnabled )
        LogVisibilityAudit( "OWNER_BYPASS", connection, GameObject );
    return true;  // Non-negotiable
}
```

---

### 6. **Grace Period Exploit**

```csharp
⚠️ ISSUE (VisibilityManager.cs line 279-283)

if ( cached.IsVisible && (RealTime.Now - cached.LastVisibleAt) 
     < VisibilityConfig.CullGracePeriod )
{
    return true;  // Still transmitting during grace period
}
```

**Problem:** Hacker deliberately grace period'u exploit edebilir:
- Entity görünüp kaybolduğunda grace period başlıyor
- Hacker 0.5s boyunca transmit edildiğini biliyor
- Şimdiye kadar hareket için 0.5s buffer = predictable position

**Çözüm:**
```csharp
// ✅ FIXED: Randomized grace period + position uncertainty
[ConVar( "sv_vis_grace_randomness" )]
public static float CullGracePeriodRandomness { get; set; } = 0.2f;

var randomizedGrace = VisibilityConfig.CullGracePeriod 
    + Random.Shared.NextSingle() * CullGracePeriodRandomness;

if ( (RealTime.Now - cached.LastVisibleAt) < randomizedGrace )
{
    return true;
}
```

---

### 7. **FindObserverGameObject Linear Search O(n) Perf Issue**

```csharp
⚠️ PERF (VisibilityManager.cs line 310-329)

private GameObject FindObserverGameObject( Connection observer )
{
    foreach ( var entity in _trackedEntities )  // O(n) HER CALL
    {
        if ( entity.Network.OwnerId == observer.Id )
            return entity;
    }
}
```

**Problem:** Observer bazında çağrılırsa 100 oyuncu = 10,000 O(n) search per frame

**Çözüm:**
```csharp
// ✅ FIXED: Cache observer→gameobject mapping

private readonly Dictionary<Guid, GameObject> _observerGameObjects = new();

public void Register( GameObject go )
{
    ...
    if ( go.Network.Active )
        _observerGameObjects[go.Network.OwnerId] = go;
}

private GameObject FindObserverGameObject( Connection observer )
{
    _observerGameObjects.TryGetValue( observer.Id, out var go );
    return go;
}
```

---

### 8. **Spectator Bypass Tag-based (Spoofable)**

```csharp
⚠️ SECURITY (VisibilityManager.cs line 331)

return go.Tags.Has( "spectator" );
```

**Risk:** Eğer game code'u tags'ı properly set etmezse hacker spectator olabilir

**Önerilen Fix:**
```csharp
// ✅ AUDIT: Explicit spectator state verification
private bool IsSpectator( Connection observer )
{
    var go = FindObserverGameObject( observer );
    if ( go is null ) return false;
    
    // Don't just check tags - verify spectator state
    var spectatorComponent = go.Components.Get<SpectatorController>();
    if ( spectatorComponent?.IsSpectating == true )
        return true;
    
    // Log suspicious tag claims
    if ( go.Tags.Has( "spectator" ) && spectatorComponent?.IsSpectating != true )
    {
        LogSecurityWarning( $"SPOOFED_SPECTATOR_TAG: {observer}" );
        return false;
    }
    
    return false;
}
```

---

## 🟡 OPTIMIZATION SORUNLAR

### 9. **9-Point Raycast CPU Cost**

```
Current: 100 players → 10,000 entities → ~90,000 raycasts/sec (worst case)
At 64 tick: 1,406 raycasts/tick
```

**Optimization önerileri:**

```csharp
// ✅ SOLUTION 1: Adaptive test point count based on distance
public static int GetTestPointCountForDistance( float distSq )
{
    if ( distSq > VisibilityConfig.MaxVisibilityRangeSq * 0.5f )
        return 3;  // Far away → only 3 corners (8 corners + center)
    if ( distSq > VisibilityConfig.MaxVisibilityRangeSq * 0.25f )
        return 5;  // Medium → 5 points
    return 9;     // Close → full 9 points
}

// ✅ SOLUTION 2: Early-out optimization already in code (good!)
if ( !VisibilityConfig.DebugEnabled )
    break;  // Exits after first visible point ✓

// ✅ SOLUTION 3: Batch ray queries (Source 2 supports)
// Cast 4-8 rays in parallel instead of sequential
```

---

### 10. **Cache Pruning Strategy Suboptimal**

```csharp
⚠️ ISSUE (VisibilityManager.cs line 387)

private void PruneStaleCache()
{
    var threshold = _currentTick - (VisibilityConfig.CacheValidTicks * 10);
    
    // This allocates List<T> even if nothing to remove!
    List<(Guid, Guid)> toRemove = null;
    foreach ( var (key, value) in _cache )
    {
        if ( value.TickStamp < threshold )
        {
            toRemove ??= new( 32 );
            toRemove.Add( key );
        }
    }
}
```

**Sorun:** 512+ cache entries haftada bir defa temizlenir. LRU daha iyi olur.

**Çözüm:**
```csharp
// ✅ FIXED: LRU eviction
private const int MaxCacheSize = 4096;

private void UpdateCache( (Guid, Guid) key, bool isVisible )
{
    _cache[key] = new CachedVisibility
    {
        IsVisible = isVisible,
        TickStamp = _currentTick,
        LastVisibleAt = ...
    };
    
    // LRU eviction
    if ( _cache.Count > MaxCacheSize )
    {
        var oldest = _cache
            .OrderBy( x => x.Value.TickStamp )
            .First();
        _cache.Remove( oldest.Key );
    }
}
```

---

## 📋 NETWORKING BEST PRACTICES CHECKLIST

- [ ] **Message Integrity**: Sound events signed with HMAC-SHA256?
- [ ] **Replication Gap**: Client'a ne kadar eski data gösterilebilir?
- [ ] **Bandwidth**: 64 oyuncu × visibility updates her tick = ?
- [ ] **Lag Compensation**: Late arrival sound events nasıl handle ediliyor?
- [ ] **Anti-Tampering**: Client'ın visibility result'ı trust ediyormusunuz?

---

## 🛠️ ÖNERİLEN FIX SIRALAMASI

### Priority 1 (Yapılması Gerekli)
1. ✅ Thread-safe dictionary (ConcurrentDictionary)
2. ✅ Ping null-safety
3. ✅ Cache position-aware invalidation
4. ✅ Auditing/logging framework

### Priority 2 (Güvenlik için Önemli)
1. ✅ Spectator state verification
2. ✅ Sound events network sync
3. ✅ Grace period randomization
4. ✅ Owner bypass audit logging

### Priority 3 (Performance)
1. ✅ Observer→GameObject mapping cache
2. ✅ Adaptive test point count
3. ✅ LRU cache eviction
4. ✅ Batch ray queries

---

## 📝 PRODUCTION DEPLOYMENT CHECKLIST

```
Pre-Launch Security Audit
[ ] Anti-cheat logs enabled + monitored?
[ ] Grace period values tuned for your game?
[ ] Sound event bandwidth analyzed?
[ ] Fallback behavior tested (manager=null)?
[ ] Serialization security validated?
[ ] Client-side trust assumptions documented?

Performance Tuning
[ ] Grid cell size optimal for map scale?
[ ] Cache tick validity tested with 64 players?
[ ] CPU spend under 5ms/frame on server?
[ ] Memory footprint < 100MB (tracker + cache)?

Networking
[ ] PVS integration tested?
[ ] Spectator bypass working for replays?
[ ] Sound sync latency < 100ms?
[ ] Rate limiting on visibility queries?
```

---

## 📊 BENCHMARK TAVSİYELERİ

Aşağıdaki senaryoları test edin:

```csharp
// Test Scenario 1: 100 player all visible (max load)
// Expected: < 10ms per tick

// Test Scenario 2: Rapid player pop-in/pop-out
// Check for flickering and cache invalidation

// Test Scenario 3: Network lag spike (500ms delay)
// Verify prediction boundaries hold

// Test Scenario 4: Concurrent spectator joins
// Ensure no race conditions in bypass logic
```

---

## 🎯 ÖZETİN ÖZETİ

| Sorun | Şiddet | Çözüm Zamanı |
|-------|--------|------------|
| Cache race condition | 🔴 Critical | 2 saat |
| Ping null-safety | 🔴 Critical | 30 min |
| Thread safety | 🟠 High | 1 saat |
| Observer cache | 🟡 Medium | 1 saat |
| Sound sync | 🟡 Medium | 3 saat |
| **TOPLAM FIX** | | **8 saat** |

**Yaşayan Sistem mi?** Hayır - 8 saat fix gerekiyor ama çok iyi başlanmışsınız! 👊
