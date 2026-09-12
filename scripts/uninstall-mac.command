#!/bin/bash
# Uninstall Simpl Courses (Mac): the app and everything it stored, gone in one go.
#
# Run it in the Terminal:  bash ~/Downloads/uninstall-simpl-courses-mac.command
# (a downloaded script is quarantined by macOS, so double-clicking it in the Finder is refused —
# "damaged and can't be opened" — while the Terminal runs it happily).
# The app carries a copy: Simpl Courses → Uninstall Simpl Courses… puts the line that runs it on
# the clipboard, ready to paste into the Terminal.
#
# Deleting the app on its own leaves the extension's storage behind inside Safari (settings, keys,
# grade history), which is what this script is for. It removes, showing each item as it goes:
#   • the extension's storage and caches kept by Safari (and Safari Technology Preview)
#   • the app's and the extension's own data: containers, preferences, caches, saved state, logs
#   • the Simpl Courses app itself, moved to the Trash
# It never touches Canvas, your Canvas account, Safari's history or bookmarks, or other extensions.
#
# Options:  --dry-run   list what would go, remove nothing
#           --yes       skip the confirmation
#           --keep-app  remove the data only, leave the app where it is
# Environment (rarely needed; the copy inside the app fills both in from where it sits):
#   SIMPL_APP_ID (bundle identifier, default com.simplcourses.app), SIMPL_APP_PATH (the app to
#   remove, when it is not in a usual place).

# The copy inside the app: it sits in the folder it is about to move to the Trash, so it takes the
# app's identifier and path from where it sits, copies itself to the temporary folder, and runs from
# there. Nothing it deletes can then pull the ground from under it.
SELF="${BASH_SOURCE[0]}"
case "$SELF" in /*) ;; *) SELF="$PWD/$SELF" ;; esac
if [[ -z "${SIMPL_RELAUNCHED:-}" && "$SELF" == *.app/Contents/Resources/* ]]; then
  BUNDLE="${SELF%/Contents/Resources/*}"
  COPY="${TMPDIR:-/tmp}/simpl-courses-uninstall-$$.command"
  if ! cp "$SELF" "$COPY" 2>/dev/null; then
    echo "The uninstaller could not be copied to a temporary folder." >&2
    exit 1
  fi
  chmod +x "$COPY" 2>/dev/null
  export SIMPL_RELAUNCHED=1
  export SIMPL_APP_PATH="${SIMPL_APP_PATH:-$BUNDLE}"
  if [[ -z "${SIMPL_APP_ID:-}" && -f "$BUNDLE/Contents/Info.plist" ]]; then
    FOUND_ID="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$BUNDLE/Contents/Info.plist" 2>/dev/null)"
    [[ -n "$FOUND_ID" ]] && export SIMPL_APP_ID="$FOUND_ID"
  fi
  exec /bin/bash "$COPY" "$@" # exec keeps the process id, so the copy cleans itself up below
fi
[[ -n "${SIMPL_RELAUNCHED:-}" ]] && trap 'rm -f "${TMPDIR:-/tmp}/simpl-courses-uninstall-$$.command"' EXIT

APP_NAME="Simpl Courses"
APP_ID="${SIMPL_APP_ID:-com.simplcourses.app}"
EXT_ID="$APP_ID.Extension"

DRY_RUN=0
YES=0
KEEP_APP=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=1 ;;
    --yes|-y) YES=1 ;;
    --keep-app) KEEP_APP=1 ;;
    -h|--help) sed -n '2,22p' "$SELF"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

LIB="$HOME/Library"
FOUND_APPS=()     # app bundles to move to the Trash
FOUND_DATA=()     # files and folders to delete
FOUND_PREFS=()    # preference domains to delete (defaults delete + the plist)
REMOVED=0

say() { printf '%s\n' "$*"; }
have() { command -v "$1" >/dev/null 2>&1; }

# ---- what is on this Mac ----------------------------------------------------------------------------
bundle_id_of() { # the bundle identifier written in an app's Info.plist ("" when it has none)
  local plist="$1/Contents/Info.plist"
  [[ -f "$plist" ]] || return 0
  if have /usr/libexec/PlistBuddy; then /usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$plist" 2>/dev/null
  elif have defaults; then defaults read "$1/Contents/Info" CFBundleIdentifier 2>/dev/null
  else grep -A1 CFBundleIdentifier "$plist" 2>/dev/null | tail -1 | sed -E 's/.*<string>(.*)<\/string>.*/\1/'; fi
}
add_app() {
  local app="$1" known
  [[ -d "$app" ]] || return 0
  for known in "${FOUND_APPS[@]+"${FOUND_APPS[@]}"}"; do [[ "$known" == "$app" ]] && return 0; done
  [[ "$(bundle_id_of "$app")" == "$APP_ID" ]] || return 0 # a different app with the same name is not ours
  FOUND_APPS+=("$app")
}
add_data() {
  local p="$1" known
  [[ -e "$p" ]] || return 0
  for known in "${FOUND_DATA[@]+"${FOUND_DATA[@]}"}"; do [[ "$known" == "$p" ]] && return 0; done
  FOUND_DATA+=("$p")
}
find_apps() {
  [[ -n "${SIMPL_APP_PATH:-}" ]] && add_app "$SIMPL_APP_PATH"
  local dir app
  for dir in /Applications "$HOME/Applications" "$HOME/Desktop" "$HOME/Downloads"; do add_app "$dir/$APP_NAME.app"; done
  if have mdfind; then # Spotlight knows every copy, wherever it was dragged
    while IFS= read -r app; do [[ -n "$app" ]] && add_app "$app"; done < <(mdfind "kMDItemCFBundleIdentifier == '$APP_ID'" 2>/dev/null)
  fi
}
find_own_data() { # the app's and the extension's own folders, by bundle identifier
  local id
  for id in "$APP_ID" "$EXT_ID"; do
    add_data "$LIB/Containers/$id"
    add_data "$LIB/Application Support/$id"
    add_data "$LIB/Caches/$id"
    add_data "$LIB/Saved Application State/$id.savedState"
    add_data "$LIB/HTTPStorages/$id"
    add_data "$LIB/HTTPStorages/$id.binarycookies"
    add_data "$LIB/WebKit/$id"
    add_data "$LIB/Logs/$id"
    add_data "$LIB/Application Scripts/$id"
    if [[ -f "$LIB/Preferences/$id.plist" ]]; then FOUND_PREFS+=("$id"); fi
  done
  local p
  if [[ -d "$LIB/Group Containers" ]]; then # <team id>.com.simplcourses.app…
    while IFS= read -r p; do [[ -n "$p" ]] && add_data "$p"; done < <(find "$LIB/Group Containers" -maxdepth 1 -iname "*$APP_ID*" 2>/dev/null)
  fi
}
mentions_us() { # a file or folder that names the bundle identifier inside (text files and plists)
  local p="$1"
  if [[ -d "$p" ]]; then
    grep -rIlqs --exclude='*.db' --exclude='*.sqlite*' -- "$APP_ID" "$p" 2>/dev/null && return 0
    local plist
    while IFS= read -r plist; do
      [[ -n "$plist" ]] || continue
      if have plutil && plutil -p "$plist" 2>/dev/null | grep -qs -- "$APP_ID"; then return 0; fi
    done < <(find "$p" -maxdepth 2 -name '*.plist' 2>/dev/null)
    return 1
  fi
  grep -Iqs -- "$APP_ID" "$p" 2>/dev/null
}
find_safari_data() { # what Safari keeps for the extension: storage, caches, its record of the extension
  local container base root p
  for container in com.apple.Safari com.apple.SafariTechnologyPreview; do
    base="$LIB/Containers/$container/Data/Library"
    [[ -d "$base" ]] || continue
    # anything named after the extension, wherever Safari filed it
    for root in "$base/WebKit" "$base/Safari" "$base/Caches" "$base/Application Support"; do
      [[ -d "$root" ]] || continue
      while IFS= read -r p; do [[ -n "$p" ]] && add_data "$p"; done < <(find "$root" -maxdepth 5 -iname "*$APP_ID*" 2>/dev/null)
    done
    # per-extension storage folders Safari names by an identifier of its own: keep the ones that name us inside
    for root in "$base/WebKit/WebExtensions" "$base/Safari/WebExtensions" "$base/Application Support/WebExtensions"; do
      [[ -d "$root" ]] || continue
      for p in "$root"/*/; do
        p="${p%/}"
        [[ -d "$p" ]] || continue
        case "$p" in *"$APP_ID"*) continue ;; esac # already listed by name
        mentions_us "$p" && add_data "$p"
      done
    done
  done
  if [[ -d "$LIB/Safari" ]]; then # Safari's pre-container folder (older macOS)
    while IFS= read -r p; do [[ -n "$p" ]] && add_data "$p"; done < <(find "$LIB/Safari" -maxdepth 4 -iname "*$APP_ID*" 2>/dev/null)
  fi
}

# ---- removing -----------------------------------------------------------------------------------------
quit_app() { # ask an app to quit by name, and wait for it rather than pulling the rug out from under it
  local name="$1" i answer
  pgrep -xq "$name" 2>/dev/null || return 0
  say "  quitting $name…"
  # (macOS may ask to let the Terminal control $name; if that is refused, the wait below still works)
  if have osascript; then osascript -e "tell application \"$name\" to quit" >/dev/null 2>&1; fi
  for i in $(seq 1 20); do pgrep -xq "$name" 2>/dev/null || return 0; sleep 1; done
  while pgrep -xq "$name" 2>/dev/null; do
    if [[ "$YES" -eq 1 || ! -t 0 ]]; then # nobody to ask: end it, its windows come back on the next launch
      killall "$name" 2>/dev/null || true
      sleep 2
      return 0
    fi
    read -r -p "  $name is still open. Quit it and press Return (or type k to force it to quit): " answer
    case "$answer" in k|K) killall "$name" 2>/dev/null || true; sleep 2 ;; esac
  done
}
trash() { # move an app bundle to the Trash (a copy elsewhere is not taken with it)
  local app="$1" name dest
  name="$(basename "$app")"
  mkdir -p "$HOME/.Trash" 2>/dev/null
  dest="$HOME/.Trash/$name"
  [[ -e "$dest" ]] && dest="$HOME/.Trash/${name%.app} $(date +%H.%M.%S).app"
  if have /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister; then
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -u "$app" >/dev/null 2>&1 || true
  fi
  if mv "$app" "$dest" 2>/dev/null; then say "  → Trash: $app"; else rm -rf "$app" && say "  ✕ deleted: $app"; fi
}

main() {
  say "Uninstall $APP_NAME"
  say "Looking for $APP_NAME ($APP_ID) on this Mac…"
  find_apps
  find_own_data
  find_safari_data

  local total=$(( ${#FOUND_APPS[@]} + ${#FOUND_DATA[@]} + ${#FOUND_PREFS[@]} ))
  if [[ "$total" -eq 0 ]]; then
    say "Nothing of $APP_NAME was found: no app, and nothing stored by it or by Safari for it."
    exit 0
  fi
  say
  local p
  if [[ "$KEEP_APP" -eq 0 ]]; then for p in "${FOUND_APPS[@]+"${FOUND_APPS[@]}"}"; do say "  app     $p"; done; fi
  for p in "${FOUND_DATA[@]+"${FOUND_DATA[@]}"}"; do say "  data    $p"; done
  for p in "${FOUND_PREFS[@]+"${FOUND_PREFS[@]}"}"; do say "  prefs   $LIB/Preferences/$p.plist"; done
  say
  if [[ "$DRY_RUN" -eq 1 ]]; then say "Dry run: nothing was removed."; exit 0; fi
  if [[ "$YES" -eq 0 ]]; then
    local answer
    say "Safari is quit first (so it lets go of the extension's storage); the app goes to the Trash."
    read -r -p "Remove everything listed? [y/N] " answer
    case "$answer" in y|Y|yes|YES) ;; *) say "Nothing was removed."; exit 0 ;; esac
  fi

  say
  quit_app "Safari"
  quit_app "Safari Technology Preview"
  quit_app "$APP_NAME"

  local id
  for p in "${FOUND_DATA[@]+"${FOUND_DATA[@]}"}"; do
    rm -rf "$p" && { say "  ✕ $p"; REMOVED=$((REMOVED + 1)); }
  done
  for id in "${FOUND_PREFS[@]+"${FOUND_PREFS[@]}"}"; do
    have defaults && defaults delete "$id" >/dev/null 2>&1
    rm -f "$LIB/Preferences/$id.plist" && { say "  ✕ $LIB/Preferences/$id.plist"; REMOVED=$((REMOVED + 1)); }
  done
  if [[ "$KEEP_APP" -eq 0 ]]; then
    for p in "${FOUND_APPS[@]+"${FOUND_APPS[@]}"}"; do trash "$p"; REMOVED=$((REMOVED + 1)); done
  fi

  say
  say "Done: $REMOVED item(s) removed. Safari's Extensions list forgets $APP_NAME the next time Safari opens."
  say "One thing stays, and only if you want it gone too: a one-line note (the look and the appearance) that the"
  say "extension kept inside your Canvas site's own website data. Reset everything in the extension's settings"
  say "clears it on any open Canvas tab; otherwise Safari → Settings → Privacy → Manage Website Data → your"
  say "school's Canvas site removes it along with the site's own data (which signs you out of Canvas there)."
}

main "$@"
