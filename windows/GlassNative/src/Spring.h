#pragma once
// A spring, as SwiftUI's: a value moving to its target with a response (seconds) and a damping ratio (1: no overshoot),
// from where it is and with the velocity it has — so motion can be grabbed and redirected at any moment.
#include <cmath>
#include <algorithm>

struct Spring {
    float value = 0, velocity = 0, target = 0;
    float response = 0.35f, damping = 0.86f;

    Spring() = default;
    Spring(float v, float r = 0.35f, float d = 0.86f) : value(v), target(v), response(r), damping(d) {}

    void To(float t) { target = t; }
    void To(float t, float v) { target = t; velocity = v; }
    void Snap(float v) { value = target = v; velocity = 0; }
    bool Settled() const { return std::fabs(value - target) < 0.0005f && std::fabs(velocity) < 0.005f; }

    void Step(float dt) {
        if (Settled()) { value = target; velocity = 0; return; }
        const float pi = 3.14159265f;
        float k = std::pow(2 * pi / std::max(response, 0.01f), 2.0f);
        float c = 4 * pi * damping / std::max(response, 0.01f);
        int steps = std::max(1, (int)std::ceil(dt / 0.004f));
        float h = dt / steps;
        for (int i = 0; i < steps; i++) {
            float a = -k * (value - target) - c * velocity;
            velocity += a * h;
            value += velocity * h;
        }
    }

    /// Where a flick heads (Apple's projection: momentum decaying as a scroll view's does).
    static float Project(float v, float rate = 0.998f) { return v / 1000.0f * rate / (1 - rate); }
};
