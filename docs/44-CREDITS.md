# Credits

The roll behind **EXTRAS → CREDITS**, and the file it is read from.

> **The register is `Assets/StreamingAssets/Data/credits.json`.** Everything on
> the screen comes from it. Adding a person, fixing a spelling or adding a role
> is a text edit — no compiler, no programmer, no build. Keep this file in step
> with the schema in `Data/CreditsData.cs`.

---

## 1. Why it is data

Credits change for reasons that have nothing to do with the program. Somebody
joins between builds. A name is spelled wrong and has to be right before a
submission. A contractor has to be added at three hours' notice. Every one of
those is a text edit, and none of them should need a code change — a credits
screen with the names compiled into it is a screen that is wrong for as long as
it takes to schedule a build.

So `CreditsUI` reads the file and lays out whatever is in it. It has no opinion
about which roles exist, how many people hold one, or how many blocks there are.

The same rule the rest of the shipped data follows: under
`StreamingAssets/Data`, read through `Core/StreamingAssetsFile.cs` — **never**
`File.ReadAllText` on `Application.streamingAssetsPath`, which is a URL into the
APK on Android and a URL on a server on the web — and named in
`StreamingAssetsFile.CoreFiles` so the web build preloads it.

---

## 2. The file

```jsonc
{
  "title": "IRON MERIDIAN",              // the masthead
  "subtitle": "…",                       // one line under it
  "website": "www.example.com",          // given a block of its own at the foot
  "sections": [
    {
      "heading": "PRODUCTION",           // a block of the roll
      "roles": [
        { "role": "Game Director", "names": [ "John Doe" ] },
        { "role": "Designers",     "names": [ "John Doe", "John Doe" ] }
      ]
    }
  ],
  "acknowledgements": [ "…" ],           // engine, data, standards
  "copyright": "© 2026 …"
}
```

| Field | Notes |
|---|---|
| `title` / `subtitle` | The fixed masthead. Empty `title` falls back to `GameConfig.GameName` |
| `website` | Shown alone, in the accent colour, at heading size — see §3 |
| `sections[].heading` | Block label: PRODUCTION, DESIGN, ENGINEERING, ART AND INTERFACE, AUDIO |
| `sections[].roles[].role` | The job. Drawn upper-case whatever the file says |
| `sections[].roles[].names` | **A list**, because most roles are held by more than one person. A schema with one name per role makes the second holder a second role with the same title |
| `acknowledgements` | Lines after the roll. **Not** a licence register — that is `docs/37-THIRD-PARTY.md`, which is the file a lawyer reads and this is the one a player does |
| `copyright` | Printed above the build number |

### The roles currently listed

| Block | Roles |
|---|---|
| PRODUCTION | Game Director · Creative Director · Producer |
| DESIGN | Lead Designer · Designers |
| ENGINEERING | Programmers |
| ART AND INTERFACE | UI |
| AUDIO | Music · Sound |

Every name is `John Doe` and the address is `www.example.com` — placeholders,
put in so the layout can be judged against real-shaped content. Replace them
before shipping; nothing in code needs to change when you do.

---

## 3. The screen

`Assets/Scripts/UI/CreditsUI.cs`, built at runtime like every other screen
(golden rule 2).

**A roll, not a page.** One centred column that scrolls, the way a film's
credits do and the way every player already expects. A credits screen is read
*down*, one role at a time; a two-column grid or a set of cards makes the reader
choose an order the list does not have.

| Device | Why |
|---|---|
| **660 px column** on a 1920 px screen | Names are short lines. Set across a full monitor they put a metre of whitespace between the role and the person who held it — the two things the reader is trying to associate |
| **Role right-aligned, names left-aligned, against a seam at 42 %** | The layout a film's end roll uses, for the reason a film uses it: the eye runs down the seam and both columns stay findable with no rule between them |
| **One row per *role*, not per name** | The row grows with the number of names. Repeating "Programmers" three times would say there were three jobs |
| **Headings centred** while the roles are set against the seam | The heading labels the whole block, so it belongs to the whole width; centring it is what makes the seam read as a device *inside* the block rather than as the page's alignment |
| **Masthead fixed, roll scrolls** | The masthead answers "whose credits are these", and an answer that scrolls off the top is one the reader has to scroll back for |
| **The website gets its own block** | It is the one line a reader might act on. Buried in a paragraph of thanks is how a link goes unread |

Artwork is `BackgroundId.ExtrasCredits` — `credits.png`, the same image the
EXTRAS board previews behind its CREDITS row — at a **0.80 scrim**, heavier than
the board's own. This screen is a column of small text read at length; the
artwork is a backdrop to it rather than the subject. See
`docs/11-GAME-MENU.md`.

**Never blank.** A missing or unreadable file falls back to a minimal roll that
still names the game, and logs why. A credits screen coming up empty because a
file moved would be the most conspicuous possible failure — it is the one screen
whose entire content is other people's names.

---

## 4. Editing it

1. Open `Assets/StreamingAssets/Data/credits.json`.
2. Add the person to the `names` list of their role, or add a `{ "role": …,
   "names": [ … ] }` to a section, or add a whole section.
3. Save. Play → EXTRAS → CREDITS.

Order is the file's order, top to bottom, in both directions — sections and the
roles inside them. There is no sort; the roll is a document and its order is an
editorial decision.

Nothing caches across sessions, and `CreditsData.Reload()` drops the in-session
cache if you need to see an edit without leaving Play mode.

---

## 5. Where the code lives

| File | Role |
|---|---|
| `Assets/StreamingAssets/Data/credits.json` | **The register** |
| `Data/CreditsData.cs` | The schema, the loader and the fallback roll |
| `UI/CreditsUI.cs` | The screen |
| `Core/StreamingAssetsFile.cs` | `CoreFiles` — what the web build preloads |
| `UI/BackgroundCatalog.cs` | `ExtrasCredits`, the artwork |

---

## Related

`docs/11-GAME-MENU.md` (the EXTRAS board and the artwork register) ·
`docs/37-THIRD-PARTY.md` (asset licences — the legal register, not this one) ·
`docs/41-WEB.md` (why the file must be in `CoreFiles`)
