# Gimme More Garbage Trucks! (2025 Fix)

**Intelligent garbage truck AI for Cities: Skylines**

Replaces the vanilla dispatch system with smart targeting, emergency response, and district-aware routing.

---

## The Problem

The game's default garbage truck AI is fundamentally broken.

See a detailed teardown [here](VANILLA_ANALYSIS.md).

Summary:
- **No emergency awareness** - Buildings with critical warnings get the same priority as everything else
- **Random opportunistic pickups** - Trucks fill up on whatever they drive by, and go home without reaching assigned targets
- **No retry logic** - Can't reach a building? Give up and return to depot
- **Wasted effort** - Multiple trucks go to the same building while others are ignored
- **District ignorance** - Trucks cross the entire map ignoring local problems
- **Passive dispatch** - Landfills wait for assignments from TransferManager (the game's city-wide supply and demand matching system) instead of actively looking for work

TransferManager is not particularly intelligent: it matches buildings to trucks with no regard for efficiency, emergency priority, district boundaries -- or whether the route is actually reachable.

**Result:** Garbage piles up despite maxed budgets, and your trucks either sit idle or get sent on bad assignments.

---

## What This Mod Does

### Proactive Scanning
Facilities actively scan the city for buildings that need service instead of waiting for TransferManager to assign targets

### Emergency Swarms
Detects warning flags and dispatches swarms of trucks to crisis buildings. Multiple facilities can swarm the same emergency - this is intentional.

### Smart Targeting
Trucks choose better targets based on distance, garbage levels, and warning flags; pathfinding failures result in fresh assignments instead of going home; claim system prevents truck overlap during normal operations.

### District Management
Optional district restrictions keep trucks working locally; facilities outside districts can service citywide

### Low Cargo Recall
Monitors trucks on long routes with poor collection rates; recalls underperformers to try different targets

### Facility Capacity Toggle
**Default (Respect capacity)** For simulationists: only dispatches trucks when incinerators have space - follows vanilla processing limits, but with smarter targeting.

**Aggressive mode** For casual gamers: dispatches trucks even when incinerators are full - does not change the vanilla behavior where returning trucks despawn and make garbage disappear.

---

## Configuration

Tons of settings with OCD levels of customization. See [ModConfiguration.cs](GimmeMoreGarbageTrucks2025/ModConfiguration.cs) for the complete list, or access via **Options → Mod Settings** in-game.

---

## Installation

### Via Steam Workshop (recommended)
1. Subscribe to this mod
2. Subscribe to [Harmony 2.2.2+](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402) (only requires installation, not activation)
3. Enable in Content Manager → Mods

### Manual
1. Download from [Releases](https://github.com/Jeaudoir/Gimme-More-Garbage-Trucks-2025-Fix/releases)
2. Extract to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\`
3. Install Harmony 2.x from Workshop

---

## Compatibility

Safe for existing saves - can be enabled/disabled/installed/removed without breaking anything.

Only uses Harmony to patch `GarbageTruckAI.SetTarget()` (bypassing TransferManager entirely) - the rest is independent logic. Should work with most mods. Untested with DLCs.

Likely conflicts with Transfer Manager CE (which I've never used because I find it so incredibly confusing).

---

## Known Issues

**To-do note** in Landfill.cs line 609: the GetClosestTarget method could use refactoring for readability, but it works and I have to stop tweaking!

**Monster conditional** in Landfill.cs line 714: A 200+ character conditional that works but is hard for humans to read. (The computer doesn't care.)

---

## Code Structure

Each file has comprehensive header comments explaining purpose and architecture. Start with:
- **Dispatcher.cs** - Main orchestrator
- **Landfill.cs** - Per-facility management
- **CustomGarbageTruckAI.cs** - Replaces vanilla SetTarget logic
- **Constants.cs** - All magic numbers centralized here

---

## Compiling Your Own

**Requirements:** .NET SDK or Visual Studio 2019+ with C#

```bash
git clone https://github.com/Jeaudoir/Gimme-More-Garbage-Trucks-2025-Fix.git
cd Gimme-More-Garbage-Trucks-2025-Fix
# Update reference paths in .csproj to match your Cities: Skylines installation
dotnet build -c Release
```

The compiled DLL will auto-copy to your mods folder (location is configured in .csproj PostBuild event).

---

## Contributing

Bug reports are welcome, but my inbox is already overflowing and response times will be slow.

Pull requests accepted case-by-case. Open an issue first to discuss major changes.

**Maintenance expectations:** Gaming is an occasional hobby for me. This mod does everything I want it to. If you want to extend it, the torch is yours - fork away!

---

## Credits

Based on the Enhanced Garbage Truck AI mods by:
- [Akira Ishizaki](https://github.com/akira-ishizaki/CS-EnhancedGarbageTruckAI)
- [Aris Lancrescent](https://github.com/arislancrescent/CS-EnhancedGarbageTruckAI)

**Why a rewrite?** Original mods used Skylines Overwatch (unmaintained). This version uses Harmony 2.x and adds a shartload of insane features.

---

## AI Assistance Disclosure

Developed with assistance from Claude AI. Code was iteratively designed, reviewed, tested, and debugged across multiple sessions. All design decisions and testing were human-driven.

Audit the code yourself, it's all here! If using AI to review, verify that it's seeing all complete files (some get truncated).

---

## License

MIT License - see [LICENSE](LICENSE)

---

## Support

- **GitHub Issues:** [Report bugs](https://github.com/Jeaudoir/Gimme-More-Garbage-Trucks-2025-Fix/issues)
- **Steam Workshop:** [Leave a comment](https://steamcommunity.com/sharedfiles/filedetails/?id=YOUR_WORKSHOP_ID)
- **Creator:** [Bilbo Fraggins](https://steamcommunity.com/id/xd00d/myworkshopfiles/?appid=255710)

Please include game version, other mods, and save file if reporting issues.

---

**Enjoy cleaner cities!**
