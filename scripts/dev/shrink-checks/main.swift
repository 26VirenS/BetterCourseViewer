import Foundation

// (tools) Compress Files' arithmetic (mac/Simpl/Tools/ShrinkModel.swift), checked on its own, as the models' checks
// are (scripts/dev/swift-checks): the names, the shares saved, the scaling, and "Fit under"'s search through quality
// and size with a made-up size curve. By hand: swiftc -o /tmp/shrink-checks mac/Simpl/Tools/ShrinkModel.swift
// scripts/dev/shrink-checks/main.swift && /tmp/shrink-checks. The exit code is the number that failed.

var passed = 0, failed = 0
func check(_ ok: Bool, _ what: String) {
    if ok { passed += 1; print("  ✓ \(what)") } else { failed += 1; print("  ✗ \(what)") }
}

print("Compress Files")
check(ShrinkMath.outputName("photo.jpg", ext: "jpg") == "photo-smaller.jpg", "a copy is <name>-smaller.<ext>")
check(ShrinkMath.outputName("IMG_0001.HEIC", ext: "jpg") == "IMG_0001-smaller.jpg", "a HEIC's copy is a JPEG")
check(ShrinkMath.outputName("Week 2 notes.v2.pdf", ext: "pdf") == "Week 2 notes.v2-smaller.pdf", "only the last extension comes off")
check(ShrinkMath.imageExtension(original: "png", hasAlpha: true, preset: .balanced) == "png", "a PNG with transparency stays a PNG at Balanced")
check(ShrinkMath.imageExtension(original: "png", hasAlpha: true, preset: .smallest) == "jpg", "and becomes a JPEG at Smallest")
check(ShrinkMath.imageExtension(original: "png", hasAlpha: false, preset: .max) == "jpg", "a PNG without transparency becomes a JPEG")
check(ShrinkMath.imageExtension(original: "webp", hasAlpha: true, preset: .max) == "jpg", "any other picture becomes a JPEG")
check(ShrinkMath.savedPercent(before: 100_000, after: 58_000) == 42 && ShrinkMath.savedPercent(before: 1_000_000, after: 695_000) == 31,
      "the share saved, half up (42, 31)")
check(ShrinkMath.savedPercent(before: 1000, after: 1000) == 0 && ShrinkMath.savedPercent(before: 0, after: 10) == 0 && ShrinkMath.savedPercent(before: 100, after: 120) == 0,
      "a copy no smaller saves nothing")
check(ShrinkMath.savedPercent(before: 200, after: 199) == 1 && ShrinkMath.savedPercent(before: 201, after: 200) == 0, "a half rounds up (1, 0)")
check(ShrinkMath.scale(width: 4032, height: 3024, longEdge: 2048) == 2048.0 / 4032 && ShrinkMath.scale(width: 1600, height: 900, longEdge: 2048) == 1
      && ShrinkMath.scale(width: 900, height: 1600, longEdge: 1280) == 1280.0 / 1600 && ShrinkMath.scale(width: 4032, height: 3024, longEdge: nil) == 1,
      "the long edge scaled down to the target's, never up")
check(ShrinkPreset.balanced.longEdge == 2048 && ShrinkPreset.smallest.longEdge == 1280 && ShrinkPreset.max.longEdge == nil
      && ShrinkPreset.balanced.quality == 0.8 && ShrinkPreset.smallest.keepsPNG == false && ShrinkPreset.max.keepsPNG,
      "the targets as the specification has them")
check(ShrinkMath.sizeText(103_579) == "104 KB" && ShrinkMath.sizeText(1_486) == "1 KB" && ShrinkMath.sizeText(1_250_000) == "1.25 MB"
      && ShrinkMath.sizeText(2_000_000) == "2 MB" && ShrinkMath.sizeText(512) == "512 B",
      "sizes as the Finder counts them (104 KB, 1 KB, 1.25 MB, 2 MB, 512 B)")
check(ShrinkMath.limitBytes("0.2") == 200_000 && ShrinkMath.limitBytes("1") == 1_000_000 && ShrinkMath.limitBytes("2,5") == 2_500_000
      && ShrinkMath.limitBytes("0") == nil && ShrinkMath.limitBytes("x") == nil && ShrinkMath.limitBytes("") == nil,
      "the Fit under field in MB (a comma for a point too; nothing, nought or words are no size)")

// "Fit under": a made-up picture whose size falls with quality and with the square of its scale
func curve(_ q: Double, _ s: Double) -> Int { Int(1_000_000 * (0.2 + 0.8 * q) * s * s) }
let easy = ShrinkFit.search(limit: 900_000, sizeAt: curve)
check(easy?.under == true && easy?.found.scale == 1 && easy?.found.quality == 0.8, "a limit the best quality lands under takes it as it is")
let mid = ShrinkFit.search(limit: 600_000, sizeAt: curve)
check(mid?.under == true && mid?.found.scale == 1 && (mid?.found.size ?? 0) <= 600_000 && (mid?.found.quality ?? 0) > 0.45 && (mid?.found.quality ?? 0) <= 0.5,
      "a tighter limit searches the quality down and stops under it (\(mid.map { "\($0.found.quality) → \($0.found.size)" } ?? "none"))")
let tight = ShrinkFit.search(limit: 200_000, sizeAt: curve)
// (at three quarters the lowest quality is 247 KB still: the half-size copy is the first that can land under)
check(tight?.under == true && tight?.found.scale == 0.5 && (tight?.found.size ?? 0) <= 200_000 && (tight?.found.quality ?? 0) < 0.8,
      "when the lowest quality is over it, the picture is scaled down until one can land under (\(tight?.found.scale ?? 0))")
let hopeless = ShrinkFit.search(limit: 10_000, sizeAt: curve)
check(hopeless?.under == false && hopeless?.found.scale == 0.25 && hopeless?.found.quality == ShrinkFit.lowest, "a limit nothing reaches gives the smallest copy and says so")
let again = ShrinkFit.search(limit: 600_000, sizeAt: curve)
check(again?.found == mid?.found && again?.under == mid?.under, "the same sizes give the same answer")
var calls = 0
let counted = ShrinkFit.search(limit: 600_000) { q, s in calls += 1; return curve(q, s) }
check(counted?.found == mid?.found && calls <= 8, "a search writes at most a handful of trial copies (\(calls))")
check(ShrinkFit.search(limit: 100) { _, _ in nil } == nil, "a picture that cannot be written at all is none")

print("\(passed) passed, \(failed) failed")
exit(Int32(failed))
