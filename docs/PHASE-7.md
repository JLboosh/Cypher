# Phase 7: Google Calendar, Google Tasks and Notion

**Result:** an **IMPORT** button glows on the left of your desk. It opens a holographic window
where you pick Google Calendar, Google Tasks or Notion, choose a calendar, list or database, tick
items, and import them as holograms (marked GCAL / GTASKS / NOTION). Importing again
**updates** instead of duplicating. Edits can sync back to the original, and deleting an
imported task asks whether to delete it at the source too.

## What's new

| File | Job |
|---|---|
| `Integrations/GoogleAuth.cs` | Google sign-in for desktop apps: loopback redirect (`http://127.0.0.1:<port>`) + PKCE; refresh token stored in `~/Library/Application Support/DefaultCompany/CypherWorkshop/google-token.json`; auto-refresh; Disconnect revokes access |
| `Integrations/GoogleClient.cs` | Calendar: list calendars + events (next 60 days); Tasks: list lists + open tasks; push edits (PATCH); delete |
| `Integrations/NotionClient.cs` | Notion API **2026-03-11**: finds databases ("data sources") shared with your integration, auto-detects the title / date / done / notes columns, imports pages, pushes edits, moves pages to the trash |
| `Integrations/IntegrationHub.cs` | Import with duplicate protection, the sync-back queue (retries every minute if offline, survives restarts), delete-at-source handling |
| `Integrations/MiniJson.cs`, `Http.cs`, `ImportCandidate.cs` | JSON for Notion's dynamic columns, HTTP helper, shared data types |
| `UI/ImportPanel.cs` | The import window + the desk button |
| `UI/HoloPrompt.cs` | Reusable two-button question (click or voice); now also used for "keep my placement?" |

Voice: **"Hey Cypher, import from Notion"** / "…from my calendar" / "…from Google Tasks".

## Setup A: Google (about 10 minutes, free)

1. Go to **https://console.cloud.google.com/** and sign in. Top bar → project picker →
   **New project** → name it `Cypher` → **Create**, then select it.
2. **APIs & Services → Library**: search and **Enable** both **Google Calendar API** and
   **Google Tasks API**.
3. **Google Auth Platform** (a.k.a. *OAuth consent screen*) → **Get started**:
   App name `Cypher`, your email as support email → **Audience: External** → contact email →
   agree → **Create**.
4. **Clients → Create client** → Application type **Desktop app** → name `Cypher Desktop` →
   **Create**. Copy the **Client ID** and **Client secret** (or download the JSON).
5. **Avoid weekly sign-ins:** **Audience → Publish app → In production**. (In "Testing" mode
   Google expires your sign-in every 7 days.) Since the app isn't verified, sign-in will show
   *"Google hasn't verified this app"*. That's expected for a personal app: click
   **Advanced → Go to Cypher (unsafe)**. If you prefer to stay in Testing, add your Gmail
   under **Audience → Test users** and expect to reconnect weekly.
6. In Unity: **Cypher → Open Config File**, then paste:
   ```json
   "googleClientId": "1234...apps.googleusercontent.com",
   "googleClientSecret": "GOCSPX-...",
   ```
   Save, then restart Play.
7. In the app: **IMPORT** (desk, left) → **Google Calendar** → **Connect Google** → approve in
   the browser → the tab says *"Cypher is connected to Google"* → back in the app, the list fills.

Permissions requested: read your calendar list; read/edit events (only needed for syncing edits
back); read/edit Google Tasks.

## Setup B: Notion (about 3 minutes, free)

1. Go to **https://www.notion.so/profile/integrations** → **New integration** → name `Cypher`,
   pick your workspace, type **Internal** → **Save**.
2. On its page: **Capabilities**: *Read content* and *Update content* (needed for sync-back and
   trash). Copy the **Internal Integration Secret**.
3. **Cypher → Open Config File** → `"notionToken": "ntn_..."` → save → restart Play.
4. In Notion, open your tasks **database** as a full page → **•••** (top right) →
   **Connections** → add **Cypher**. (Integrations only see what you share with them.)
5. In the app: **IMPORT** → **Notion**. Click the **FROM** button to cycle through databases.

**Column detection:** title = the database's title column; due date = a Date column (prefers
names containing *due / deadline / date*); done = a Checkbox (prefers *done / complete*) or a
Status column whose value contains *done / complete*; notes = a Text column named like
*notes / description / details*. Checkbox "done" syncs back; Status is read-only, because option
names differ per database.

## How sync works

| You do | Happens |
|---|---|
| Import the same item again | That task is **updated** (title, notes, date, done), no duplicate. The list marks it `[update]` |
| Edit an imported task, Save | The edit panel asks: **Update original** or **Keep local only** (Phase 3). "Update original" pushes title, notes, date (and done for Google Tasks / Notion checkbox) |
| Offline when pushing | Retries every minute until it works, even after restarting the app |
| Remove an imported task | Asks: *"Delete it there too?"* (**Delete in Google/Notion** / **Only here**). Answer by click or voice. Notion pages go to Notion's trash (restorable for 30 days) |
| Prefer not to be asked | Config `"deleteSyncMode"`: `"always"` or `"never"` |

Calendar events become tasks due at the event's start time (all-day events: due that day).
Moving the due date pushes a new start time and keeps the event's length.

## Test

| # | Do this | Expect |
|---|---|---|
| 1 | Click **IMPORT** on the desk | Window opens in front; camera and hotkeys are locked; Esc or X closes |
| 2 | Google Calendar tab before setup | Status explains what to add to the config |
| 3 | After setup: **Connect Google** | Browser consent → "Cypher is connected" → events of your primary calendar listed |
| 4 | Tick 2 events → **Import 2** | "Imported 2 new". Two holograms build in with a **GCAL** tag, ranked by date |
| 5 | Same events again | Marked `[update]`; importing again says "updated 2", no duplicates |
| 6 | Google Tasks tab | Your task lists (FROM cycles lists); import one → **GTASKS** tag |
| 7 | Edit an imported GTASKS task's title → Save → **Update original** | Within ~10 s the Console says `Synced "..." to Google Tasks`; check tasks.google.com |
| 8 | Remove an imported task | Crumple animation, then the question "Delete it there too?" (Cypher also asks aloud). Choose either |
| 9 | Notion tab (after setup B) | Your shared database; import a few pages → **NOTION** tag |
| 10 | "Hey Cypher, import from Notion" | Opens the window on the Notion tab |
| 11 | **Disconnect Google** | Token deleted and revoked; tab asks you to connect again |

## Troubleshooting

| Symptom | Fix |
|---|---|
| Browser shows "Access blocked: app has not completed verification" | You're in Testing mode and your Gmail isn't a test user. Add it under **Audience → Test users**, or publish to **In production** (step 5) |
| `redirect_uri_mismatch` | The client type must be **Desktop app** (not Web) |
| `403 ... API has not been used in project` | Enable the Calendar / Tasks API (step 2) in the same project; wait a minute |
| `invalid_grant` after a week | Testing-mode expiry: reconnect, or publish to production |
| Notion: "No databases shared" | Step B4: add the Cypher connection on the database page itself |
| Notion: `object_not_found` / 404 | Same as above, or the page was deleted |
| Sync never happens | The Console shows a warning with the API's error message. Calendar events you only have *read* access to can't be edited |

## Privacy notes
Google tokens are stored locally in `google-token.json`, and the Notion secret in
`cypher-config.json`, both in `~/Library/Application Support/DefaultCompany/CypherWorkshop/`,
outside the Unity project. Data goes directly between your Mac and Google/Notion. **Disconnect
Google** deletes the token and revokes access. You can also revoke at
https://myaccount.google.com/permissions.

---

When everything works, ask for **Phase 8**: building the standalone macOS app (app icon,
microphone permission, background running with App Nap off, ad-hoc signing, launching it like
a normal app).
