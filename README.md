# Robin.Net

A native C# / SDL2 port of *The Adventures of Robin Hood* that reproduces the
original program's rules, simulation, input, presentation and save format from the
reconstructed assembly. It uses the original data files, with no x86 interpreter,
DOS emulator or executable dependency.

## Run

Requires the .NET 10 runtime (or a self-contained publish) and the
original game files, which are not included:

```
dotnet run --project RobinHood.csproj
```

`SDL2.dll` (x64, 2.30.9, zlib licence) is copied from [native/win-x64](native/README.md).
Place the original game resource files (`ERULES.PRG`, `SCREEN.GFX`, and the other
required files) in the same directory as the executable. When using `dotnet run`,
that is the project's build output directory; for a published build, it is the
publish directory. The game does not search the working directory or accept
command-line options.

Publish a runnable folder (framework-dependent, or add `-r win-x64 --self-contained`):

```
dotnet publish RobinHood.csproj -c Release -o dist
```

## Playing

Mouse: click the sidebar buttons, then people or map cubes, as in the original; any
key or click advances title screens and waits. Keyboard shortcuts are the original
button keys. F11 toggles fullscreen. The game saves `STATUS.DTA` (original format)
when it quits through the game's own exit command and restores it on the next start.

## Implemented

- Original squeezed-file decoding, program-file parsing for all four languages.
- Complete rule interpreter: all 50 conditions and all 102 actions, the post-startup
  scheduler (input rules, alert round-robin, timers, signals, modes, proposals).
- Sequencer, movement, collision, route finding, interaction matrix, hazards.
- Keyboard ring, mouse events, sidebar buttons, hit testing, cube picking, cursor.
- Message decoding, wrapping, sub-message choice, caption bubble and speaker pointer,
  numeric displays, bars, object icons, status gauge, puffs.
- Palette fades, scene changes, aerial map; persistent 320x200 frame buffer.
- `STATUS.DTA` export/import in the original layout.

## Audio
[Engine/GameAudio.cs](Engine/GameAudio.cs) routes the original sound and music
actions through the sound-ID tables in `ADLIB.OVL`. An SDL audio device plays
the sequences from `ROBIN.MUS` using the AdLib instrument events and the
NukedOPL3Sharp OPL emulator. The DOS executable and `ADLIB.BIN` are not run.
If the audio files or an audio device are unavailable, the game reports the
problem and continues without sound.

The game decodes and validates the MIDI resources in `ROBIN.MUS` in memory.
They preserve the original AdLib instrument events, but ordinary MIDI players
ignore those events and generally play piano. The game renders the decoded
sequences directly; it does not depend on a system MIDI player.
