# kiss-scenario/1: scenario file format

A scenario is one JSON file that loads a whole fight into the K.I.S.S. RP Combat Logger: enemies, their HP scaling, dodge ranges, the ability macros GMs click, the status legend, the passive and the Enemy Momentum finisher.

**Getting a new one:** describe the fight to Claude ("a three-phase fight against a corrupted Ixali war-chief in the Sea of Clouds, about 8 players...") and ask for a kiss-scenario file. Then either:
- copy the JSON and click **GM → Scenario → Paste from clipboard**, or
- save it as `something.json` in the plugin's scenarios folder (**Open folder** on that tab) and use **Load file**.

The preview lists any problems before anything is loaded. Loading needs a Ctrl+click and replaces the enemies, markers and waiting saves. The party and roll log stay.

## Top level

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `format` | string | yes | Always `"kiss-scenario/1"` |
| `name` | string | yes | Shown to players in the window header |
| `summary` | string | no | One or two lines for the GM preview |
| `enemy_momentum_max` | int | no (25) | When Enemy Momentum reaches this, the momentum action is ready |
| `passive` | string | no | Encounter-wide rule shown under the enemies (e.g. downed-not-dead) |
| `statuses` | list | no | Status legend: `{ "name", "effect", "clears" }` |
| `momentum_action` | action | no | The finisher unleashed when Enemy Momentum is full |
| `enemies` | list | yes | One entry per enemy (a swarm or pack is one enemy) |

## Enemy

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `name` | string | yes | |
| `hp_per_player` | int | one of these | HP = this × players present (set at load time) |
| `hp` | int | one of these | Fixed HP, ignores headcount |
| `dodge_max` | int 0–9 | no (0) | Player attack rolls at or below this miss. 0 = never dodges |
| `effects` | string | no | Short tag shown beside the name |
| `hidden` | bool | no (false) | Starts hidden from players (reveal it at its phase) |
| `phase` | int | no (1) | For the GM's reference |
| `abilities` | string | no | Text players see. Left empty, it's generated from `actions` |
| `actions` | list | no | Ability macros (below) |

## Action (ability macro)

Clicking an action calls for a save from each target. That player's next `/random 10` resolves the save automatically. A roll at or below `fail_max` (or any natural 1) fails: `damage` hits shield first, then HP, and `status` is added to their effects. A natural 10 always succeeds and counts as a Counter Attack. Unless the GM turns it off, each action also rolls the enemy's own d10 into Enemy Momentum.

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `name` | string | yes | Button label |
| `targets` | string | yes | `choose` (GM ticks players), `random`, `all`, `highest_momentum`, `lowest_hp` |
| `count` | int | no (1) | How many targets for `choose` / `random` / `highest_momentum` / `lowest_hp` |
| `fail_max` | int 0–9 | yes | Save fails on 1 to this number |
| `damage` | int | yes | Damage on a failed save |
| `status` | string | no | Status applied on a failed save (match a `statuses` name) |
| `heal_self` | int | no | Enemy heals this much for each failed save |
| `reset_momentum` | bool | no | A failed save drops the target's Momentum to 0 |
| `phase` | int | no | Shown on the button as [P2] etc. |
| `note` | string | no | Reminder for the GM (e.g. "every other turn") |

Anything the macros can't express (regeneration, bodyguards, phase triggers, a rising dodge range) goes in `abilities` text for the GM to apply by hand.

## Balancing reference (Garden K.I.S.S. system)

- Players: 20 HP, 3 potions (+5 HP), 1 Interception, Momentum to 25 (10 DMG or 5 healing + 2 rolls).
- Player damage per roll: 1 → 1, 2–3 → 2, 4–5 → 3, 6–7 → 4, 8–9 → 5, 10 → 5 + another roll. About 3.5 a turn.
- Ten players deal roughly 35 a round. A ~2-hour event has 6–8 rounds of combat, so total enemy HP around 20 × players fills a fight.
- Fail chance = `fail_max` × 10%. Typical enemy hits are 2–5 DMG. Keep "all players" abilities at `fail_max` 2–4.

## Example

See `GardenTracker/Scenarios/hearthless.json`, the built-in Hearthless fight.
