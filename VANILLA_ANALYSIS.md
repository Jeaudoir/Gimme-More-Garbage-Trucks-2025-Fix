# Why Vanilla Garbage Truck AI is Broken: A Technical Analysis

**For programmers who want the gory details**

---

## TL;DR

The vanilla `GarbageTruckAI` class randomly collects garbage while driving (filling up before reaching targets), blindly accepts whatever assignments the matchmaking system gives it, gives up after 20 ticks of waiting, has no concept of emergencies, and has zero coordination between facilities.

---

## Stuff We Noticed...

### 1. Opportunistic Pickup Chaos: TryCollectGarbage() (Lines 548-612)

Yes, the method is named "try to collect garbage!"

```csharp
private void TryCollectGarbage(ushort vehicleID, ref Vehicle vehicleData, 
    ref Vehicle.Frame frameData)
{
    // ... position calculation ...
    Vector3 position = frameData.m_position;
    float num = position.x - 32f;
    float num2 = position.z - 32f;
    float num3 = position.x + 32f;
    float num4 = position.z + 32f;
    
    // Draw a 64x64 box around the truck and check every building in it
    for (int i = num6; i <= num8; i++)
    {
        for (int j = num5; j <= num7; j++)
        {
            // Try to collect from buildings within 32 units
            // ...
        }
    }
}
```

**What this does:** While driving to its assigned target, the truck draws a 64×64 unit box around itself every frame and tries to collect garbage from any building within 32 units.

**Problems:**
- Purely luck-based - depends on which buildings the truck happens to pass
- No prioritization - can't distinguish "needs pickup soon" from "CRITICAL EMERGENCY"
- No coordination - multiple trucks can pass the same building or miss it entirely
- **Fills up randomly** - truck might collect enough garbage before reaching its actual target, then **abandons its mission**:

```csharp
// Lines 476-479
if ((int)vehicleData.m_transferSize >= this.m_cargoCapacity && (vehicleData.m_flags & Vehicle.Flags.GoingBack) == (Vehicle.Flags)0 && vehicleData.m_targetBuilding != 0)
{
    this.SetTarget(vehicleID, ref vehicleData, 0);  // 0 = go home
}
```

When the truck fills up (even from random opportunistic pickups), it sets target to `0` and heads home. The building it was originally assigned to help? Still full of garbage.

**Mission failed successfully!**

---

### 2. SetTarget(): supposedly "AI"; do you see "intelligence" here? (Lines 196-297)

The actual target assignment method is remarkably simple:

```csharp
public virtual void SetTarget(ushort vehicleID, ref Vehicle data, ushort targetBuilding)
{
    // ... (lines 198-211: if same target, try pathfinding)
    
    else  // Different target
    {
        this.RemoveTarget(vehicleID, ref data);
        data.m_targetBuilding = targetBuilding;  // Just accept whatever was given
        data.m_flags &= ~Vehicle.Flags.WaitingTarget;
        data.m_waitCounter = 0;
        
        // ... (lines 218-290: add to new building, handle transfer offers)
        
        // Try pathfinding - if it fails, just give up entirely
        if (!this.StartPathFind(vehicleID, ref data))
        {
            data.Unspawn(vehicleID);  // Truck disappears from the game
        }
    }
}
```

**What's happening here?**

This method blindly accepts whatever building ID it's given and immediately tries to path to it. No validation, no intelligence, no fallback plan.

**No consideration for:**
- **Whether the target still needs service** - Building might have been serviced by another truck already
- **Whether another truck is closer** - Could be wasting fuel driving across the map while a closer truck sits idle
- **Whether pathfinding will fail** - No retry logic; if path fails, truck just vanishes (line 294)
- **District restrictions** - Will happily drive to District 9 even if truck belongs to District 1
- **Priority levels** - Can't distinguish emergency from routine pickup
- **Historical failures** - Will retry the same unreachable building over and over

Compare this to intelligent dispatching where you'd:
1. Validate the target is still valid
2. Check if you're the best truck for the job
3. Have a backup plan if pathfinding fails
4. Learn from previous failures
5. Respect operational boundaries (districts)
6. Prioritize based on urgency

Vanilla does **none of this**. It just blindly accepts whatever TransferManager sends and hopes for the best.

---

### 3. Completely Passive Dispatch (Lines 317-330)

```csharp
public override void StartTransfer(ushort vehicleID, ref Vehicle data, 
    TransferManager.TransferReason material, TransferManager.TransferOffer offer)
{
    if (material == (TransferManager.TransferReason)data.m_transferType)
    {
        if ((data.m_flags & Vehicle.Flags.WaitingTarget) != (Vehicle.Flags)0)
        {
            this.SetTarget(vehicleID, ref data, offer.Building);
        }
    }
}
```

**This is the ONLY way trucks get assigned targets.**

Trucks sit around with `WaitingTarget` flag set, hoping `TransferManager` will call this method. There is **zero** proactive scanning, zero intelligence, zero awareness of what's happening in the city.

**TransferManager** is a city-wide resource matching system that pairs "incoming offers" (buildings needing garbage pickup) with "outgoing offers" (idle trucks). Its matching algorithm is simplistic: closest offer to closest truck, with no regard for:
- Emergency priority
- District boundaries  
- Route reachability
- Whether 5 other trucks are already heading there
- Overall efficiency

**Result:** Trucks wait passively for bad assignments.

---

### 4. The 20-Tick Surrender (Lines 126-139)

```csharp
public override void SimulationStep(ushort vehicleID, ref Vehicle data, Vector3 physicsLodRefPos)
{
    if ((data.m_flags & Vehicle.Flags.WaitingTarget) != (Vehicle.Flags)0 && 
        (data.m_waitCounter += 1) > 20)
    {
        this.RemoveOffers(vehicleID, ref data);
        data.m_flags &= ~Vehicle.Flags.WaitingTarget;
        data.m_flags |= Vehicle.Flags.GoingBack;
        data.m_waitCounter = 0;
        if (!this.StartPathFind(vehicleID, ref data))
        {
            data.Unspawn(vehicleID);
        }
    }
    base.SimulationStep(vehicleID, ref data, physicsLodRefPos);
}
```

**Translation:** If a truck waits more than 20 simulation ticks for TransferManager to assign it, it gives up and goes home.

No retry logic. No alternative target search. Just surrender.

**Worse:** If pathfinding fails on the way home, the truck **despawns entirely** (line 136).

---

### 5. Zero Emergency Response

Vanilla has **no concept** of:
- Red flag emergencies (major garbage problems)
- Yellow flag warnings  
- Garbage accumulation thresholds
- Priority-based targeting
- Swarm dispatching

The opportunistic collection logic (lines 592-612) just checks:
1. Is the truck within 32 units?
2. Does the truck have cargo space?

That's it. No priority checking. No problem detection. A building with flies swarming and citizens leaving gets the same consideration as a building at 10% capacity.

---

### 6. No Coordination Between Facilities

Each landfill operates in complete isolation with zero awareness of:
- What other landfills are doing
- Which buildings already have trucks assigned
- Overlapping service areas  
- Workload distribution

**Result:** Multiple trucks can pile onto the same building while others sit ignored.

---

## The Verdict

The vanilla `GarbageTruckAI` class is a masterclass in passive, hope-based programming:
1. Hope the truck doesn't fill up on random pickups
2. Hope TransferManager assigns something useful
3. Hope the route is reachable
4. Hope it doesn't wait longer than 20 ticks
5. Hope the building still needs service when it arrives

**This mod replaces hope with actual intelligence.**

---

## For The Curious

Want to see the vanilla code yourself? The `GarbageTruckAI` class ships with Cities: Skylines in:
```
\SteamLibrary\steamapps\common\Cities_Skylines\Cities_Data\Managed\Assembly-CSharp.dll
```

Decompile with ILSpy, dnSpy, or similar tools. Search for `GarbageTruckAI`.

Prepare to be amazed.

**Note:** Line numbers referenced in this document are approximate, based on decompiled code via dnSpy. Your mileage may vary depending on game version and decompiler used.
