**What's new in 0.3.24** (beta)

- **Tools open straight away** — some tools (Smartwork among them) can't be opened by Canvas's one-time launch, so Open Tool showed Canvas's own page for the tool with a "Load … in a new window" button. Simpl now presses on with that launch itself, in the same window, so the tool opens without the extra click.

**What's new in 0.3.23** (beta)

- **Files open again** — a file linked in an assignment's instructions (or in an announcement, a discussion or a page) now opens the way a module's file does: Simpl asks your school for that file by its id and opens it under its real name, rather than fetching an address made from the link, which at some schools gave nothing or a file named "download"; when the school will not say, the link's own key is kept on the address it fetches.
- **Alta's maths drawn whole** — between questions, Simpl no longer hides Alta's next question so completely that its answer box measures its brackets and root signs at zero size; they now show right away, with no need to switch to Light and back to Dark. A question whose page changes without Simpl noticing is now shown anyway, and if a question is ever still out of sight after a while, Simpl shows Alta's page as it is.

**What's new in 0.3.22** (beta)

- **Critical updates** — Simpl now looks for a critical update every five minutes while it runs (and as the PC wakes from sleep), not only once an hour, so one reaches you within minutes; the look reads one small file from simplcourses.com rather than asking GitHub, so it never uses up a shared school network's allowance of GitHub requests, shows nothing unless a critical update is found, and other updates still come with the hourly look.

**What's new in 0.3.21** (beta)

- **Faster Alta** — a Knewton Alta assignment opens noticeably faster: its launch is asked of your school at once, while the popup and its page are still being made (before, only once they were ready), its due date comes from the screen you opened it from instead of a second request, and its question shows as soon as it has finished drawing — its answer box and its maths in, then a tenth of a second still, where it waited 350 ms — however much the rest of Alta's page is still moving.

**What's new in 0.3.20** (beta)

- **Alta verdict** — Correct or Incorrect beside Continue shows only once Alta has said so in its own words; 0.3.19 could show Correct on a question not yet answered.

**What's new in 0.3.19** (beta)

- **Alta verdict** — when Knewton Alta says a question was right or wrong, the popup shows Correct (green) or Incorrect (red) beside its own buttons, until the next question.

**What's new in 0.3.18** (beta)

- **Larger Alta graphs** — a graph in what a question asks is drawn twice as large as before (up to 600 × 440 instead of 300 × 220); Enlarge beside it still opens it large.

**What's new in 0.3.17** (beta)

- **Module files open** — a file in a module opens as one from the Files screen does (by the file's own record, in the preview), where before the module's own address was fetched and could come back with nothing.
- **Rubric ring** — one band of colour, without the dark seams between criteria; and on an open criterion each level sits beside the bar at the height of its points, moved only where two would overlap.

**What's new in 0.3.16** (beta)

- **Study planner order** — when two pieces of work share a deadline day (finishing a day early put one due tonight and one due tomorrow night on the same day), the one due sooner now takes the day's first blocks; before, the heavier one took the whole day and the one due tonight was said not to fit.

**What's new in 0.3.15** (beta)

- **Verb conjugations** — a new Study tool: type a verb in Spanish, French, German, Italian or Portuguese and every main tense is laid out with its pronouns — regular verbs by rule, the most common irregular ones from a table, stem and spelling changes included — with a switch that tints the irregular forms, suggestions as you type, and Copy for a table; `/verbs` in the search box, and the present tense as a pin.

**What's new in 0.3.14** (beta)

- **Circuit Builder** — the Physics section's third tool: place batteries, resistors, wires, switches, lamps, meters and a ground on a grid, join their terminals, and the circuit is solved as you build it — current arrows, lamps lit by their power, every part's V, I and P in a readout, and plain words when it can't be solved; quick builds (series, parallel, a voltage divider), save and load, Export PNG, and an Ohm's law and series/parallel strip; `/circuit` in the search box, and the strip as a pin.

**What's new in 0.3.13** (beta)

- **Solutions & pH** — a new Chemistry tool with four tabs: Molarity (any two of amount, volume and molarity give the third, with mass % and ppm), Dilution (M1V1 = M2V2, "How much stock?"), pH (any one of pH, pOH, [H⁺] and [OH⁻] gives the others; strong and weak acids and bases from a concentration, the weak ones solved exactly with the % ionized, on a 0–14 colour strip) and Titration (the equivalence volume, the pH at the start, half-way, at equivalence and after, the curve drawn, and a Henderson–Hasselbalch buffer); `/ph` in the search box, and a pin.

**What's new in 0.3.12** (beta)

- **Molar Mass & Stoichiometry** — a new Chemistry tool with four tabs: Molar mass (a formula's mass with each element's count, mass and percent), Convert (grams, moles, molecules and litres at STP from any one), Reaction (a balanced equation with what you have of each reactant gives the limiting reagent, every product's yield, what is left over and the percent yield) and Empirical (mass percents to an empirical and molecular formula); `/moles` in the search box, and a pin.
- **Gas Laws** — a new Chemistry tool: PV = nRT with units for each field, leave one blank and it is solved; the combined law with Boyle, Charles and Gay-Lussac chips; a piston drawing that follows the volume, the amount and the temperature; `/gas` in the search box, and a pin.

**What's new in 0.3.11** (beta)

- **Compress files** — a new Files tool: drop in pictures and PDFs and they are made small enough to upload — Max quality, Balanced, Smallest or Fit under a size in MB — saved to Downloads as `<name>-smaller`, never over the original, with a before-and-after table and a preview; `/compress` in the search box, and a pin.
- **Study planner** — a new Study tool: the work due in the next six weeks shared out as study blocks over the days before each is due, weighted by points and kind (an exam most), with your hours per weekday, block length and preferred time of day; a week view and a list, blocks marked done, Regenerate, and Add to Calendar as an .ics file; `/plan` in the search box, and today's blocks as a pin.

**What's new in 0.3.10** (beta)

- **Word counter** — a new Study tool: paste or type an essay and it is counted as it changes — words, characters with and without spaces, sentences, paragraphs, reading and speaking time, the longest word, a word target with "to go" and the ten words used most; `/words` in the search box, and a pin.
- **Statistics** — a new Math & science tool with four pages: Describe (mean, median, modes, quartiles, SD and variance, outliers, a histogram and a box plot), Regression (slope, intercept, r and r², a scatter with the line, predict y), Distributions (normal and t, with the curve shaded) and Tests (one-sample, two-sample and paired t-tests with a 95 % interval); `/stats` in the search box, and a pin.
- **Matrix calculator** — a new Math & science tool: A and B up to 6×6, with A + B, A − B, A × B, kA, the transpose, determinant, inverse, rank, RREF with its steps, trace, A·x = b, eigenvalues (real and complex) and Aⁿ, as exact fractions when the cells are; `/matrix` in the search box, and a pin.

**What's new in 0.3.9** (beta)

- **Physics** — a new section on the Tools page, between Chemistry and Biology.
- **Kinematics & Projectiles** — 1D motion: fill any three of Δx, v₀, v, a and t and the other two are solved, with the equation used and position and velocity graphs; projectiles: speed, angle and height give the time of flight, range, highest point and the impact, drawn and animated, with the two angles that hit a target; `/motion` in the search box, and a pin.
- **Vectors** — up to six vectors by components or by magnitude and angle, in 2D or 3D: magnitudes, the resultant, dot and cross products, the angle between, unit vectors and projections, drawn; `/vectors` in the search box, and a pin.

**What's new in 0.3.8** (beta)

- **Equation balancer** — a new Chemistry tool: type any chemical equation (phases, charges, brackets, hydrates) and it is balanced as you type, with each element's count on both sides, a mass check and plain words when it can't be balanced; `/balance` in the search box, and a pin.

**What's new in 0.3.7** (beta)

- **Always up to date** — updates always install by themselves, at a quiet moment (never during a quiz, a tool or a dialog); the Install updates automatically switch is gone, and anyone who had it off gets updates by themselves again.
- **Earlier versions stay** — after you install an earlier version in **Settings ▸ Updates ▸ Earlier versions**, Simpl stays on it: the version you left doesn't come back by itself, a newer one does, and **Update now** installs the newest whenever you choose.

**What's new in 0.3.6** (beta)

- **Dark mode for Alta** — a Light/Dark choice above the objectives in Knewton Alta's popup, remembered.
- **Show browser** — Knewton Alta's own page on the right, beside your objectives.
- **Alta pop-ups in full** — Alta's own pop-ups show their whole message: words, lists, formulas and pictures; one that can't be read is shown in Alta's page, never empty.

**What's new in 0.3.5** (beta)

- **Open pins full screen** — the button at the top of a pinned tool's flyout opens the tool full screen (its popup across the window), not in a floating window.
- **Pins keep your work** — what you began in the flyout (a graph, numbers, a search) carries over to the full tool.

**What's new in 0.3.4** (beta)

- **Popups in the middle** — What's New, Hand In, a new message and every other popup open in the middle of the window, not of the page beside the pane.

**What's new in 0.3.3** (beta)

- **Cleaner counter cards** — the Dashboard's counter cards lose their coloured border; each keeps its own soft colour.

**What's new in 0.3.2** (beta)

- **Chemistry tools** — the Periodic Table, Lewis Structures, Molecule Builder and Atoms & Orbitals, each its own tool in a new Chemistry section.
- **Live updates** — an open screen checks for new work every minute while Simpl is in view; course pages every 10 minutes.
- **Dashboard cards** — the counter cards grow into their lists again and fold back, are as tall as what is on them, and each wears its own colour.
- **Wider tool popups** — a tool opens across the whole window, over the pane; drag an edge to resize it, and it remembers the size.
- **Only what changed** — the Dashboard updates just the counters, cards and rows that changed, nothing else moves.
- **Messages stay unread** — a check never marks a message or reply read while you are away, and nothing is checked while the PC is locked.

**What's new in 0.3.1**

- **More from Canvas** — History, Help and your school's own tools in the sidebar.
- **Beta updates** — turn on **Settings ▸ Updates ▸ Get beta updates** to try new versions first; everyone else gets only the regular ones.
- **Tools in popups** — tools open over your work and never block the app (only a quiz does).
- **Biology tools, fixed** — controls and facts in sight at every window size, models full size, colours right from the start.
- **Counters as before** — square, coloured counters and the old counter panels and previews are back.
- **Tools by provider** — external tools grouped by their site (Macmillan Learning's links on one card), and their pages in light mode.

**What's new in 0.3.0**

- **A biology toolkit** — ten new tools, from a 3D cell to genetic crosses.
- **Send from phone** — scan a code to hand in photos; nothing leaves your Wi-Fi.
- **Clearer screens** — grades, dates and courses read one way everywhere.
- **Updates on their own** — new versions install themselves when you're not busy.
- **Lighter and faster** — much less memory when minimized; screens open 2–3x faster.
- **Fewer PIN prompts** — Windows Hello asks once, not every time.

Coming from 0.2.9 or earlier, update once from **Settings ▸ Updates** (Download, then Restart to Update); from 0.3.0 on, Simpl installs each new version by itself.

**Download Simpl-Setup.exe and open it.** It installs Simpl for you alone — no administrator needed — with a Start menu shortcut (and a desktop one if you tick it), then opens it. Simpl-Setup.exe is not code-signed yet: on "Windows protected your PC", choose **More info → Run anyway**. Once installed, Simpl updates itself — each new version downloads and goes in at a quiet moment, never over a quiz, a hand-in or a dialog (**Settings ▸ Updates** can turn that off, or put one in now with Restart to Update) — and you can uninstall it from Simpl's own **Settings ▸ General ▸ Uninstall Simpl** — where you can choose to erase your settings, grade history and saved sign-in too — or from Windows' **Settings ▸ Apps**, which keeps your settings and sign-in unless you erase them in Simpl first.

**Simpl-Windows-x64.zip** — the portable choice: unzip anywhere and run **Simpl.exe** (everything it needs is in the folder; it uses Microsoft Edge WebView2, which Windows 10 and 11 already have). The same "More info → Run anyway" applies.

**Simpl for Windows, made again in Windows 11's look — an early preview.** A test build for evaluation, not a release: the Mac app's updates and the first Windows preview's never offer it (this one's own Settings ▸ Updates offers the next Windows 11-look preview). The first Windows preview (windows-app-v…) stays available.

What is in it — the Mac app's screens and steps, in Windows 11's look:

- **Windows 11's own look**: Mica behind the window, rounded corners, the title bar with Back and snap layouts, Segoe UI Variable and Fluent icons, Windows 11's buttons, fields, toggles, cards and dialogs (the window dims behind each one), and Windows' motion;
- **finding your school and signing in, as on the Mac**: search for your school, see its sign-in page and confirm it, then sign in with Simpl's own card; "Logging you in", the note over a page you must finish yourself, Start Over, and "Stay logged in?" (kept in Windows' Credential Manager);
- **the app laid out like Windows 11 Settings**: the search box with its suggestions and commands (type / or press Ctrl+K), Dashboard, To Do, Calendar, Grades, Notifications, Inbox, Tools, your courses with their sections, your groups, and your account at the foot of the pane (Settings is in its menu); the page's title as a breadcrumb; Ctrl+1 to Ctrl+7 for the places;
- **every screen drawn by Simpl**: the Dashboard with its cards that open, To Do and New Task, the Calendar (month, week, list), Notifications, Grades with What-If, Inbox and New Message, Groups, All Courses, a course's home and every section, pages and files, an assignment with Hand In, its feedback and its rubric, Search, External Tools;
- **a quick look at any piece of work**: click a row and a card grows out of it; double-click to open it whole;
- **quizzes, Knewton Alta and Macmillan in their own popups**, as on the Mac;
- **the tools**: Citation generator, Focus timer, Calculator, Graphing calculator, the Periodic Table (with the electron cloud, the Lewis structure maker and the 3D molecule builder), Grade needed, File converter, Unit converter, Merge & split PDFs, PDF annotator, Image to text, Flashcards, and the biology tools (Cell Explorer, Gene Expression, Biomolecule Builder, DNA & RNA, Cell Division, Genetics Crosses, Hardy–Weinberg, Bio Lab Calculator, Enzyme Lab, Photosynthesis & Respiration) — each opens over the window, and any of them can be pinned to the title bar for its quick version;
- **Settings laid out like Windows 11's Settings app**, Guided Setup, What's New, the guided tour, themes and photos, reminders and new-activity alerts;
- **an installer**: Simpl-Setup.exe installs it for you alone, and updates it quietly by itself (or when you choose Restart to Update) — Simpl opens again on its own, where you were;
- **on your taskbar**: after the guided tour, Simpl asks once to be kept on the taskbar (and Settings ▸ General has Pin to taskbar… any time);
- **runs in the background**: closing the window keeps Simpl in the notification area (right-click its icon to quit);
- **the window's size**: it opens as you left it, never larger than your screen.
