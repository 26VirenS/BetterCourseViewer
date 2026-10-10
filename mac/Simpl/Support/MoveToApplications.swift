import AppKit
import CoreServices

/// (1.3.13) Opened from anywhere but an Applications folder — Downloads, the desktop, a disk image — Simpl offers, once
/// at launch, to move itself there: the copy goes into Applications (one already there to the Bin), the one it was
/// opened from to the Bin, the quarantine mark comes off so macOS runs it in place, and the moved copy opens as this one
/// quits. "Do Not Move" with "Don't ask again" ticked is kept; without it the question comes back next launch. Never in
/// a development run (the screenshot suite, a build from Xcode or CI).
enum MoveToApplications {
    private static let declinedKey = "SimplMoveToApplicationsDeclined"

    @MainActor
    static func offerIfNeeded() {
        guard !Updater.isDevelopmentRun, !UserDefaults.standard.bool(forKey: declinedKey) else { return }
        let here = AppPlacement.originalURL.standardizedFileURL
        guard !inApplications(here) else { return }

        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Move Simpl to the Applications folder?"
        alert.informativeText = "Simpl works best from Applications: it can update itself there, and it won't be lost when Downloads is cleaned up."
        alert.addButton(withTitle: "Move to Applications")
        alert.addButton(withTitle: "Do Not Move")
        alert.showsSuppressionButton = true
        alert.suppressionButton?.title = "Don't ask again"
        let answer = alert.runModal()
        guard answer == .alertFirstButtonReturn else {
            if alert.suppressionButton?.state == .on { UserDefaults.standard.set(true, forKey: declinedKey) }
            return
        }
        do {
            AppPlacement.relaunch(try move(from: here))
        } catch {
            let failed = NSAlert()
            failed.alertStyle = .warning
            failed.messageText = "Simpl could not be moved"
            failed.informativeText = "Drag Simpl into the Applications folder in Finder. (\(error.localizedDescription))"
            failed.runModal()
        }
    }

    /// In /Applications or the student's own Applications folder, at any depth.
    private static func inApplications(_ url: URL) -> Bool {
        let path = url.path
        let own = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications").standardizedFileURL.path
        return path.hasPrefix("/Applications/") || path.hasPrefix(own + "/")
    }

    /// This app copied into Applications (ditto: its signature and attributes whole), what was there before to the Bin
    /// (and back if the copy fails), the copy it was opened from to the Bin. Where it went.
    private static func move(from original: URL) throws -> URL {
        let fm = FileManager.default
        let target = try AppPlacement.applicationsFolder().appendingPathComponent(original.lastPathComponent)
        var trashed: NSURL?
        if fm.fileExists(atPath: target.path) { try fm.trashItem(at: target, resultingItemURL: &trashed) }
        // (from the running copy: when macOS runs a temporary copy of it, that one is whole and readable)
        guard ShellCommand.run("/usr/bin/ditto", [AppPlacement.runningURL.path, target.path]) == 0, fm.fileExists(atPath: target.path) else {
            try? fm.removeItem(at: target)
            if let back = trashed as URL? { try? fm.moveItem(at: back, to: target) }
            throw CocoaError(.fileWriteNoPermission)
        }
        ShellCommand.run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", target.path])
        _ = LSRegisterURL(target as CFURL, true)
        // (the copy it was opened from; one on a disk image cannot be, and stays)
        if fm.isWritableFile(atPath: original.deletingLastPathComponent().path) { try? fm.trashItem(at: original, resultingItemURL: nil) }
        return target
    }
}
