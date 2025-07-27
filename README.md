# Blacklist
A Town of Salem 2 mod to track names on a local or remote blacklist. When players on the list join your lobby, Blacklist can alert you or kick them automatically.

## Installing
This mod requires [Salem Mod Loader](https://github.com/Curtbot9000/SalemModLoader), but is not available on its mod browser yet.
After installing SML, you can [download the latest release of Blacklist](https://github.com/LyricLy/Blacklist/releases/latest) and copy the .dll file to the `Town of Salem 2/SalemModLoader/Mods/` directory.

## Configuring
The mod's settings menu allows you to decide whether to kick blacklisted players automatically.
If you would prefer not to be notified of blacklisted players you can't do anything about (because you are not host or not in a custom), you can change this behaviour too.

Changing the blacklist itself is a bit more technical. After starting a lobby with the mod installed for the first time, a `config.kdl` file is created in `Town of Salem 2/SalemModLoader/ModFolders/Blacklist/`. You'll need to edit it with a text editor.

To blacklist a player, write their name in quotes preceded by a hyphen.
```
- "EvilBadTroll"
```

You can organize entries into labelled sublists. The labels will be shown in-game.
```
list "Mediocre Players" {
    - "overcon"
}

list "Ban On Sight" {
    - "oldetown"
}
```

To load a list hosted online, use `src="url"` in place of a sublist's body.
```
list "Ballows Build" src="https://example.com/"
```

You can use `!` instead of `-` to override an entry from a sublist. (That is, to "un-blacklist" someone.)
```
list "Ballows Build" src="https://example.com/"
! "Redhead1321"
```

## Building (for developers)
On Linux:
```
dotnet build
cp ~/.nuget/packages/kadlet/0.1.0/lib/netstandard2.0/Kadlet.dll '~/.steam/steam/steamapps/common/Town of Salem 2/SalemModLoader/Mods/'
```
On Windows:
```
dotnet build -p:SteamLibraryPath="C:\\Program Files (x86)\\Steam\\"
copy %userprofile%\.nuget\packages\kadlet\0.1.0\lib\netstandard2.0\Kadlet.dll "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Town of Salem 2\\SalemModLoader\\Mods\\"
```

After copying Kadlet the first time, only the first command (`dotnet build`) needs to be run for subsequent builds. `Blacklist.dll` is copied to your mod folder automatically upon building.

After installing [`ilrepack`](https://github.com/gluck/il-repack), `dotnet publish` will put a `Final.dll` file in `bin/Debug/netstandard21/publish`.
This file can be used as a mod without having to copy `Kadlet.dll` and is suitable for distribution.
