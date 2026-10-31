# Native dependencies

`win-x64/SDL2.dll` is the only native dependency.

| Item | Value |
| --- | --- |
| Library | Simple DirectMedia Layer (SDL2) |
| Version | 2.30.9 (file version 2, 30, 9, 0) |
| Architecture | x64 (PE machine 0x8664) |
| SHA-256 | `58F0A2AF6653445B48E58ACE6890FBE237BF76452C9743E800358D6B935DED64` |
| Origin | The `SDL2Test` sample supplied with this project (Debug/net10.0 output) |
| Licence | zlib, Copyright (C) 1997-2024 Sam Lantinga; see [../THIRD-PARTY.md](../THIRD-PARTY.md) |

To use a different build, pass `-p:SdlNativePath=C:\path\to\SDL2.dll` to `dotnet build`
or `dotnet publish`. Only a 64-bit SDL2 2.x library is supported. Verify a replacement
with a normal game launch before use.
