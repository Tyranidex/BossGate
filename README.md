# BossGate

BossGate is a lightweight Valheim mod that lets admins **enable/disable boss summons per world**.  
By default, **all bosses are blocked**, so players can’t progress bosses without admin approval.  
Changes apply instantly (no restart).

## Features
- ✅ **Server-authoritative boss gating** (blocks boss summon attempts)
- ✅ **Simple in-game admin UI** (toggle bosses on/off)
- ✅ **No server restart** required (uses world global keys)
- ✅ **Optional**: block the **Queen door** until the Queen is enabled
- ✅ **Chat / center-screen message** when a summon is blocked
- ✅ Works in **local host** for testing (host is treated as admin)

## How it works (simple)
BossGate blocks boss summons when a boss is disabled.  
Boss availability is saved as **world global keys**, so it persists with the world.

## Installation
You can install manually, but for most people it’s easier to use **r2modman / Thunderstore**.

### Manual install (Client / Singleplayer / Local host)
1. Install **BepInEx 5** for Valheim.
2. Unzip mod folder into:
   - `Valheim/BepInEx/plugins/` 
3. Launch the game once.

### Manual install (Dedicated Server)
1. Install **BepInEx 5** on the server.
2. Unzip mod folder into:
   - `Valheim/BepInEx/plugins/` 
3. Start/restart the server once to load the plugin.
4. Add your **SteamID64** to the server `adminlist.txt`.

> Only the server needs the mod to block summons.  
> Admins need the mod on their PC to use the in-game UI.

## Usage
- Press **F7** to open/close the BossGate admin window.
- Toggle bosses **ENABLED / DISABLED**.
- Use **Enable all** / **Disable all** for quick setup.

### Default behavior
All bosses are **disabled** until an admin enables them.

## Configuration
A config file is generated after first launch:
- `BepInEx/config/bossgate.cfg`

Common options:
- UI toggle key (default: `F7`)
- Blocked message text (supports `{boss}`)
- Show message in chat and/or center screen
- Block unknown boss altars (safer)
- Block the Queen door while Queen is disabled

## Notes
- **Local host testing:** the host is treated as admin automatically.
- **Dedicated servers:** admin permissions come from `adminlist.txt` (server-side).

## Compatibility
- Target game version: **Valheim 0.221.4**
- BepInEx: **5.x**
- Language level: **C# 7.3**

## TO DO
- Better UI ?

## Known Bugs
- Please tell me !

## License
MIT
