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

/// The whole uninstall. This app runs in the App Sandbox, so it can neither reach the storage Safari
/// keeps for the extension nor start the Terminal itself (and a script it writes is quarantined, which
/// is what "damaged and can't be opened" means). So it hands over the one line that does the work: the
/// uninstaller inside this bundle, run by the Terminal, which is outside the sandbox. The script takes
/// this app's identifier and path from where it sits, so the pasted line stays short.
enum Uninstaller {
    static func run() {
        guard let script = Bundle.main.url(forResource: "uninstall-mac", withExtension: "command") else {
            missing()
            return
        }
        let command = "/bin/bash \(quoted(script.path))"
        let alert = NSAlert()
        alert.messageText = "Uninstall Simpl Courses?"
        alert.informativeText = """
        The uninstaller runs in the Terminal, where it can reach everything this app cannot. It quits Safari, then shows each item as it removes it: the settings, keys and grade history Safari keeps for the extension, this app's own data and preferences, and the app itself, which is moved to the Trash.

        Nothing on Canvas is touched. Tip: press Reset everything in the extension's settings first, so the small note kept on open Canvas tabs goes too.
        """
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Copy the Command")
        alert.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertFirstButtonReturn else { return }

        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(command, forType: .string)

        let next = NSAlert()
        next.messageText = "Copied. Paste it into the Terminal"
        next.informativeText = """
        1. Open the Terminal: press ⌘Space, type Terminal, press Return.
        2. Paste with ⌘V and press Return.

        The command is:
        \(command)
        """
        next.addButton(withTitle: "Quit Simpl Courses")
        next.addButton(withTitle: "Leave It Open")
        if next.runModal() == .alertFirstButtonReturn {
            NSApp.terminate(nil) // out of the uninstaller's way; the Terminal has the command
        }
    }

    private static func missing() {
        let alert = NSAlert()
        alert.messageText = "The uninstaller is missing from this copy of the app"
        alert.informativeText = "Download uninstall-simpl-courses-mac.command from the Simpl Courses releases on GitHub, then run it in the Terminal with:\n\nbash ~/Downloads/uninstall-simpl-courses-mac.command"
        alert.runModal()
    }

    private static func quoted(_ s: String) -> String {
        return "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }
}
