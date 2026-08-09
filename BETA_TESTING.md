# Phở Simulator — Beta 1 (playtest build)

The point of this build is **one question: is the core loop fun?**
Everything is grey boxes. Ignore how it looks — that's deliberate, art comes
after the answer.

---

## Run it

**Option A — the standalone app (no Unity needed)**

```
open Build/PhoSimulator.app
```

If macOS says it's from an unidentified developer: right-click the app →
**Open** → **Open**. (It's unsigned; that's expected for a local build.)

**Option B — in the Unity Editor**

Open `Assets/Scenes/Boot.unity` **first**, then press Play. If you press Play
on an empty/Untitled scene nothing will happen — the whole game lives in
`Boot.unity`.

---

## Controls

| Input | Action |
|---|---|
| `W A S D` | Move |
| Mouse | Look |
| `Shift` | Sprint |
| `E` | Interact (context-sensitive — the prompt tells you) |
| `Esc` | Release the mouse cursor (click to recapture) |
| `F5` / `F9` | Debug save / load |

---

## The loop to test (≈5 minutes)

The restaurant starts in **Prep**. You have unlimited-ish starting stock and
$1,500.

1. **Start the broth first — it's the long pole.** Walk to the **BrothPot**
   (kitchen, to your right) with empty hands and press `E`. It takes about
   **100 seconds** to become Ready. Start it, then do everything else while
   it simmers.
2. **Grab a bowl** from the bowl stack and press `E`.
3. **Add ingredients** by walking to each station and pressing `E`:
   rice noodles → beef brisket. (That's the recipe for *Phở Tái*.)
4. **Ladle the broth** once the pot reads Ready — walk to it holding the bowl
   and press `E`.
5. **Open the restaurant** at the **RestaurantSign** near the entrance.
   **Customers only arrive once you're open**, so don't open until the broth
   is going.
6. **A customer arrives** roughly every 20s, walks to a table, and orders.
7. **Place your finished bowl on the PassCounter** (`E`). It gets scored and
   automatically fulfils the oldest matching order.
8. **You get paid.** Watch the cash readout in the top-left go up.
9. **Bus the table** they left — walk up to it and press `E`.
10. **Buy the upgrade** at the **UpgradeStation** in the kitchen ($450, a
    commercial burner). Then start a new pot: broth should be noticeably
    faster.
11. **Close the restaurant** at the sign when you're done.
12. **`F5` to save, `F9` to load** — check your cash and day survive.

---

## What I actually want to know

Not bugs (I'll find those). **Feel:**

- Is the cook → serve → get paid loop satisfying, or is it busywork?
- Does the ~100s broth wait create useful pressure, or just dead time?
- Is walking between stations interesting, or tedious?
- Does one customer every 20s feel too slow, too frantic, or about right?
- Was anything confusing about what to do next?

---

## Known limitations — not bugs, don't report these

- **Everything is untextured grey boxes.** No art yet, by design.
- **The HUD is programmer-art text** in the corners. It's a readout, not a
  designed UI.
- **Ingredient cost always reads $0** in the day summary — there's no
  purchasing system yet, so nothing ever debits ingredients. Revenue and
  rent are real.
- **Utilities is a flat $12.50/day** constant — the balance config has no
  field for it yet.
- **No shopping trip.** Starting stock is just handed to you (an approved
  scope cut — the wet market is post-vertical-slice).
- **Rent ($350) only hits on day 7**, and a full day is 15 real minutes, so
  you won't see it in a short session.
- **Customer pathing is basic** and they're capsules.
- **Only 2 recipes** (Phở Tái, Phở Chín) and **2 customer types** (an
  impatient office worker, a demanding food critic).

---

## If something breaks

The standalone build's log is at:

```
~/Library/Logs/DefaultCompany/PhoSimulator/Player.log
```

That plus "what were you doing" is enough for me to chase it down.
