namespace KerbalStatusEffects
{
    // ScenarioModule for Kerbal Status Effects
    // Is destroyed and re-created at each scene change, which is why pretty much everything lives in KSEAddon instead
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class KSEScenario : ScenarioModule
    {
        public override void OnSave(ConfigNode node)
        {
            KSEAddon.Instance.OnSave(node);
        }

        public override void OnLoad(ConfigNode node)
        {
            KSEAddon.Instance.OnLoad(node);
        }
    }
}
