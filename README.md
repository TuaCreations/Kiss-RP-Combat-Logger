# K.I.S.S. RP Combat Logger

A Dalamud plugin (internal project name `GardenTracker`) for Star Garden roleplay events. One shared, live combat state for the whole group:

- **Everyone** sees a window with enemy HP bars, Enemy Momentum, the party's HP / shield / potions / Interception / Momentum, and a roll log.
- **The GM's** client reads `/random 10` results from chat, queues them, and applies the Garden K.I.S.S. rules with one click (Attack, Heal, Save, Check, Enemy).
- **Enemy markers** are drawn in the world for everyone: a ring on the ground with the enemy's name and HP bar floating above it. GMs place them at their own position or on a target.

State lives in Supabase (the `garden` schema of the graduation-rsvp project). Players only ever read it; every change needs the session's GM key.

> Third-party tools are against Square Enix's Terms of Service. Use at your own risk, and keep it to a private dev plugin for the group.

---

## 1. Build it (once)

You need **Visual Studio 2022** (with the ".NET desktop development" workload) or **JetBrains Rider**, plus the current **.NET SDK** that Dalamud uses.

1. Run the game through **XIVLauncher** once, so Dalamud is installed on your PC (the build references it).
2. Open `GardenTracker/GardenTracker.csproj` in your IDE.
3. Check the first line of the `.csproj`: `Dalamud.NET.Sdk/15.0.0`. If Dalamud has released a newer API level, change it to the latest version on [nuget.org](https://www.nuget.org/packages/Dalamud.NET.Sdk).
4. Build in **Release**. The output lands in `GardenTracker/bin/x64/Release/GardenTracker/` (`GardenTracker.dll` + `GardenTracker.json`, plus a zip from the packager).

If the build fails, send Claude the error list. The likeliest spots are listed at the bottom.

## 2. Load it in game

1. In game: `/xlsettings` → **Experimental** → **Dev Plugin Locations** → add the full path to `GardenTracker.dll` → Save.
2. `/xlplugins` → **Dev Tools** → **Installed Dev Plugins** → enable **K.I.S.S. RP Combat Logger**.
3. `/garden` opens the tracker and `/gardengm` opens GM controls.

For other players: send them the build folder (or the packager's zip) and they repeat step 2. Only the GM needs the GM key.

## 3. Run an event

**GM, before the event**
1. `/garden`, then open **GM** → **Session** tab. Type a GM key (6+ characters, keep it secret), fill in **Create a new session** (e.g. code `HEARTHLESS`, name "The Hearthless"), and click **Create session**.
2. **Party** tab: **Add my party**, **Add target**, or type names. Names must match characters exactly. By default only rolls from players on this list are logged, so bystanders using `/random` in the zone are ignored (toggle in **Session → Options**).
3. **Scenario** tab: click **Built-in: hearthless.json** (or paste/load another scenario; see `SCENARIO_FORMAT.md`), set players present, and Ctrl+click **Load**. The Hearthless and the Husks start hidden; tick them visible in the **Enemies** tab at Phase 2.
4. **Markers** tab: stand where each enemy is and click **At me** (or target a stand-in and click **At target**). Add objective markers such as "The Anchor".

**Players**
- `/garden join HEARTHLESS` and keep the window open. Roll with `/random 10` as usual.

**During combat (GM)**
- New rolls appear in the **Rolls** tab. Pick the attack/heal target at the top, add a **Bonus** for modifiers, then click:
  - **Attack**: the enemy's dodge range is checked; otherwise the DMG table is applied and the roll is added to the roller's Momentum.
  - **Heal**: heals the selected party member and adds Momentum.
  - **Save / Check**: logged with natural 1 and natural 10 (Counter Attack) notes.
  - **Enemy**: your own enemy-action roll; it is added to Enemy Momentum.
  - **Ignore**: logged as not counted.
- **Enemy Actions** tab: tick targets if needed and click an ability (e.g. Ember Drain). Each target sees "roll /random 10" highlighted in their window, and their next roll resolves the save automatically: damage (shield first), status, enemy self-heal and Momentum reset are all applied. The enemy's own d10 goes into Enemy Momentum; when it's full, the **Unleash** button fires the scenario's finisher (Whiteout). **Cancel** a waiting save for an Interception or a GM call.
- The **Party** tab has HP +/-, **Use** potion (+5 HP), Interception, Momentum **Unleash**, effects, and Reset.
- Enemy HP can also be nudged with -5/-1/+1/+5 in the **Enemies** tab.

`/random` results only reach players near the roller, so the GM should stay close to the action.

## How it fits together

| File | Job |
| --- | --- |
| `Plugin.cs` | Entry point, Dalamud services, commands, fire-and-forget server calls |
| `StateSync.cs` | Polls the session state every second on a background task |
| `GardenApi.cs` | Calls the `garden_*` Postgres functions through Supabase REST |
| `RollWatcher.cs` | Parses `/random` results from chat (GM client logs them) |
| `GmActions.cs` | Combat rules applied by the GM: attacks, heals, potions, presets, markers |
| `Rules.cs` | The K.I.S.S. damage table |
| `Scenario.cs` | kiss-scenario/1 parser, validator and loader |
| `Scenarios/*.json` | Built-in scenarios, embedded in the DLL |
| `MarkerOverlay.cs` | Draws rings, names and HP bars in the world |
| `Windows/TrackerWindow.cs` | Player view |
| `Windows/GmWindow.cs` | GM controls |

Server side (already deployed): tables `garden.sessions / members / enemies / rolls / markers / saves` with RLS on and no direct access. Everything goes through `public.garden_*` security-definer functions; every write checks the session's bcrypt-hashed GM key.

## If the build complains

These Dalamud APIs change between API levels and are the first places to check:

- `RollWatcher.OnChatMessage`: the `IChatGui.ChatMessage` delegate signature.
- `Plugin.LocalPlayerName` / `GmActions.MyPosition`: `IObjectTable.LocalPlayer` (older APIs used `IClientState.LocalPlayer`).
- `using Dalamud.Bindings.ImGui;`: ImGui bindings namespace (older plugins used `ImGuiNET`).
- `ImGui.GetClipboardText()` in `GmWindow.DrawScenario`: some binding versions name it `GetClipboardTextS()`.
- Non-English clients: the roll regex in `RollWatcher.cs` matches the English "Random! … rolls a 7" text.
