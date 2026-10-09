import SwiftUI

// The Dashboard's courses (1.2): as cards (the Cards view) and as a dense list, one line a course (the List view).

/// What a course's context menu offers: open it or one of its sections, or open it on the school's site. (The engine
/// is handed in: a menu is drawn apart from the view it belongs to.)
struct DashCourseMenu: View {
    let course: DashCourse
    let engine: Engine

    var body: some View {
        Button("Open") { engine.openWeb(course.url, title: course.code) }
        Divider()
        ForEach(DashCourseMenu.sections, id: \.0) { kind, label in
            Button(label) { engine.go(.section("courses/\(course.id)", kind)) }
        }
        Divider()
        Button("Open in \(engine.lmsName)") { engine.openWebScreen(course.url, title: course.code) }
        Button("Copy Link") { if let u = engine.absolute(course.url) { copyToPasteboard(u.absoluteString) } }
    }

    static let sections: [(String, String)] = [
        ("grades", "Grades"), ("assignments", "Assignments"), ("modules", "Modules"), ("announcements", "Announcements"),
        ("discussions", "Discussions"), ("files", "Files"),
    ]

    /// The sections the app has a screen of its own for.
    static let native: Set<String> = ["announcements", "assignments", "discussions", "files", "grades", "modules", "pages", "people", "quizzes", "syllabus"]
}

/// A card or a line on the page that is a button: glass that answers the pointer on macOS 26; before it, the card's
/// colour lifting under the pointer. Never larger under the pointer: the course card's quick links sit over it.
struct DashCardStyle: ButtonStyle {
    var radius: CGFloat = 20

    func makeBody(configuration: Configuration) -> some View {
        DashCardBody(configuration: configuration, radius: radius)
    }

    private struct DashCardBody: View {
        let configuration: ButtonStyleConfiguration
        let radius: CGFloat
        @State private var hover = false

        var body: some View {
            let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
            surface(configuration.label.contentShape(shape), shape: shape)
                .opacity(configuration.isPressed ? 0.86 : 1)
                .animation(Motion.snappy, value: configuration.isPressed)
                .onHover { h in withAnimation(Motion.hover) { hover = h } }
        }

        @ViewBuilder
        private func surface(_ label: some View, shape: RoundedRectangle) -> some View {
            if #available(macOS 26.0, *) {
                label.glass(shape, interactive: true)
            } else {
                label
                    .background { shape.fill(hover ? Theme.cardHover : Theme.card) }
                    .overlay(shape.strokeBorder(Theme.edge, lineWidth: 1))
                    .shadow(color: Theme.shadow, radius: hover ? 10 : 1.5, y: hover ? 4 : 1)
            }
        }
    }
}

/// A small round icon button (a course's quick link): a wash under the pointer.
struct DashIconButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        DashIconBody(configuration: configuration)
    }

    private struct DashIconBody: View {
        let configuration: ButtonStyleConfiguration
        @State private var hover = false

        var body: some View {
            configuration.label
                .frame(width: 30, height: 30)
                .contentShape(Circle())
                .background(Circle().fill(Color.primary.opacity(configuration.isPressed ? 0.14 : (hover ? 0.08 : 0))))
                .animation(Motion.hover, value: hover)
                .onHover { hover = $0 }
        }
    }
}

/// A course's score in a ring of its colour, its letter under the number.
struct DashScoreRing: View {
    let course: DashCourse
    var size: CGFloat = 56

    var body: some View {
        ZStack {
            Ring(value: course.score, color: Color(hex: course.color), lineWidth: 5, key: "dash:\(course.id)")
            VStack(spacing: 0) {
                Text(course.scoreText ?? "N/A")
                    .font(.system(size: 12, weight: .semibold).monospacedDigit())
                    .foregroundStyle(course.score == nil ? .secondary : .primary)
                    .minimumScaleFactor(0.6)
                    .lineLimit(1)
                if let g = course.grade, !g.isEmpty {
                    Text(g)
                        .font(.system(size: 10.5, weight: .semibold))
                        .foregroundStyle(.secondary)
                }
            }
            .padding(7)
        }
        .frame(width: size, height: size)
    }
}

/// A course's quick links (its first sections, as Canvas's card has them): each opens that section.
struct DashQuickLinks: View {
    let course: DashCourse
    @EnvironmentObject private var engine: Engine

    static func count(_ course: DashCourse) -> Int { min(course.links?.count ?? 0, 4) }

    var body: some View {
        HStack(spacing: 2) {
            ForEach(Array((course.links ?? []).prefix(4).enumerated()), id: \.offset) { _, link in
                Button { open(link) } label: {
                    Image(systemName: Glyph.section(link.kind ?? ""))
                        .font(.system(size: 13.5, weight: .medium))
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(DashIconButtonStyle())
                .help(link.label ?? "")
                .accessibilityLabel(link.label ?? "Open")
            }
        }
    }

    private func open(_ link: DashLink) {
        if let kind = link.kind, DashCourseMenu.native.contains(kind) {
            engine.go(.section("courses/\(course.id)", kind))
        } else if let url = link.url {
            engine.openWeb(url, title: link.label ?? course.code)
        }
    }
}

/// A course as a card: its colour (or its picture) with its code and what wants attention (due today, unread
/// announcements); its name, term and teacher; its score in a ring; how much is handed in; what is due next; its
/// quick links.
struct DashCourseCard: View {
    let course: DashCourse
    let progress: CourseProgress?
    @EnvironmentObject private var engine: Engine

    private var color: Color { Color(hex: course.color) }

    var body: some View {
        Button { engine.openWeb(course.url, title: course.code) } label: { face }
            .buttonStyle(DashCardStyle(radius: 20))
            .overlay(alignment: .bottomLeading) {
                // (over the card rather than in it: buttons of their own, on the row the card keeps free for them)
                DashQuickLinks(course: course)
                    .padding(.leading, 9)
                    .padding(.bottom, 8)
            }
            .contextMenu { DashCourseMenu(course: course, engine: engine) }
            .accessibilityElement(children: .contain)
            .accessibilityLabel("\(course.code), \(course.scoreText ?? "")")
    }

    private var face: some View {
        VStack(alignment: .leading, spacing: 0) {
            hero
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .center, spacing: 12) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(course.name ?? course.code)
                            .font(.sBody.weight(.semibold))
                            .lineLimit(1)
                        Text(course.sub?.isEmpty == false ? course.sub! : " ")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                    Spacer(minLength: 6)
                    DashScoreRing(course: course, size: 56)
                }
                handedIn
                nextLine
                if DashQuickLinks.count(course) > 0 {
                    Color.clear.frame(height: 26)
                }
            }
            .padding(.horizontal, 16)
            .padding(.top, 13)
            .padding(.bottom, 12)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var hero: some View {
        LinearGradient(colors: [color, color.opacity(0.78)], startPoint: .topLeading, endPoint: .bottomTrailing)
            .overlay {
                if let s = course.image, let url = URL(string: s) {
                    AsyncImage(url: url) { phase in
                        if let image = phase.image {
                            image.resizable().scaledToFill()
                                .overlay(LinearGradient(colors: [.clear, .black.opacity(0.45)], startPoint: .top, endPoint: .bottom))
                        } else {
                            Color.clear
                        }
                    }
                }
            }
            .overlay(alignment: .bottomLeading) {
                Text(course.code)
                    .font(.system(size: 20, weight: .bold))
                    .foregroundStyle(.white)
                    .lineLimit(2)
                    .shadow(color: .black.opacity(0.22), radius: 2, y: 1)
                    .padding(.horizontal, 16)
                    .padding(.bottom, 12)
            }
            .overlay(alignment: .topTrailing) { badges.padding(12) }
            .frame(height: 96)
            .clipShape(UnevenRoundedRectangle(topLeadingRadius: 20, topTrailingRadius: 20, style: .continuous))
    }

    private var badges: some View {
        HStack(spacing: 6) {
            if let n = course.dueToday, n > 0 {
                Text("\(n) due today")
                    .font(.sCaption.weight(.bold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(Color.red, in: Capsule())
            }
            if let n = course.unread, n > 0 {
                Label("\(n)", systemImage: "megaphone.fill")
                    .font(.sCaption.weight(.bold))
                    .foregroundStyle(color)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(.white, in: Capsule())
                    .help("\(n) unread \(n == 1 ? "announcement" : "announcements")")
            }
        }
    }

    /// "12 of 20 handed in"; "Nothing to hand in" for a course with nothing to hand in; "Counting…" until it is known.
    static func handedInText(_ p: CourseProgress?) -> String {
        guard let p else { return "Counting…" }
        return p.total > 0 ? "\(p.done) of \(p.total) handed in" : "Nothing to hand in"
    }

    private var handedIn: some View {
        HStack(spacing: 10) {
            DashBar(fraction: progress.map { $0.total > 0 ? Double($0.done) / Double($0.total) : 0 } ?? 0, color: color)
            Text(DashCourseCard.handedInText(progress))
                .font(.sCaption.monospacedDigit())
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .fixedSize()
        }
    }

    @ViewBuilder
    private var nextLine: some View {
        if let next = course.next {
            (Text("Next  ").foregroundStyle(.secondary) + Text(next.title).fontWeight(.medium) + Text(next.when.map { "  ·  \($0)" } ?? "").foregroundStyle(.secondary))
                .font(.sCallout)
                .lineLimit(1)
                .frame(maxWidth: .infinity, alignment: .leading)
        } else {
            Text("Nothing due")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}
