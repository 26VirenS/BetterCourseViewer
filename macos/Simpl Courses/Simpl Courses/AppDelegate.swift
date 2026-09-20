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
import CoreServices
import ServiceManagement

@main
class AppDelegate: NSObject, NSApplicationDelegate {

    private var statusItem: NSStatusItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        Placement.register()
        makeStatusItem()
        NotificationCenter.default.addObserver(self, selector: #selector(windowClosed(_:)), name: NSWindow.willCloseNotification, object: nil)
        LoginItem.applyDefaultOnce()
        Updater.shared.start()
        if !ViewController.welcomeDue(SharedStore()) { Placement.offerMoveIfNeeded() } // (the first screen's button does the move)
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

    /// The first launch from a place the app can stay registers it, and so does the first launch
    /// after a move (the system's record names the path). Turned off in the window, it stays off;
    /// from then on the window's switch (and System Settings) decide.
    static func applyDefaultOnce() {
        guard !Placement.isTranslocated else { return } // registered from the temporary copy, the record would point there
        let d = UserDefaults.standard
        let here = Placement.originalURL.path
        guard d.string(forKey: "loginItemOfferedAt") != here else { return }
        d.set(here, forKey: "loginItemOfferedAt")
        if d.object(forKey: "openAtLogin") as? Bool == false { return }
        set(true)
    }
}

/// Where the app is, and whether Safari can use its extension from there. A downloaded app opened
/// straight from Downloads is run by macOS from a hidden, read-only, temporary copy (App
/// Translocation), and Safari cannot see an extension inside such a copy — on a Mac that has the
/// app open, that is the one thing that makes the extension "missing". The app's home is the
/// Applications folder, where Safari finds it and where it can replace itself with an update; the
/// app offers to move itself there, and can.
enum Placement {
    private typealias IsTranslocatedFn = @convention(c) (CFURL, UnsafeMutablePointer<Bool>?, UnsafeMutablePointer<Unmanaged<CFError>?>?) -> Bool
    private typealias OriginalPathFn = @convention(c) (CFURL, UnsafeMutablePointer<Unmanaged<CFError>?>?) -> Unmanaged<CFURL>?
    private static let RTLD_DEFAULT = UnsafeMutableRawPointer(bitPattern: -2)

    /// This running copy — the temporary one, when macOS made one.
    static let runningURL = Bundle.main.bundleURL

    /// macOS is running this copy from its temporary mount.
    static let isTranslocated: Bool = {
        if runningURL.path.contains("/AppTranslocation/") { return true }
        guard let sym = dlsym(RTLD_DEFAULT, "SecTranslocateIsTranslocatedURL") else { return false }
        var flag = false
        return unsafeBitCast(sym, to: IsTranslocatedFn.self)(runningURL as CFURL, &flag, nil) && flag
    }()

    /// The app as the user sees it: the copy in Downloads that macOS made the temporary one from, or this copy.
    static let originalURL: URL = {
        guard isTranslocated, let sym = dlsym(RTLD_DEFAULT, "SecTranslocateCreateOriginalPathForURL"),
              let original = unsafeBitCast(sym, to: OriginalPathFn.self)(runningURL as CFURL, nil)?.takeRetainedValue() else { return runningURL }
        return original as URL
    }()

    /// In /Applications, or the user's own Applications folder.
    static var isInApplications: Bool {
        let folder = originalURL.deletingLastPathComponent().standardizedFileURL.path
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        return ["/Applications", "\(home)/Applications"].contains { folder == $0 || folder.hasPrefix($0 + "/") }
    }

    static var needsMove: Bool { isTranslocated || !isInApplications }

    /// For the window: why the extension may be missing, and the button to put it right.
    static var report: [String: Any] {
        ["translocated": isTranslocated, "inApplications": isInApplications, "path": originalURL.path]
    }

    /// Makes sure LaunchServices has this copy — and so its extension — on record.
    static func register() {
        _ = LSRegisterURL(originalURL as CFURL, true)
    }

    /// On a launch from the wrong place, the offer to move: once from a folder the app works from,
    /// every time while translocated, since nothing works from there.
    static func offerMoveIfNeeded() {
        guard needsMove else { return }
        let d = UserDefaults.standard
        if !isTranslocated && d.bool(forKey: "moveOffered") { return }
        d.set(true, forKey: "moveOffered")
        let alert = NSAlert()
        alert.messageText = "Move Simpl Courses to the Applications folder?"
        alert.informativeText = isTranslocated
            ? "macOS is running this copy from a temporary place, and Safari cannot see the extension inside it. From the Applications folder, Safari finds it and the app can update itself."
            : "Safari reads the extension out of the app, and updates replace the app in place, so it belongs in the Applications folder."
        alert.addButton(withTitle: "Move to Applications")
        alert.addButton(withTitle: "Not Now")
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        do {
            try moveToApplications()
        } catch {
            let failed = NSAlert()
            failed.messageText = "Simpl Courses could not move itself"
            failed.informativeText = "\(error.localizedDescription) Drag it to the Applications folder in the Finder, then open it again."
            failed.runModal()
        }
    }

    /// A copy in the Applications folder (the user's own when the shared one cannot be written), free
    /// of the download's quarantine mark so macOS runs it in place, the copy the user opened to the
    /// Trash, and the new one opened as this one quits.
    @discardableResult
    static func moveToApplications() throws -> URL {
        let fm = FileManager.default
        var folder = URL(fileURLWithPath: "/Applications", isDirectory: true)
        if !fm.isWritableFile(atPath: folder.path) {
            folder = fm.homeDirectoryForCurrentUser.appendingPathComponent("Applications", isDirectory: true)
            try fm.createDirectory(at: folder, withIntermediateDirectories: true)
        }
        let dest = folder.appendingPathComponent(originalURL.lastPathComponent)
        if dest.standardizedFileURL.path == originalURL.standardizedFileURL.path {
            guard isTranslocated else { return dest }
            // already in Applications, put there by something other than the Finder: the quarantine mark is all that is wrong
            Shell.run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", dest.path])
            _ = LSRegisterURL(dest as CFURL, true)
            relaunch(dest)
            return dest
        }
        if fm.fileExists(atPath: dest.path) { try fm.trashItem(at: dest, resultingItemURL: nil) } // an older copy
        try fm.copyItem(at: runningURL, to: dest)
        Shell.run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", dest.path])
        try? fm.trashItem(at: originalURL, resultingItemURL: nil) // (when the original is not known, the temporary copy cannot be trashed: it stays)
        _ = LSRegisterURL(dest as CFURL, true)
        relaunch(dest)
        return dest
    }

    /// The copy at `url` opens once this one has quit (-n: started afresh rather than this one found).
    static func relaunch(_ url: URL) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", "sleep 1; /usr/bin/open -n \"$0\"", url.path]
        try? p.run()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { NSApp.terminate(nil) }
    }
}

/// A command run to its end, quietly; its exit status (-1 when it could not start).
enum Shell {
    @discardableResult
    static func run(_ path: String, _ arguments: [String]) -> Int32 {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = arguments
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return -1 }
        p.waitUntilExit()
        return p.terminationStatus
    }
}
