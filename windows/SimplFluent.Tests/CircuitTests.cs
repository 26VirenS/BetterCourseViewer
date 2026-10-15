using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Circuit Builder's maths (Tools/Physics/Model/CircuitModel.cs): the nodes terminals and wires make, the DC solver
/// on the quick-build circuits and on circuits that can't be solved, the readings, and the calculator strip's Ohm's law
/// and series/parallel sums — the shared worked examples.
/// </summary>
public class CircuitTests
{
    private const double Tol = 0.0005;

    private static CircuitPart Named(CircuitDoc d, string name) => d.Parts.First(p => d.NameOf(p) == name);

    [Fact]
    public void NineVoltsAcrossOneHundredAndTwoHundredInSeries()
    {
        // (9 V with 100 Ω and 200 Ω in series → I = 0.03 A, 6 V across the 200 Ω)
        var d = CircuitSolver.Preset("series")!;
        var s = CircuitSolver.Solve(d);
        Assert.Null(s.Problem);
        Assert.Equal(0, s.Unpowered);
        Assert.Equal(0.03, Math.Abs(s.Of(Named(d, "Resistor 1").Id).Amps), Tol);
        Assert.Equal(3, Math.Abs(s.Of(Named(d, "Resistor 1").Id).Volts), Tol);
        Assert.Equal(6, Math.Abs(s.Of(Named(d, "Resistor 2").Id).Volts), Tol);
        Assert.Equal(0.03, Math.Abs(s.Of(Named(d, "Ammeter 1").Id).Amps), Tol);
        Assert.Equal(0, Math.Abs(s.Of(Named(d, "Ammeter 1").Id).Volts), 1e-9);
        var battery = s.Of(Named(d, "Battery 1").Id);
        Assert.Equal(9, battery.Volts, Tol);
        Assert.Equal(0.03, Math.Abs(battery.Amps), Tol);
        Assert.Equal(0.27, Math.Abs(battery.Watts), Tol);
        // (the current leaves the battery's + and comes round to its −: through the resistors from terminal 0 to 1)
        Assert.True(s.Of(Named(d, "Resistor 1").Id).Amps > 0);
        Assert.True(battery.Amps < 0);
    }

    [Fact]
    public void OneHundredInParallelWithTwoHundred()
    {
        // (100 ∥ 200 = 66.67 Ω; 9 V across them draws 0.135 A)
        Assert.Equal(66.67, CircuitMath.Parallel(new[] { 100.0, 200 }), 0.005);
        Assert.Equal(300, CircuitMath.Series(new[] { 100.0, 200 }), 1e-9);
        var d = CircuitSolver.Preset("parallel")!;
        var s = CircuitSolver.Solve(d);
        Assert.Null(s.Problem);
        Assert.Equal(0.135, Math.Abs(s.Of(Named(d, "Battery 1").Id).Amps), Tol);
        Assert.Equal(0.09, Math.Abs(s.Of(Named(d, "Resistor 1").Id).Amps), Tol);
        Assert.Equal(0.045, Math.Abs(s.Of(Named(d, "Resistor 2").Id).Amps), Tol);
        Assert.Equal(9, Math.Abs(s.Of(Named(d, "Resistor 2").Id).Volts), Tol);
    }

    [Fact]
    public void TheVoltageDivider()
    {
        // (12 V across 1 kΩ + 2 kΩ → 8 V at the middle node, read by the voltmeter across the 2 kΩ)
        var d = CircuitSolver.Preset("divider")!;
        var s = CircuitSolver.Solve(d);
        Assert.Null(s.Problem);
        Assert.Equal(8, Math.Abs(s.Of(Named(d, "Voltmeter 1").Id).Volts), Tol);
        Assert.Equal(8, Math.Abs(s.Of(Named(d, "Resistor 2").Id).Volts), Tol);
        Assert.Equal(4, Math.Abs(s.Of(Named(d, "Resistor 1").Id).Volts), Tol);
        Assert.Equal(0.004, Math.Abs(s.Of(Named(d, "Resistor 1").Id).Amps), 1e-6);
        // (the voltmeter takes next to nothing)
        Assert.True(Math.Abs(s.Of(Named(d, "Voltmeter 1").Id).Amps) < 1e-7);
        Assert.Null(CircuitSolver.Preset("nothing"));
        Assert.Null(CircuitSolver.Preset(null));
    }

    [Fact]
    public void TerminalsOnOneCellAreOneNode()
    {
        // (a battery turned upright, a resistor standing on its + terminal's cell: joined without a wire)
        var d = new CircuitDoc();
        var bat = d.Add(PartKind.Battery, 4, 7, 1, 9);
        var r = d.Add(PartKind.Resistor, 4, 3, 1, 100);
        Assert.Equal(new[] { (4, 5), (4, 9) }, CircuitParts.Terminals(bat));
        Assert.Equal(new[] { (4, 1), (4, 5) }, CircuitParts.Terminals(r));
        var nodes = CircuitSolver.Nodes(d);
        Assert.Equal(nodes[(bat.Id, 0)], nodes[(r.Id, 1)]);
        Assert.NotEqual(nodes[(bat.Id, 1)], nodes[(r.Id, 0)]);
        // (wired round, it is a circuit: 0.09 A)
        Assert.NotNull(d.Connect(r.Id, 0, bat.Id, 1, (0, 1)));
        Assert.Null(d.Connect(bat.Id, 1, r.Id, 0));
        Assert.Null(d.Connect(bat.Id, 1, bat.Id, 1));
        var s = CircuitSolver.Solve(d);
        Assert.Equal(0.09, Math.Abs(s.Of(r.Id).Amps), Tol);
        // (a ground's one terminal is above it, and turns with it)
        var g = d.Add(PartKind.Ground, 2, 2, 0);
        Assert.Equal(new[] { (2, 1) }, CircuitParts.Terminals(g));
        g.Turn = 1;
        Assert.Equal(new[] { (3, 2) }, CircuitParts.Terminals(g));
    }

    [Fact]
    public void AnOpenSwitchStopsTheCurrent()
    {
        var d = new CircuitDoc();
        var bat = d.Add(PartKind.Battery, 4, 7, 1, 9);
        var sw = d.Add(PartKind.Switch, 9, 3, 0);
        var lamp = d.Add(PartKind.Lamp, 14, 7, 1, 50);
        d.Connect(bat.Id, 0, sw.Id, 0, (4, 3));
        d.Connect(sw.Id, 1, lamp.Id, 0, (14, 3));
        d.Connect(lamp.Id, 1, bat.Id, 1);
        var on = CircuitSolver.Solve(d);
        Assert.Equal(0.18, Math.Abs(on.Of(lamp.Id).Amps), Tol);
        Assert.Equal(1.62, Math.Abs(on.Of(lamp.Id).Watts), Tol);
        Assert.Equal(1, on.Of(lamp.Id).Brightness, 1e-9);
        Assert.Equal(0.18, Math.Abs(on.Of(sw.Id).Amps), Tol);
        sw.Closed = false;
        var off = CircuitSolver.Solve(d);
        Assert.Null(off.Problem);
        Assert.Equal(0, off.Of(lamp.Id).Amps, 1e-12);
        Assert.Equal(0, off.Of(lamp.Id).Brightness, 1e-12);
        // (a dimmer lamp: 9 V across 50 Ω + 450 Ω puts 0.0162 W in it, a 3% glow)
        sw.Closed = true;
        lamp.Value = 50;
        var r = d.Add(PartKind.Resistor, 9, 11, 0, 450);
        d.Links.RemoveAll(l => l.Touches(lamp.Id) && l.Touches(bat.Id));
        d.Connect(lamp.Id, 1, r.Id, 1, (14, 11));
        d.Connect(r.Id, 0, bat.Id, 1, (4, 11));
        var dim = CircuitSolver.Solve(d);
        Assert.Equal(0.0162, Math.Abs(dim.Of(lamp.Id).Watts), 1e-4);
        Assert.Equal(0.0324, dim.Of(lamp.Id).Brightness, 1e-4);
    }

    [Fact]
    public void WhatCannotBeSolvedIsSaidSo()
    {
        // (a wire straight across a battery)
        var d = new CircuitDoc();
        var bat = d.Add(PartKind.Battery, 4, 7, 1, 9);
        var w = d.Add(PartKind.Wire, 9, 7, 1);
        d.Connect(bat.Id, 0, w.Id, 0);
        d.Connect(bat.Id, 1, w.Id, 1);
        var s = CircuitSolver.Solve(d);
        Assert.NotNull(s.Problem);
        Assert.Contains("shorted", s.Problem);
        // (two batteries against each other in one loop)
        var e = new CircuitDoc();
        var b1 = e.Add(PartKind.Battery, 4, 7, 1, 9);
        var b2 = e.Add(PartKind.Battery, 9, 7, 1, 5);
        e.Connect(b1.Id, 0, b2.Id, 0);
        e.Connect(b1.Id, 1, b2.Id, 1);
        Assert.NotNull(CircuitSolver.Solve(e).Problem);
        // (parts with no battery are counted, not an error; a resistor hanging off one end takes no current)
        var f = new CircuitDoc();
        var r1 = f.Add(PartKind.Resistor, 4, 3, 0, 100);
        var r2 = f.Add(PartKind.Resistor, 9, 3, 0, 100);
        f.Connect(r1.Id, 1, r2.Id, 0);
        var t = CircuitSolver.Solve(f);
        Assert.Null(t.Problem);
        Assert.Equal(2, t.Unpowered);
        var g = CircuitSolver.Preset("series")!;
        var loose = g.Add(PartKind.Resistor, 9, 7, 0, 100);
        g.Connect(loose.Id, 0, Named(g, "Resistor 1").Id, 1);
        var u = CircuitSolver.Solve(g);
        Assert.Null(u.Problem);
        Assert.Equal(0, u.Of(loose.Id).Amps, 1e-12);
        Assert.Equal(0.03, Math.Abs(u.Of(Named(g, "Resistor 1").Id).Amps), Tol);
        // (nothing at all)
        Assert.Null(CircuitSolver.Solve(new CircuitDoc()).Problem);
    }

    [Fact]
    public void APartRemovedTakesItsWiresWithIt()
    {
        var d = CircuitSolver.Preset("series")!;
        Assert.Equal(4, d.Links.Count);
        d.Remove(Named(d, "Ammeter 1").Id);
        Assert.Equal(2, d.Links.Count);
        Assert.Equal(3, d.Parts.Count);
        var s = CircuitSolver.Solve(d);
        Assert.Null(s.Problem);
        Assert.Equal(0, s.Of(Named(d, "Resistor 1").Id).Amps, 1e-12);
        // (names follow the kind and its place: the second resistor is "Resistor 2")
        Assert.Equal("Resistor 2", d.NameOf(d.Parts.Where(p => p.Kind == PartKind.Resistor).Last()));
        // (a wire's corner avoids a part's body)
        var corner = CircuitParts.Corner(d, (4, 5), (9, 3));
        Assert.Equal((9, 5), corner);
        Assert.Null(CircuitParts.Corner(d, (4, 5), (4, 9)));
    }

    [Fact]
    public void TheCircuitRoundTripsThroughJson()
    {
        var d = CircuitSolver.Preset("divider")!;
        var json = Simpl.Support.Json.Write(d);
        Assert.Contains("\"kind\":\"Battery\"", json);
        var back = Simpl.Support.Json.Read<CircuitDoc>(json)!;
        Assert.Equal(d.Parts.Count, back.Parts.Count);
        Assert.Equal(d.Links.Count, back.Links.Count);
        Assert.Equal(d.Parts.Select(p => (p.Id, p.Kind, p.Col, p.Row, p.Turn, p.Value, p.Closed)), back.Parts.Select(p => (p.Id, p.Kind, p.Col, p.Row, p.Turn, p.Value, p.Closed)));
        Assert.Equal(8, Math.Abs(CircuitSolver.Solve(back).Of(Named(back, "Voltmeter 1").Id).Volts), Tol);
    }

    [Fact]
    public void OhmsLawFromAnyTwo()
    {
        Assert.Equal((9.0, "V"), CircuitMath.Ohm(null, 0.03, 300));
        Assert.Equal((0.03, "I"), CircuitMath.Ohm(9, null, 300));
        Assert.Equal((300.0, "R"), CircuitMath.Ohm(9, 0.03, null));
        Assert.Null(CircuitMath.Ohm(9, null, null));
        Assert.Null(CircuitMath.Ohm(9, 0.03, 300));
        Assert.Null(CircuitMath.Ohm(9, null, 0));
        Assert.Null(CircuitMath.Ohm(9, 0, null));
    }

    [Fact]
    public void ResistancesAreReadAndWritten()
    {
        Assert.Equal(new[] { 100.0, 200 }, CircuitMath.Ohms("100, 200"));
        Assert.Equal(new[] { 1000.0, 2200, 4.7e6, 0.5 }, CircuitMath.Ohms("1k 2.2k; 4.7M, 500m"));
        Assert.Equal(new[] { 470.0 }, CircuitMath.Ohms("470 Ω"));
        Assert.Empty(CircuitMath.Ohms(""));
        Assert.Empty(CircuitMath.Ohms("abc"));
        Assert.Equal("66.67 Ω", CircuitMath.OhmsText(66.6667));
        Assert.Equal("1 kΩ", CircuitMath.OhmsText(1000));
        Assert.Equal("2.2 kΩ", CircuitMath.OhmsText(2200));
        Assert.Equal("4.7 MΩ", CircuitMath.OhmsText(4.7e6));
        Assert.Equal("0.03 A", CircuitMath.AmpsText(0.03));
        Assert.Equal("0.135 A", CircuitMath.AmpsText(0.135));
        Assert.Equal("8 V", CircuitMath.VoltsText(8));
        Assert.Equal("0.27 W", CircuitMath.WattsText(0.27));
        Assert.Equal(0, CircuitMath.Parallel(new[] { 100.0, 0 }), 1e-12);
        Assert.Equal(0, CircuitMath.Parallel(Array.Empty<double>()), 1e-12);
        Assert.Equal(50, CircuitMath.Parallel(new[] { 100.0, 100 }), 1e-9);
    }
}
