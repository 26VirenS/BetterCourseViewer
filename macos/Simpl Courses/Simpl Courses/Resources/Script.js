// The window follows one state at a time: on, off, missing (Safari has never heard of the
// extension) or unknown (the answer has not come back yet). ViewController.swift calls show().
function show(state, useSettingsInsteadOfPreferences, detail) {
    if (useSettingsInsteadOfPreferences) {
        document.getElementsByClassName('state-on')[0].innerText = "Simpl Courses’s extension is currently on. You can turn it off in the Extensions section of Safari Settings.";
        document.getElementsByClassName('state-off')[0].innerText = "Simpl Courses’s extension is currently off. You can turn it on in the Extensions section of Safari Settings.";
        document.getElementsByClassName('open-preferences')[0].innerText = "Quit and Open Safari Settings…";
    }

    const known = ['on', 'off', 'missing'];
    document.body.className = `state-${known.indexOf(state) === -1 ? 'unknown' : state}`;
    document.querySelector('.detail').innerText = detail || '';
}

function openPreferences() {
    webkit.messageHandlers.controller.postMessage("open-preferences");
}

document.querySelector("button.open-preferences").addEventListener("click", openPreferences);

// The app explains what goes and hands its bundled uninstaller to the Terminal (see AppDelegate.swift).
document.querySelector("button.uninstall").addEventListener("click", () => {
    webkit.messageHandlers.controller.postMessage("uninstall");
});
