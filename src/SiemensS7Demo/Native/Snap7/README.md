# Snap7 Native Runtime

This folder is part of the EnviroEquipmentFinalEdition project runtime.

It contains the Siemens S7 native communication dependency used by `Snap7S7Adapter`.
The application must be runnable from this repository without depending on a sibling
`H:\qtFileForVscode\snap7` checkout.

## Files

```text
win64\snap7.dll                    Native runtime copied to the app output as snap7.dll
reference\dotnet\snap7.net.cs       Official .NET wrapper kept as an API reference
licenses\lgpl-3.0.txt               Upstream LGPL license text
licenses\gpl.txt                    Upstream GPL license text
UPSTREAM_README.md                  Upstream Snap7 readme snapshot
HISTORY.txt                         Upstream Snap7 history snapshot
```

Upstream source:

```text
https://github.com/davenardella/snap7
```
