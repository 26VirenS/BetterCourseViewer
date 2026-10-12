namespace Simpl.Tools;

/// <summary>
/// The Periodic Table's Lewis structures and Molecule builder pages, made in Tools/Chemistry (on the Mac, pages of the
/// same tool): the pages themselves, a molecule handed to either (the tour's presets, the --ptable-lewis and
/// --ptable-molecule pictures), the Lewis maker's "Open in 3D" turning to the builder's page, and what is done in them
/// told to the table's tour.
/// </summary>
internal static class PeriodicChemistry
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Wire()
    {
        PTChemistry.LewisView = c => Chemistry.Lewis(c, null);
        PTChemistry.MoleculeView = c => Chemistry.Molecule(c, null);
        PTChemistry.OpenLewis = name =>
        {
            LewisState.Shared.Start(name);
            LewisState.Shared.Change();
            PTNav.Page = PTPage.Lewis;
        };
        PTChemistry.OpenMolecule = name =>
        {
            MoleculeModel.Shared.Start(name);
            PTNav.Page = PTPage.Molecules;
        };
        Chemistry.OpenIn3D = _ => PTChemistry.ShowMolecules();
        Chemistry.Did += what =>
        {
            switch (what)
            {
                case "preset": PTTour.Did(PTTourEvent.Preset); break;
                case "draw": PTTour.Did(PTTourEvent.Draw); break;
                case "resonance": PTTour.Did(PTTourEvent.Resonance); break;
                case "turned": PTTour.Did(PTTourEvent.Turned); break;
            }
        };
    }
}
