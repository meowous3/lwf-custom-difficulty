# LWF Custom Difficulty

BepInEx plugin for **Lazy Witch's Factory**. Adds a Custom difficulty whose time limit, repayment count, repayment curve and taxes are set from the difficulty selection screen.

Custom runs pay `x0.00` and write nothing to your save, no cleared difficulty, no unlock notice, no patron clears.

Built against `0.21.0` (Steam app 3971650). Also runs on the demo.

## Install

1. Install [BepInEx 5.4.23.5 win_x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) into the game folder, next to `LazyWitchsFactory.exe`.

2. Run the game once.

3. Put `LwfCustomDifficulty.dll` in `BepInEx/plugins/`.

Custom is the leftmost card in the difficulty carousel.

## Options

| Row | Accepts | Default |
|---|---|---|
| Time Limit | minutes, `0` = none | 30 |
| Repayments | ≥ 1 | 5 |
| First Repayment | ≥ 1 | 10 |
| Growth | Linear / Multiplicative / Exponential | Linear |
| Growth Amount | ≥ 0, decimals allowed | 20 |
| Surcharge | ≥ 0, `0` = off | 500 |
| Surcharge Every | ≥ 1 | 5 |
| Taxes | on / off | off |

Edits apply to the next run. Values persist in `BepInEx/config/dev.meow.lwfcustomdifficulty.cfg`.

## Growth

The first demand is **First Repayment**. Each one after that:

```
Linear          target += GrowthAmount
Multiplicative  target *= GrowthAmount
Exponential     target += FirstRepayment × GrowthAmount^n
```

then `+= Surcharge` whenever `n` divides evenly by **Surcharge Every**.

In Exponential, Growth Amount is the acceleration: every step is the one before it times that number. From `10`:

| Growth Amount | Demands |
|---|---|
| `1.1` | 10, 21, 34, 48, 63 |
| `1.5` | 10, 25, 48, 82, 133 |

Multiplicative is the steep one — `×2` from `10` reaches 10,240 by the eleventh demand where Exponential at `1.5` reaches 1,713.

A multiplier below `1` holds the curve flat rather than reducing it. Targets cap at `536870911`.

## Build

Set `GameDir` in `Directory.Build.props`, then:

```bash
dotnet build src/LwfCustomDifficulty/LwfCustomDifficulty.csproj -c Release
dotnet test tests/LwfCustomDifficulty.Tests
```

## Licence

MIT. Not affiliated with the developers of Lazy Witch's Factory.
