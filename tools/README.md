# Headless battle renderer

## Local CLI export (Unity Editor)

1. Install [ffmpeg](https://ffmpeg.org/) and ensure it is on `PATH`.
2. Open the project in Unity with `Assets/Game/Scenes/Game.unity` loaded.
3. Use menu **TPD Arena → Export Battle From Sample JSON (FFmpeg)**.

Or run batchmode from the project root:

```powershell
& "C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe" `
  -batchmode -nographics -projectPath . `
  -executeMethod TPD.Arena.BattleExportCli.Run `
  -exportJson tools/sample-battle-request.json `
  -output Exports/cli_battle.mp4 `
  -forceFFmpeg `
  -logFile -
```

## Linux headless build

`Game.unity` is the only scene in build settings. CI builds with:

- Target: `StandaloneLinux64`
- Binary: `build/StandaloneLinux64/TPD_Arena_Core.x86_64`

Manual render on Linux (artifact contents):

```bash
BINARY_DIR=path/to/extracted/battle-renderer
cd "$BINARY_DIR"
chmod +x TPD_Arena_Core.x86_64
xvfb-run --auto-servernum ./TPD_Arena_Core.x86_64 -batchmode -nographics -logFile - \
  -executeMethod TPD.Arena.BattleExportCli.Run \
  -exportJson /path/to/request.json \
  -output /path/to/battle.mp4
```

Requires `ffmpeg` in `PATH` (or set `FFMPEG_PATH`).

## Battle request JSON

MVP:

```json
{
  "leftHp": 80,
  "rightHp": 100
}
```

Optional ability loadouts (requires `BattleAbilityRegistry` on `BattleConfig`):

```json
{
  "leftHp": 80,
  "rightHp": 100,
  "leftAbilities": ["Fireball", "Heal", "Shield"],
  "rightAbilities": ["Frostball", "Stun", "Heal"]
}
```

Sample file: `tools/sample-battle-request.json`
