import SwiftUI

/// A quiz being taken in the app: its intro, the attempt with every answer as picked, the saves on their way
/// to Canvas, the clock, the review, the receipt and the feedback. Every action is one of the page's quiz calls
/// (native-app.js: the web quiz screen's own rules); nothing is ever handed in unless the student says so.
@MainActor
final class QuizRun: ObservableObject {
    enum Stage: Equatable { case intro, starting, taking, review, submitting, receipt, feedback }

    struct Banner: Equatable {
        var text: String
        var error: Bool
    }

    let course: String
    let quiz: String
    weak var engine: Engine?

    @Published var stage: Stage = .intro
    @Published var intro: QuizIntro?
    @Published var introError: String?
    @Published var attempt: QuizAttempt?
    @Published var idx = 0
    @Published var code = ""
    @Published var refused: String?
    @Published private(set) var saving = 0
    @Published private(set) var saved = false
    @Published var banner: Banner?
    /// Canvas wants the access code to save an answer: the screen asks, then the saves waiting on it go again.
    @Published var askCode = false
    /// A move on a one-at-a-time attempt under way, to this question.
    @Published private(set) var moving: Int?
    /// Which way the last move went (the page slides that way): on to a later question, or back.
    @Published private(set) var forward = true
    @Published var receipt: QuizReceipt?
    @Published var feedback: QuizFeedback?
    @Published var feedbackError: String?
    /// Canvas did not keep these answers (by number): the student decides whether to hand in all the same.
    @Published var missing: [Int] = []
    @Published private(set) var uploading: String?
    @Published var timeUp = false
    /// The stage the feedback was opened from (Back goes there).
    private(set) var feedbackFrom: Stage = .intro
    private(set) var submitted = false
    private var typing: [String: Task<Void, Never>] = [:]
    private var waitingForCode: [String] = []
    private var warned: Set<Int> = []
    private var bannerTask: Task<Void, Never>?

    init(course: String, quiz: String) {
        self.course = course
        self.quiz = quiz
    }

    private var ids: [String: Any] { ["course": course, "quiz": quiz] }

    var questions: [QuizQuestion] { attempt?.questions ?? [] }
    var current: QuizQuestion? { questions.indices.contains(idx) ? questions[idx] : nil }
    var answeredCount: Int { questions.filter(\.isAnswered).count }

    /// The last question: Canvas's page says so on a one-at-a-time attempt, the list otherwise.
    var isLast: Bool {
        guard let a = attempt else { return true }
        if a.paged, let last = a.last { return last }
        return idx >= a.questions.count - 1
    }

    var canGoBack: Bool {
        guard let a = attempt, !a.noBack else { return false }
        return idx > 0 && (a.paged ? a.canPrev : true)
    }

    var saveWord: String { saving > 0 ? "Saving…" : saved ? "Saved" : "" }

    func say(_ text: String, error: Bool = false) {
        banner = Banner(text: text, error: error)
        bannerTask?.cancel()
        bannerTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 3_200_000_000)
            guard !Task.isCancelled else { return }
            self?.banner = nil
        }
    }

    // MARK: - Intro and begin

    func loadIntro() async {
        guard let engine else { return }
        do {
            let i = try await engine.call("quizIntro", ids, as: QuizIntro.self)
            intro = i
            if code.isEmpty, let c = i.code { code = c }
            introError = nil
        } catch {
            introError = error.localizedDescription
        }
    }

    func begin() async {
        guard let engine else { return }
        refused = nil
        stage = .starting
        var args = ids
        args["code"] = code.trimmingCharacters(in: .whitespaces)
        do {
            let r = try await engine.call("quizBegin", args, as: QuizBeginAnswer.self)
            if let a = r.attempt {
                attempt = a
                idx = a.idx
                warned = []
                stage = .taking
                Haptics.tap()
            } else {
                refused = r.refused ?? "The attempt could not begin."
                if r.needsCode == true { intro?.needsCode = true }
                stage = .intro
                Haptics.error()
            }
        } catch {
            refused = error.localizedDescription
            stage = .intro
            Haptics.error()
        }
    }

    // MARK: - Answers

    private func update(_ id: String, _ change: (inout QuizQuestion) -> Void) {
        guard let i = attempt?.questions.firstIndex(where: { $0.id == id }) else { return }
        change(&attempt!.questions[i])
    }

    /// An answer changed: a pick is saved at once, typing once it pauses.
    func set(_ id: String, typed: Bool = false, _ change: (inout QuizQuestion) -> Void) {
        update(id, change)
        typing[id]?.cancel()
        typing[id] = nil
        if typed {
            typing[id] = Task { [weak self] in
                try? await Task.sleep(nanoseconds: 650_000_000)
                guard !Task.isCancelled else { return }
                await self?.save(id)
            }
        } else {
            Haptics.select()
            Task { await save(id) }
        }
    }

    private func save(_ id: String, code given: String? = nil) async {
        guard let engine, let q = questions.first(where: { $0.id == id }) else { return }
        typing[id] = nil
        saving += 1
        defer { saving -= 1 }
        var args = ids
        args["question"] = id
        args["value"] = q.value
        if let given { args["code"] = given }
        do {
            let r = try await engine.call("quizAnswer", args, as: QuizSaved.self)
            if r.needsCode == true {
                if !waitingForCode.contains(id) { waitingForCode.append(id) }
                askCode = true
                return
            }
            saved = true
        } catch {
            say("Could not save that answer: \(error.localizedDescription)", error: true)
            Haptics.error()
        }
    }

    /// The access code typed in: the answers that waited on it are saved, with it.
    func codeGiven() async {
        let c = code.trimmingCharacters(in: .whitespaces)
        guard !c.isEmpty else { return }
        let waiting = waitingForCode
        waitingForCode = []
        for id in waiting { await save(id, code: c) }
    }

    /// Every answer on its way: typing still waiting on its pause is sent now, and the saves under way are awaited.
    func flush() async {
        for id in Array(typing.keys) {
            typing[id]?.cancel()
            typing[id] = nil
            await save(id)
        }
        var waited = 0
        while saving > 0 && waited < 300 {
            try? await Task.sleep(nanoseconds: 50_000_000)
            waited += 1
        }
    }

    func flag(_ id: String) async {
        guard let engine, let q = questions.first(where: { $0.id == id }) else { return }
        let on = !q.flagged
        update(id) { $0.flagged = on }
        Haptics.select()
        var args = ids
        args["question"] = id
        args["on"] = on
        do {
            _ = try await engine.call("quizFlag", args, as: QuizSaved.self)
        } catch {
            update(id) { $0.flagged = !on }
            say("Could not flag it: \(error.localizedDescription)", error: true)
        }
    }

    /// A file for a file-upload question: uploaded to the student's own quiz files on Canvas, the answer set to name it.
    func upload(_ id: String, _ f: PickedFile) async {
        guard let engine else { return }
        guard f.data.count <= 50 * 1024 * 1024 else {
            say("\(f.name) is larger than 50 MB. Answer this one on Canvas’s own page.", error: true)
            return
        }
        uploading = id
        defer { uploading = nil }
        var args = ids
        args["question"] = id
        args["name"] = f.name
        args["type"] = f.type
        args["data"] = f.data.base64EncodedString()
        do {
            let r = try await engine.call("quizUpload", args, as: QuizSaved.self)
            if r.needsCode == true {
                askCode = true
                say("Enter the access code, then choose the file again.", error: true)
                return
            }
            if let file = r.file {
                update(id) { $0.files = [file] }
                saved = true
                Haptics.success()
            }
        } catch {
            say("Could not upload \(f.name): \(error.localizedDescription)", error: true)
            Haptics.error()
        }
    }

    // MARK: - Moving

    func go(to k: Int) async {
        guard let a = attempt, a.questions.indices.contains(k), k != idx, moving == nil else { return }
        if a.noBack && k < idx { return }
        Haptics.select()
        await heading(k > idx)
        if a.paged { await move(question: a.questions[k].id, to: k) } else { idx = k }
    }

    func next() async {
        guard let a = attempt, moving == nil else { return }
        await heading(true)
        if a.paged { await move("next", to: idx + 1) } else { idx = min(idx + 1, a.questions.count - 1) }
    }

    func back() async {
        guard let a = attempt, moving == nil, canGoBack else { return }
        await heading(false)
        if a.paged { await move("prev", to: idx - 1) } else { idx = max(0, idx - 1) }
    }

    /// The way a move goes, set a frame before the move itself: the page leaving reads it as it leaves.
    private func heading(_ on: Bool) async {
        guard forward != on else { return }
        forward = on
        try? await Task.sleep(nanoseconds: 30_000_000)
    }

    /// A move on a one-at-a-time attempt, through Canvas's own page (every answer is on Canvas first).
    private func move(_ how: String = "to", question: String? = nil, to k: Int) async {
        guard let engine else { return }
        moving = k
        defer { moving = nil }
        await flush()
        var args = ids
        args["move"] = how
        if let question { args["question"] = question }
        do {
            let a = try await engine.call("quizGo", args, as: QuizAttempt.self)
            attempt = a
            idx = a.idx
        } catch {
            say("Could not move to that question: \(error.localizedDescription)", error: true)
            Haptics.error()
        }
    }

    // MARK: - Review, submit, feedback

    func toReview() async {
        await flush()
        stage = .review
    }

    func keepWorking(at k: Int? = nil) async {
        stage = .taking
        if let k { await go(to: k) }
    }

    func submit(force: Bool = false) async {
        guard let engine else { return }
        stage = .submitting
        await flush()
        var args = ids
        args["force"] = force
        do {
            let r = try await engine.call("quizSubmit", args, as: QuizReceipt.self)
            if r.ok == false, let m = r.missing, !m.isEmpty {
                missing = m
                stage = .review
                return
            }
            receipt = r
            submitted = true
            stage = .receipt
            Haptics.success()
        } catch {
            stage = .review
            say("Could not submit: \(error.localizedDescription)", error: true)
            Haptics.error()
        }
    }

    func openFeedback(_ attempt: Int? = nil) async {
        guard let engine else { return }
        if stage != .feedback { feedbackFrom = stage }
        stage = .feedback
        feedback = nil
        feedbackError = nil
        var args = ids
        if let attempt { args["attempt"] = attempt }
        do {
            feedback = try await engine.call("quizFeedback", args, as: QuizFeedback.self)
        } catch {
            feedbackError = error.localizedDescription
        }
    }

    func leaveFeedback() {
        stage = feedbackFrom == .feedback ? .intro : feedbackFrom
    }

    // MARK: - The clock

    func remaining(at now: Date) -> TimeInterval? {
        guard let a = attempt, a.timed, let end = QuizTime.date(a.endAt) else { return nil }
        return end.timeIntervalSince(now)
    }

    func elapsed(at now: Date) -> TimeInterval? {
        guard let start = QuizTime.date(attempt?.startedAt) else { return nil }
        return now.timeIntervalSince(start)
    }

    /// Five minutes left, one minute left, time up: said once each (time up goes to the review; Canvas closes the attempt).
    func checkClock(_ now: Date) {
        guard stage == .taking || stage == .review, let left = remaining(at: now) else { return }
        if left <= 300 && left > 290 && !warned.contains(5) {
            warned.insert(5)
            say("5 minutes left.")
            Haptics.play("warning")
        }
        if left <= 60 && left > 50 && !warned.contains(1) {
            warned.insert(1)
            say("1 minute left.", error: true)
            Haptics.play("warning")
        }
        if left <= 0 && !warned.contains(0) && stage == .taking {
            warned.insert(0)
            timeUp = true
            Haptics.play("warning")
            Task { await toReview() }
        }
    }
}
