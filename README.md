# BossGate

BossGate is a lightweight Valheim server-side mod that lets admins **enable/disable boss summons per world**.  
By default, **all bosses are blocked**, so players can’t progress bosses without admin approval. Changes apply instantly (no restart).

## Features

- ✅ **Server-authoritative boss gating** (blocks boss summon attempts)
- ✅ **Simple in-game admin UI** (toggle bosses on/off)
- ✅ **No server restart** required (uses world global keys)
- ✅ **Optional**: block the **Sealbreaker door** until the Queen is enabled
- ✅ **Chat / center-screen message** when a summon is blocked
- ✅ Works in **local host** for testing (host is treated as admin)

## How it works

BossGate intercepts the boss summon RPCs on `OfferingBowl` (the altar interaction).  
If the boss is disabled, the summon is blocked and a message is shown.

Boss availability is stored as **world global keys**, so it persists with the world save.

## Installation

### Client / Singleplayer (local host)
1. Install **BepInEx 5** for Valheim.
2. Copy `BossGate.dll` into:
   - `Valheim/BepInEx/plugins/`
3. Launch the game.

### Dedicated Server
1. Install **BepInEx 5** on the server.
2. Copy `BossGate.dll` into:
   - `Valheim/BepInEx/plugins/`
3. Restart the server once to load the plugin.
4. Make sure your SteamID64 is in the server `adminlist.txt`.

> Only the server needs the mod for blocking summons.  
> (Clients do not need it for the block itself, but admins need it to use the in-game UI.)

## Usage

- Press **F7** to open/close the BossGate admin window.
- Toggle bosses **ENABLED / DISABLED**.
- Use **Enable all** / **Disable all** buttons for quick setup.

### Default behavior
- All bosses are **disabled** until an admin enables them.

## Configuration

A config file will be generated after first launch:
- `BepInEx/config/bossgate.cfg`

Common options:
- UI toggle key (default: `F7`)
- Blocked message text
- Show message in chat and/or center screen
- Block unknown boss altars (safer)
- Block Sealbreaker door while Queen is disabled

## Notes

- **Local host testing:** the host is treated as admin automatically (for convenience).
- **Dedicated servers:** admin permissions come from `adminlist.txt` (server-side).

## Compatibility

- Target game version: **Valheim 0.221.4**
- BepInEx: **5.x**
- Language level: **C# 7.3**

## TO DO

- Better UI ?
- Mistlands Boss fix

## Known Bugs

- Mistlands Boss door can still be opened and can softlock the arena.
- Fix / workaround: Use a Seeker Soldier Trophy to spawn the boss.

## License

MIT
