// The window follows one state at a time: on, off, missing (Safari has never heard of the
// extension) or unknown (the answer has not come back yet). ViewController.swift calls show().
// Everything is addressed by id: the body carries the state class names too, so a lookup by class
// name can return the body first and writing to it would empty the window.
const STATES = ['on', 'off', 'missing'];

function show(state, useSettingsInsteadOfPreferences, detail) {
    if (useSettingsInsteadOfPreferences) { // macOS 13 and later call them Settings
        document.getElementById('line-on').innerText = "Simpl Courses is on. You can turn it off in the Extensions section of Safari Settings. Open your Canvas site — the setup page opens by itself the first time.";
        document.getElementById('line-off').innerText = "Simpl Courses is off. Turn it on in the Extensions section of Safari Settings, and the setup page opens by itself.";
        document.getElementById('open-preferences').innerText = "Quit and Open Safari Settings…";
    }

    document.body.className = `state-${STATES.indexOf(state) === -1 ? 'unknown' : state}`;
    document.getElementById('detail').innerText = detail || '';
}

function openPreferences() {
    webkit.messageHandlers.controller.postMessage("open-preferences");
}

document.getElementById('open-preferences').addEventListener("click", openPreferences);
