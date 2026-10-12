using System.Windows;
using Simpl.Kit;

namespace Simpl.Screens;

public sealed class DashboardScreen : Screen
{
    public DashboardScreen(Engine.Engine engine) : base(engine) { }
    public override string Title => "Dashboard";
    public override void Refresh() { }
    protected override FrameworkElement Build() => K.Text("Dashboard");
}
