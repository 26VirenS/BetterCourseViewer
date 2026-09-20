//
//  AppDelegate.swift
//  Simpl Courses
//
//  The app is the extension's home on a Mac: its settings window is where Simpl Courses is set up
//  and controlled, and it stays running — a small mark in the menu bar — so it can look for an
//  update every hour on its own. Closing the window keeps it running; the menu bar mark brings the
//  window back, and Quit is there too. Opened at login unless that is turned off in the window.
//  The extension asks for the window through the simplcourses:// address.
//

import Cocoa
import ServiceManagement

@main
class AppDelegate: NSObject, NSApplicationDelegate {

    private var statusItem: NSStatusItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        makeStatusItem()
        NotificationCenter.default.addObserver(self, selector: #selector(windowClosed(_:)), name: NSWindow.willCloseNotification, object: nil)
        LoginItem.applyDefaultOnce()
        Updater.shared.start()
    }

    /// The hourly check needs the app running: the window closing does not end it.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return false
    }

    /// The Dock icon, or Launchpad, or `open` again: the window.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showWindow()
        return true
    }

    /// simplcourses://settings — the extension's way of asking for the window (Settings in its popup,
    /// the account panel on a Canvas page).
    func application(_ application: NSApplication, open urls: [URL]) {
        showWindow()
    }

    // ---- the window ------------------------------------------------------------------------------

    func showWindow() {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
        if let window = NSApp.windows.first(where: { $0.contentViewController is ViewController }) {
            window.makeKeyAndOrderFront(nil)
        }
    }

    @objc private func windowClosed(_ note: Notification) {
        guard let window = note.object as? NSWindow, window.contentViewController is ViewController else { return }
        // out of the Dock and the app switcher while it only keeps watch; the menu bar mark stays
        DispatchQueue.main.async { NSApp.setActivationPolicy(.accessory) }
    }

    // ---- the menu bar ----------------------------------------------------------------------------

    private func makeStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = item.button {
            let image = NSImage(systemSymbolName: "book.closed.fill", accessibilityDescription: "Simpl Courses")
            image?.isTemplate = true
            button.image = image
            button.toolTip = "Simpl Courses"
        }
        let menu = NSMenu()
        menu.addItem(withTitle: "Open Simpl Courses…", action: #selector(openFromMenu), keyEquivalent: "")
        menu.addItem(withTitle: "Check for Updates…", action: #selector(checkFromMenu), keyEquivalent: "")
        menu.addItem(NSMenuItem.separator())
        menu.addItem(withTitle: "Quit Simpl Courses", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        item.menu = menu
        statusItem = item
    }

    @objc private func openFromMenu() {
        showWindow()
    }

    @objc private func checkFromMenu() {
        Updater.shared.check()
        showWindow()
    }
}

/// Open at login, through the system's own login items (System Settings → General → Login Items
/// lists it, and can turn it off there too). On by default: the hourly check needs the app up.
enum LoginItem {
    static var enabled: Bool {
        if #available(macOS 13.0, *) { return SMAppService.mainApp.status == .enabled }
        return false
    }

    @discardableResult
    static func set(_ on: Bool) -> String? {
        guard #available(macOS 13.0, *) else { return "Open at login needs macOS 13 or later." }
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            UserDefaults.standard.set(on, forKey: "openAtLogin")
            return nil
        } catch {
            return error.localizedDescription
        }
    }

    /// The first launch registers the app once; from then on the window's switch (and System Settings) decide.
    static func applyDefaultOnce() {
        let d = UserDefaults.standard
        if d.bool(forKey: "loginItemOffered") { return }
        d.set(true, forKey: "loginItemOffered")
        set(true)
    }
}
