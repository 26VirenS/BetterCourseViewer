//
//  AppDelegate.swift
//  Simpl Courses
//
//  Created by Viren Sharma on 9/10/26.
//

import Cocoa

@main
class AppDelegate: NSObject, NSApplicationDelegate {

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Override point for customization after application launch.
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }

    /// Simpl Courses → Uninstall Simpl Courses… (also the button on the window).
    @IBAction func uninstallSimplCourses(_ sender: Any?) {
        Uninstaller.run()
    }

}

/// The whole uninstall: this app is sandboxed, so it cannot reach the storage Safari keeps for the
/// extension itself. It explains what goes, then hands the bundled uninstaller script to the Terminal
/// (which runs outside the sandbox and shows each item as it is removed) and quits.
enum Uninstaller {
    static func run() {
        let alert = NSAlert()
        alert.messageText = "Uninstall Simpl Courses?"
        alert.informativeText = """
        Safari is asked to quit, then the Terminal opens and runs the uninstaller. It shows each item as it goes: the settings, keys and grade history Safari keeps for the extension, this app's own data and preferences, and the app itself, which is moved to the Trash.

        Nothing on Canvas is touched. Tip: press Reset everything in the extension's settings first, so the small note kept on open Canvas tabs goes too.
        """
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Open the Uninstaller")
        alert.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        do {
            try launch()
        } catch {
            failed(error)
        }
    }

    private static func failed(_ error: Error) {
        let alert = NSAlert()
        alert.messageText = "The uninstaller could not be started"
        alert.informativeText = "\(error.localizedDescription)\n\nRun it by hand instead: download uninstall-simpl-courses-mac.command from the release page on GitHub and open it (right-click → Open the first time)."
        alert.runModal()
    }

    /// A tiny launcher is written to this app's temporary folder and opened with the Terminal. It runs the
    /// script from inside the bundle with this app's identifier and path, so the Trash step finds this copy
    /// even when the app was never put in Applications.
    private static func launch() throws {
        guard let script = Bundle.main.url(forResource: "uninstall-mac", withExtension: "command") else {
            throw NSError(domain: "SimplCourses", code: 1, userInfo: [NSLocalizedDescriptionKey: "The uninstaller is missing from this copy of the app."])
        }
        let appID = Bundle.main.bundleIdentifier ?? "com.simplcourses.app"
        let launcher = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("Uninstall Simpl Courses.command")
        let body = """
        #!/bin/bash
        export SIMPL_APP_ID=\(quoted(appID))
        export SIMPL_APP_PATH=\(quoted(Bundle.main.bundlePath))
        exec /bin/bash \(quoted(script.path))

        """
        try body.write(to: launcher, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: launcher.path)
        let terminal = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.apple.Terminal")
            ?? URL(fileURLWithPath: "/System/Applications/Utilities/Terminal.app")
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        NSWorkspace.shared.open([launcher], withApplicationAt: terminal, configuration: configuration) { _, error in
            DispatchQueue.main.async {
                if let error = error {
                    failed(error)
                } else {
                    NSApp.terminate(nil) // the script takes it from here (and moves this app to the Trash)
                }
            }
        }
    }

    private static func quoted(_ s: String) -> String {
        return "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }
}
