# Simpl Courses privacy policy

_Last updated: 10 September 2026_

Simpl Courses is a browser extension that redraws the Canvas learning-management system in your browser. It has no servers, no accounts and no analytics. This page describes exactly what the extension touches and where that data goes.

## What the extension reads

- **Your Canvas data.** On Canvas pages the extension calls the same Canvas REST API your browser already uses, with the session you are already signed in with, to draw your dashboard, courses, assignments, grades, calendar, inbox, groups, quizzes and submissions. Responses are cached briefly in the extension's storage in your browser so pages open quickly, and are never sent anywhere else.
- **Your Canvas settings.** Course colours, nicknames, favourites and the dashboard view come from your Canvas profile and are changed only when you change them in the interface, through the same Canvas endpoints Canvas's own pages use.
- **Page content, only for the smart panel.** If you open the smart panel and ask something, the content of the page you are looking at (for example an assignment description or a discussion thread) and your question are sent from your browser to the provider you chose — Anthropic (Claude) or OpenAI (ChatGPT) — using the API key you entered. Nothing is sent to a provider unless you open the panel and send a message, and you can turn page content off under Settings. Those requests are governed by the provider's own privacy policy.

## What the extension stores

Everything below lives in the extension's local storage in your browser and is removed when the extension is uninstalled:

- your settings (appearance, the sites you enabled it on, preferences such as list groupings);
- your Claude and/or ChatGPT API key, if you added one — stored only locally, never included in settings exports, and sent only to that provider's API;
- unsent drafts (a text-entry submission you have not sent yet);
- the Grades page's optional inputs (a prior GPA, a goal, target grades) and its daily GPA snapshots, which are computed from the scores Canvas returns and never leave your browser.

## What the extension never does

- It never sends your Canvas data, keys or usage to the extension's authors or to any server of its own. There is none.
- It never sells, shares or transfers your data to third parties, and never uses it for advertising, tracking, or determining creditworthiness.
- It never performs an action on Canvas that you did not take in the interface: marking items done, dismissing, favouriting, replying, messaging and handing in work all happen only when you press the button, through the same endpoints Canvas's own pages use.
- It never runs code from a remote source.

## Permissions, and why each is needed

- **Canvas sites (`*.instructure.com`)** — to draw the new interface on Canvas pages and read Canvas's API for them.
- **Optional access to a site you choose** — schools often host Canvas at their own address (for example `canvas.university.edu`). The extension asks for that one site only when you click **Enable on this site**, and it can be revoked from the browser's extension settings.
- **`api.anthropic.com` / `api.openai.com`** — for the smart panel to reach the provider you chose with your own key. Unused unless you add a key.
- **`storage`** — to keep the settings and cache described above in your browser.
- **`scripting`** — to register the interface on a site you added with **Enable on this site**.
- **`activeTab`** — so the toolbar popup can tell which site is open and whether the extension is enabled there.

## Changes and contact

Changes to this policy are published in this file in the project's repository, with the date above updated. Questions can be raised as an issue on the repository: https://github.com/26VirenS/BetterCourseViewer/issues
