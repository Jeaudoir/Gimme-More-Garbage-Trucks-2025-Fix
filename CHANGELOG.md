# CHANGELOG

## Version 1.1.0 - 2025-10-24

### New Features
- Added "Respect facility capacity" toggle for purists: follow the vanilla rule of NOT dispatching trucks even during emergencies, or allow aggressive dispatch mode that sends trucks even when facilities are full

### Code Quality
- Consolidated scattered magic numbers throughout codebase
- Eliminated magic numbers (replaced hardcoded values with named constants)
- Renamed a number of variables to be more human-friendly
- Removed excessive debug logging (~70 lines of verbose emergency tracking)

### Documentation
- Added comprehensive header comments to all major classes
- Improved inline documentation for complex logic
- Added explanatory comments for safety checks and my goofy workarounds

### Technical
- Replaced minimal Settings.cs (17 lines) with robust Constants.cs (150+ lines)
- All user setting limits now reference centralized constants
- Added 50+ documented constants for distances, angles, pathfinding, time thresholds, and iteration limits

### Files Changed
- **NEW**: Constants.cs
- **REMOVED**: Settings.cs
- **MODIFIED**: All core .cs files updated to use Constants system


## Version 1.1.0 - 2025-10-13

Initial release.