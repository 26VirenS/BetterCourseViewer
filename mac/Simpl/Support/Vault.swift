import CryptoKit
import Foundation
import Security

/// (1.3.76) What Simpl keeps on this Mac, encrypted by Simpl itself and never with a prompt: the extension's storage
/// (settings, grade history, goals), the answers kept for a fast start, the flashcards, the saved sign-in and the
/// school's cookies. One 256-bit key, made the first time it is needed, seals each of them (AES-256-GCM), and that key
/// is held by this Mac's Secure Enclave: what Application Support ▸ Simpl ▸ vault.key keeps is the Enclave key's blob,
/// which only this Mac's Enclave can use, and the data key sealed to it (an ephemeral P-256 key agreed with the
/// Enclave's, HKDF-SHA256, AES-GCM). A Mac with no Enclave (an older Intel Mac, a virtual machine — CI's), or one
/// whose Enclave refuses, keeps the key in the login Keychain instead; where that fails too, in vault.key itself,
/// readable by this user only, and the console says so.
///
/// A sealed file is "SIMPLENC", a version byte (1) and AES-GCM's combined box (nonce, ciphertext, tag), what it is
/// (its purpose: "storage", "flashcards" …) authenticated with it, so one file cannot be passed off as another. A file
/// without the mark was written before 1.3.76: it is read as it is, and its owner writes it again sealed. A vault that
/// cannot be opened (vault.key made on another Mac, its Keychain item gone) is set aside in Application Support ▸
/// Simpl ▸ Unreadable-<date>, with each file it sealed as that file is next read — never deleted — and a new one is
/// made: a line on the console, never an alert or a crash.
///
/// Plain on purpose: the preferences (UserDefaults: the theme, the layout, what was seen), the console, the theme's
/// photos, the screenshot suite's pictures, files the student saves elsewhere, and WebKit's own website data.
///
/// Opened once a launch, off the main thread (SimplApp.init); safe from any thread.
enum Vault {
    /// What holds the key.
    enum Protection: String {
        case secureEnclave = "Secure Enclave"
        case keychain = "Keychain"
        case file = "file"
    }

    static var protection: Protection { current().protection }

    /// Where the key is, in the words Settings and the sign-in card use ("Its key is …"); nil when it is in a file
    /// beside the data (the last resort, which locks nothing).
    static var keyHolder: String? {
        switch protection {
        case .secureEnclave: return "held by this Mac’s Secure Enclave"
        case .keychain: return "kept in your Keychain"
        case .file: return nil
        }
    }

    /// Application Support ▸ Simpl (the theme's photos are kept there too).
    static var folder: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first ?? FileManager.default.temporaryDirectory
        let dir = base.appendingPathComponent("Simpl", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    /// At launch: the vault opened (or made) off the main thread, so the first read finds it ready.
    static func prepare() {
        DispatchQueue.global(qos: .userInitiated).async { _ = current() }
    }

    // MARK: - Sealing

    private static let mark = Data("SIMPLENC".utf8)
    private static let version: UInt8 = 1

    /// The data sealed: the mark, the version, AES-GCM's combined box. Nil only while the vault cannot be opened.
    static func seal(_ data: Data, purpose: String) -> Data? {
        guard let key = current().sealKey,
              let combined = try? AES.GCM.seal(data, using: key, authenticating: Data(purpose.utf8)).combined else { return nil }
        var out = mark
        out.append(version)
        out.append(combined)
        return out
    }

    /// What `seal` sealed for this purpose, or nil (not sealed, another purpose's, another key's, damaged).
    static func open(_ data: Data, purpose: String) -> Data? {
        guard isSealed(data), data[data.startIndex + mark.count] == version, let key = current().sealKey,
              let box = try? AES.GCM.SealedBox(combined: Data(data.dropFirst(mark.count + 1))) else { return nil }
        return try? AES.GCM.open(box, using: key, authenticating: Data(purpose.utf8))
    }

    /// A file's name that says nothing of what it holds: 32 hex digits of an HMAC-SHA256 of the purpose.
    static func fileName(_ purpose: String) -> String {
        let code = HMAC<SHA256>.authenticationCode(for: Data(purpose.utf8), using: current().nameKey)
        return String(code.map { String(format: "%02x", $0) }.joined().prefix(32))
    }

    /// The data sealed and written in one go (atomically). False when nothing was written.
    @discardableResult
    static func write(_ data: Data, to url: URL, purpose: String) -> Bool {
        guard let box = seal(data, purpose: purpose) else { return false }
        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            try box.write(to: url, options: .atomic)
            return true
        } catch {
            return false
        }
    }

    /// A file's contents opened. `legacy`: it was written before 1.3.76, in plain — its owner writes it again sealed.
    /// Nil when there is no file, or it cannot be opened: then it is set aside (Unreadable-<date>), never deleted.
    static func read(from url: URL, purpose: String) -> (data: Data, legacy: Bool)? {
        guard let raw = try? Data(contentsOf: url) else { return nil }
        guard isSealed(raw) else { return (raw, true) }
        if let data = open(raw, purpose: purpose) { return (data, false) }
        // (a vault the Keychain would not open this launch: the file is left where it is, for the next)
        guard current().sealKey != nil else { return nil }
        if let dir = setAside(url) { note("\(url.lastPathComponent) could not be opened; set aside in \(dir.path)") }
        return nil
    }

    /// Reset Everything: vault.key and its Keychain item deleted and the key forgotten — whatever it sealed can no
    /// longer be read by anyone. The next seal makes a new vault.
    static func erase() {
        lock.lock()
        defer { lock.unlock() }
        if let raw = try? Data(contentsOf: keyURL), let file = try? JSONDecoder().decode(KeyFile.self, from: raw), file.mode == "keychain", let id = file.id {
            keychainDelete(id)
        }
        try? FileManager.default.removeItem(at: keyURL)
        opened = nil
    }

    private static func isSealed(_ data: Data) -> Bool {
        data.count > mark.count + 1 && data.prefix(mark.count) == mark
    }

    // MARK: - The key

    /// The vault as this launch has it: two keys drawn from the one kept (HKDF-SHA256), one to seal with and one to name
    /// files with.
    private struct Opened {
        let protection: Protection
        /// Nil while the Keychain will not give the key (locked, access refused): nothing is sealed, opened or set aside
        /// this launch, and the next launch tries again.
        let sealKey: SymmetricKey?
        let nameKey: SymmetricKey

        init(_ protection: Protection, master: Data?) {
            self.protection = protection
            if let master {
                let key = SymmetricKey(data: master)
                sealKey = HKDF<SHA256>.deriveKey(inputKeyMaterial: key, info: Data("Simpl vault v1 seal".utf8), outputByteCount: 32)
                nameKey = HKDF<SHA256>.deriveKey(inputKeyMaterial: key, info: Data("Simpl vault v1 names".utf8), outputByteCount: 32)
            } else {
                sealKey = nil
                nameKey = SymmetricKey(size: .bits256)
            }
        }
    }

    /// vault.key: what holds the key, and what it takes to get it back.
    private struct KeyFile: Codable {
        var version = 1
        /// "se", "keychain" or "file".
        var mode: String
        /// "se": the Enclave key's blob (only this Mac's Enclave can use it), the ephemeral public key (raw), the HKDF
        /// salt, and the data key sealed (AES-GCM, combined).
        var enclaveKey: Data?
        var ephemeral: Data?
        var salt: Data?
        var box: Data?
        /// "keychain": the item's account is "master-" and this (a vault's own: two homes on one Mac — the screenshot
        /// suite's workers — never share or overwrite one).
        var id: String?
        /// "file": the key itself.
        var key: Data?
    }

    private static let lock = NSLock()
    private static var opened: Opened?
    private static var keyURL: URL { folder.appendingPathComponent("vault.key") }

    private static func current() -> Opened {
        lock.lock()
        defer { lock.unlock() }
        if let o = opened { return o }
        let o = openOrMake()
        opened = o
        return o
    }

    private static func openOrMake() -> Opened {
        if FileManager.default.fileExists(atPath: keyURL.path) {
            switch unlock() {
            case .key(let master, let protection):
                if protection == .file { note("its key is kept in vault.key itself (readable by this user only, not locked)") }
                return Opened(protection, master: master)
            case .later(let why):
                note("\(why); nothing is kept encrypted until Simpl is opened again")
                return Opened(.keychain, master: nil)
            case .lost(let why):
                let dir = setAside(keyURL)
                note("its key cannot be opened (\(why)); a new vault is made, and what the old one sealed is set aside in \(dir?.path ?? "Unreadable") as it is read")
            }
        }
        return make()
    }

    private enum Unlock {
        case key(Data, Protection)
        /// The key is there but not to be had this launch.
        case later(String)
        /// The key is gone for good.
        case lost(String)
    }

    private static func unlock() -> Unlock {
        guard let raw = try? Data(contentsOf: keyURL), let file = try? JSONDecoder().decode(KeyFile.self, from: raw) else {
            return .lost("vault.key cannot be read")
        }
        switch file.mode {
        case "se":
            do {
                let master = try enclaveOpen(file)
                return .key(master, .secureEnclave)
            } catch {
                return .lost("the Secure Enclave will not open it: \(error)")
            }
        case "keychain":
            guard let id = file.id else { return .lost("vault.key names no Keychain item") }
            var q = keychainQuery(id)
            q[kSecReturnData as String] = true
            q[kSecMatchLimit as String] = kSecMatchLimitOne
            var item: CFTypeRef?
            let status = SecItemCopyMatching(q as CFDictionary, &item)
            if status == errSecSuccess, let master = item as? Data, master.count == 32 { return .key(master, .keychain) }
            if status == errSecSuccess || status == errSecItemNotFound { return .lost("its Keychain item is gone") }
            return .later("the Keychain would not give its key (\(status))")
        case "file":
            guard let master = file.key, master.count == 32 else { return .lost("vault.key holds no key") }
            return .key(master, .file)
        default:
            return .lost("vault.key's mode \(file.mode) is not known")
        }
    }

    /// A new key, kept where it can be, and vault.key written.
    private static func make() -> Opened {
        let master = random(32)
        let (file, protection) = keep(master)
        switch commit(file) {
        case .written:
            switch protection {
            case .secureEnclave: note("made; its key is held by this Mac's Secure Enclave")
            case .keychain: note("made; its key is kept in the login Keychain")
            case .file: note("made; its key is kept in vault.key itself (no Secure Enclave or Keychain would take it): readable by this user only, not locked")
            }
            return Opened(protection, master: master)
        case .taken:
            // (another copy of Simpl made one at the same moment: that one, and this key let go)
            if protection == .keychain, let id = file.id { keychainDelete(id) }
            if case .key(let theirs, let p) = unlock() { return Opened(p, master: theirs) }
            note("another copy of Simpl made one that cannot be opened; nothing is kept encrypted until Simpl is opened again")
            return Opened(protection, master: nil)
        case .failed(let why):
            if protection == .keychain, let id = file.id { keychainDelete(id) }
            note("vault.key could not be written (\(why)); nothing is kept encrypted until Simpl is opened again")
            return Opened(protection, master: nil)
        }
    }

    /// The Secure Enclave if it will (and gives the key back), else the Keychain, else vault.key itself.
    private static func keep(_ master: Data) -> (KeyFile, Protection) {
        if SecureEnclave.isAvailable {
            do {
                let file = try enclaveSeal(master)
                // (opened again before it is trusted: a blob this Mac's Enclave would not take back is no use)
                if try enclaveOpen(file) == master { return (file, .secureEnclave) }
                note("the Secure Enclave gave back another key; the Keychain will hold it")
            } catch {
                note("the Secure Enclave could not hold its key (\(error)); the Keychain will")
            }
        }
        let id = random(8).map { String(format: "%02x", $0) }.joined()
        let status = keychainAdd(master, id: id)
        if status == errSecSuccess { return (KeyFile(mode: "keychain", id: id), .keychain) }
        note("the Keychain could not hold its key either (\(status))")
        return (KeyFile(mode: "file", key: master), .file)
    }

    private enum Commit {
        case written, taken
        case failed(String)
    }

    /// vault.key written whole, readable by this user only (0600), and only if there is none yet: put in its place by a
    /// hard link, which fails when the name is taken.
    private static func commit(_ file: KeyFile) -> Commit {
        guard let data = try? JSONEncoder().encode(file) else { return .failed("it could not be encoded") }
        let temp = folder.appendingPathComponent(".vault-\(UUID().uuidString).key")
        guard FileManager.default.createFile(atPath: temp.path, contents: data, attributes: [.posixPermissions: 0o600]) else {
            return .failed("no file could be made in \(folder.path)")
        }
        defer { unlink(temp.path) }
        if link(temp.path, keyURL.path) == 0 { return .written }
        let e = errno
        return e == EEXIST ? .taken : .failed(String(cString: strerror(e)))
    }

    // MARK: - The Secure Enclave

    private enum VaultError: Error {
        case noAccessControl, noBox, incomplete
    }

    private static let sharedInfo = Data("Simpl vault v1".utf8)

    /// The key sealed to a new Secure Enclave key (not kept in any keychain: its blob is kept in vault.key): an ephemeral
    /// P-256 key agreed with it, HKDF-SHA256 over the shared secret with a random salt, AES-GCM. Usable whenever this
    /// Mac's user is logged in (after the first unlock since it started, this Mac only), with no prompt.
    private static func enclaveSeal(_ master: Data) throws -> KeyFile {
        guard let access = SecAccessControlCreateWithFlags(nil, kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly, .privateKeyUsage, nil) else {
            throw VaultError.noAccessControl
        }
        let enclave = try SecureEnclave.P256.KeyAgreement.PrivateKey(accessControl: access)
        let ephemeral = P256.KeyAgreement.PrivateKey()
        let salt = random(32)
        let wrap = try ephemeral.sharedSecretFromKeyAgreement(with: enclave.publicKey)
            .hkdfDerivedSymmetricKey(using: SHA256.self, salt: salt, sharedInfo: sharedInfo, outputByteCount: 32)
        guard let box = try AES.GCM.seal(master, using: wrap).combined else { throw VaultError.noBox }
        return KeyFile(mode: "se", enclaveKey: enclave.dataRepresentation, ephemeral: ephemeral.publicKey.rawRepresentation, salt: salt, box: box)
    }

    private static func enclaveOpen(_ file: KeyFile) throws -> Data {
        guard let blob = file.enclaveKey, let ephemeral = file.ephemeral, let salt = file.salt, let box = file.box else {
            throw VaultError.incomplete
        }
        let enclave = try SecureEnclave.P256.KeyAgreement.PrivateKey(dataRepresentation: blob)
        let wrap = try enclave.sharedSecretFromKeyAgreement(with: P256.KeyAgreement.PublicKey(rawRepresentation: ephemeral))
            .hkdfDerivedSymmetricKey(using: SHA256.self, salt: salt, sharedInfo: sharedInfo, outputByteCount: 32)
        return try AES.GCM.open(AES.GCM.SealedBox(combined: box), using: wrap)
    }

    // MARK: - The Keychain (the fallback)

    private static let service = "Simpl Courses vault"

    private static func keychainQuery(_ id: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: "master-" + id]
    }

    /// (the login Keychain, a file keychain: its items have no accessibility class, so none is given)
    private static func keychainAdd(_ master: Data, id: String) -> OSStatus {
        var q = keychainQuery(id)
        q[kSecValueData as String] = master
        q[kSecAttrLabel as String] = service
        return SecItemAdd(q as CFDictionary, nil)
    }

    private static func keychainDelete(_ id: String) {
        SecItemDelete(keychainQuery(id) as CFDictionary)
    }

    // MARK: - Files

    /// A file that cannot be opened, moved (never deleted) to Application Support ▸ Simpl ▸ Unreadable-<date>, under its
    /// folder's name and its own. The folder it went to.
    @discardableResult
    private static func setAside(_ url: URL) -> URL? {
        let fm = FileManager.default
        let day = ISO8601DateFormatter.string(from: Date(), timeZone: .current, formatOptions: [.withFullDate])
        let dir = folder.appendingPathComponent("Unreadable-" + day, isDirectory: true)
        var name = url.deletingLastPathComponent().lastPathComponent + "-" + url.lastPathComponent
        if fm.fileExists(atPath: dir.appendingPathComponent(name).path) { name = "\(Int(Date().timeIntervalSince1970))-" + name }
        do {
            try fm.createDirectory(at: dir, withIntermediateDirectories: true)
            try fm.moveItem(at: url, to: dir.appendingPathComponent(name))
            return dir
        } catch {
            note("\(url.path) could not be set aside (\(error))")
            return nil
        }
    }

    /// Random bytes (CryptoKit's generator).
    private static func random(_ count: Int) -> Data {
        SymmetricKey(size: SymmetricKeySize(bitCount: count * 8)).withUnsafeBytes { Data($0) }
    }

    /// A line on the console (never a key, never what was sealed).
    private static func note(_ line: String) {
        NSLog("[Simpl] vault: %@", line)
    }
}
