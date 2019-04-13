using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KerbalStatusEffects
{
    // Kerbal Status Effects main Addon class
    // Fires up when game first loads into the main menu scene and remains available thereafter
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KSEAddon : MonoBehaviour
    {
        // Outcomes enum used for results of some operations
        public enum Outcome
        {
            SUCCESS,   // the operation completed succesfully
            REDUNDANT, // the operation was not carried out as it would be redundant e.g. kerbal already affected
            FAILURE,   // the operation could not be completed for some unknown reason

            // Safer/easier than expecting downstream mods to catch exceptions
            INVALID_STATUS,   // no such status effect
            INVALID_KERBAL,   // no such kerbal
            INELIGIBLE_KERBAL // kerbal is not eligible for status effect to be applied
        }

        public static KSEAddon Instance;

        #region ConfigNode Parsing
        private const string SettingsUrl = "KerbalStatusEffects/KerbalStatusEffects/KERBAL_STATUS_EFFECTS_SETTINGS";
        private const string CfgRemoveAllException = "remove_all_exception";
        private const string CfgNodeExperienceTrait = "EXPERIENCE_TRAIT";
        private const string CfgValueTraitName = "name";
        #endregion

        #region Common from GameDatabase
        // Config nodes from GameDatabase for all known ExperienceTraits
        internal Dictionary<string, ConfigNode> traitConfigNodes = new Dictionary<string, ConfigNode>();

        // All known status effects, by name
        internal Dictionary<string, StatusEffect> statusEffects = new Dictionary<string, StatusEffect>();
        public ICollection<string> StatusEffects
        {
            get { return statusEffects.Keys.ToList(); }
        }

        // Skills (ExperienceEffect) that are by default exempted from a "remove all" operation (unless overridden)
        internal string[] removeAllExceptions;
        #endregion

        #region Game-Specific
        // Shortcut to HighLogic.fetch.currentGame.CrewRoster
        internal KerbalRoster roster;

        // Tracks all kerbals that have active status effects
        internal Dictionary<string, AffectedKerbal> affectedKerbals;
        public ICollection<string> AffectedKerbals
        {
            get { return affectedKerbals.Keys.ToList(); }
        }
        #endregion

        #region Lifecycle
        private void Awake()
        {
            LogDebug("KSEAddon Awake()");

            if (Instance != null)
            {
                // Reloading of GameDatabase causes another copy of addon to spawn at next opportunity. Suppress it.
                // see: https://forum.kerbalspaceprogram.com/index.php?/topic/7542-x/&do=findComment&comment=3574980
                Log("Destroying spurious copy of KSEAddon!");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadDB();

            GameEvents.OnGameDatabaseLoaded.Add(OnGameDatabaseLoadedHandler);
            GameEvents.onLevelWasLoaded.Add(OnLevelWasLoadedHandler);
            GameEvents.onKerbalNameChanged.Add(OnKerbalNameChangedHandler);
            GameEvents.onKerbalRemoved.Add(OnKerbalRemovedHandler);
            GameEvents.onKerbalStatusChanged.Add(OnKerbalStatusChangedHandler);
        }

        private void OnDestroy()
        {
            LogDebug("KSEAddon OnDestroy()");

            GameEvents.OnGameDatabaseLoaded.Remove(OnGameDatabaseLoadedHandler);
            GameEvents.onLevelWasLoaded.Remove(OnLevelWasLoadedHandler);
            GameEvents.onKerbalNameChanged.Remove(OnKerbalNameChangedHandler);
            GameEvents.onKerbalRemoved.Remove(OnKerbalRemovedHandler);
            GameEvents.onKerbalStatusChanged.Remove(OnKerbalStatusChangedHandler);
        }
        #endregion

        #region Handlers
        internal void OnSave(ConfigNode node)
        {
            LogDebug("KSEAddon OnSave()");

            foreach (AffectedKerbal ak in affectedKerbals.Values)
                node.AddNode(ak.Save());
        }

        internal void OnLoad(ConfigNode node)
        {
            LogDebug("KSEAddon OnLoad()");

            roster = HighLogic.fetch.currentGame.CrewRoster;

            affectedKerbals?.Clear();
            affectedKerbals = new Dictionary<string, AffectedKerbal>();
            ConfigNode[] akNodes = node.GetNodes(AffectedKerbal.CfgNodeAffectedKerbal);
            for (int i = 0; i < akNodes.Length; i++)
            {
                AffectedKerbal ak = AffectedKerbal.Create(akNodes[i], this);
                if (ak == null)
                    continue;
                affectedKerbals[ak.pcm.name] = ak;
            }
        }

        // We need to update our database of StatusEffects if GameDatabase is reloaded
        private void OnGameDatabaseLoadedHandler()
        {
            LogDebug("KSEAddon OnGameDatabaseLoadedHandler()");

            LoadDB();

            // If game in progress, all active status effects must be recomputed from scratch
            // Some may no longer exist, others may have been modified in the reload
            if (HighLogic.LoadedSceneIsGame)
            {
                List<string> unaffected = new List<string>();
                foreach (var ak in affectedKerbals)
                    if (!ak.Value.RevalidateEffects())
                        unaffected.Add(ak.Key);
                for (int i = 0; i < unaffected.Count; i++)
                    affectedKerbals.Remove(unaffected[i]);
            }
        }

        // When entering non-game scenes, discard game-specific data
        private void OnLevelWasLoadedHandler(GameScenes gs)
        {
            LogDebug("KSEAddon OnLevelWasLoadedHandler()");

            if (HighLogic.LoadedSceneIsGame || affectedKerbals == null)
                return;

            affectedKerbals.Clear();
            affectedKerbals = null;
            roster = null;
        }

        // We need to update our active status effects to reflect the name change
        private void OnKerbalNameChangedHandler(ProtoCrewMember pcm, string oldName, string newName)
        {
            if (!HighLogic.LoadedSceneIsGame || affectedKerbals == null || !affectedKerbals.ContainsKey(oldName))
                return;

            affectedKerbals[newName] = affectedKerbals[oldName];
            affectedKerbals.Remove(oldName);
        }

        // Stop tracking active status effects for kerbals removed from roster
        private void OnKerbalRemovedHandler(ProtoCrewMember pcm)
        {
            if (!HighLogic.LoadedSceneIsGame || affectedKerbals == null)
                return;

            ClearStatusEffects(pcm.name); // Revert pcm to stock, just in case
        }

        // Stop tracking active status effects for dead kerbals (but not missing ones!)
        private void OnKerbalStatusChangedHandler(ProtoCrewMember pcm, ProtoCrewMember.RosterStatus oldStatus, ProtoCrewMember.RosterStatus newStatus)
        {
            if (!HighLogic.LoadedSceneIsGame || newStatus != ProtoCrewMember.RosterStatus.Dead || affectedKerbals == null)
                return;

            ClearStatusEffects(pcm.name); // Revert pcm to stock, just in case
        }
        #endregion

        // (Re)load settings and status effects from GameDatabase
        private void LoadDB()
        {
            LogDebug("KSEAddon LoadDB()");

            GameDatabase gd = GameDatabase.Instance;
            ConfigNode settings = gd.GetConfigNode(SettingsUrl);
            removeAllExceptions = settings?.GetValues(CfgRemoveAllException);
            if (removeAllExceptions == null)
                Log("WARNING: could not find settings.");

            traitConfigNodes.Clear();
            ConfigNode[] etNodes = gd.GetConfigNodes(CfgNodeExperienceTrait);
            for (int i = 0; i < etNodes.Length; i++)
            {
                string traitName = null;
                if (!etNodes[i].TryGetValue(CfgValueTraitName, ref traitName) || String.IsNullOrEmpty(traitName))
                    continue;

                if (traitConfigNodes.ContainsKey(traitName))
                {
                    Log("WARNING: Discarding {CfgNodeExperienceTrait} with duplicate {CfgValueTraitName} {traitName}.");
                    continue;
                }

                traitConfigNodes[traitName] = etNodes[i];
            }
            // safety check against known trait names in GameDatabase
            if (!Enumerable.SequenceEqual(
                traitConfigNodes.Keys.ToList().OrderBy(x => x),
                gd.ExperienceConfigs.TraitNames.OrderBy(x => x)
            ))
            {
                Log("ERROR: Disagreement between traits parsed from configs vs those in GameDatabase!{2}    [{0}]{2}    [{1}]",
                    String.Join(", ", traitConfigNodes.Keys.ToArray()),
                    String.Join(", ", gd.ExperienceConfigs.TraitNames.ToArray()),
                    Environment.NewLine
                );
            }

            statusEffects.Clear();
            ConfigNode[] seNodes = gd.GetConfigNodes(StatusEffect.CfgNodeKSE);
            for (int i = 0; i < seNodes.Length; i++)
            {
                StatusEffect se = StatusEffect.Create(seNodes[i], this);
                if (se == null)
                    continue;
                if (statusEffects.ContainsKey(se.Name))
                {
                    Log("WARNING: Discarding {StatusEffect.CfgNodeKSE} with duplicate {StatusEffect.CfgValueName} {se.Name}.");
                    continue;
                }
                statusEffects[se.Name] = se;
            }
        }

        #region Public-Facing API
        // These take the kerbal's name as param instead of directly accepting ProtoCrewMember, then do a look up against the roster
        // This helps ensure we only operate only on actual kerbals in roster, not arbitrary dangling pcm (e.g. programmatically-created)
        // See also KSEExtensions which for convenience offers extension methods that can be called like instance methods of ProtoCrewMember

        // Lists the status effects affecting the kerbal
        public List<string> KerbalStatus(string kerbalName)
        {
            if (!HighLogic.LoadedSceneIsGame)
                return null;
            if (!KerbalExists(kerbalName))
                return null;
            if (!affectedKerbals.ContainsKey(kerbalName))
                return new List<string>();
            return affectedKerbals[kerbalName].activeEffects;
        }

        // Is the kerbal eligible to have the effect applied?
        public bool IsEligibleFor(string kerbalName, string effectName)
        {
            if (!HighLogic.LoadedSceneIsGame)
                return false;
            if (!KerbalExists(kerbalName) || !statusEffects.ContainsKey(effectName))
                return false;
            return statusEffects[effectName].EligibleTraits.Contains(roster[kerbalName].trait);
        }

        // Is the kerbal affected by the effect?
        public bool IsAffectedBy(string kerbalName, string effectName)
        {
            if (!HighLogic.LoadedSceneIsGame)
                return false;
            if (!affectedKerbals.ContainsKey(kerbalName) || !statusEffects.ContainsKey(effectName))
                return false;
            return affectedKerbals[kerbalName].activeEffects.Contains(effectName);
        }

        // Apply an effect to a kerbal
        public Outcome Apply(string kerbalName, string effectName)
        {
            if (!HighLogic.LoadedSceneIsGame)
                return Outcome.FAILURE;
            if (!KerbalExists(kerbalName))
                return Outcome.INVALID_KERBAL;
            if (!statusEffects.ContainsKey(effectName))
                return Outcome.INVALID_STATUS;
            if (!statusEffects[effectName].EligibleTraits.Contains(roster[kerbalName].trait))
                return Outcome.INELIGIBLE_KERBAL;

            if (affectedKerbals.ContainsKey(kerbalName))
            {
                if (affectedKerbals[kerbalName].activeEffects.Contains(effectName))
                    return Outcome.REDUNDANT;
                if (!affectedKerbals[kerbalName].AddEffect(effectName))
                    return Outcome.FAILURE;
            }
            else
            {
                AffectedKerbal ak = AffectedKerbal.Create(kerbalName, effectName, this);
                if (ak == null)
                    return Outcome.FAILURE;
                affectedKerbals[kerbalName] = ak;
            }
            return Outcome.SUCCESS;
        }

        // Remove an effect from a kerbal
        public Outcome Remove(string kerbalName, string effectName)
        {
            if (!HighLogic.LoadedSceneIsGame)
                return Outcome.FAILURE;
            if (!KerbalExists(kerbalName))
                return Outcome.INVALID_KERBAL;
            if (!statusEffects.ContainsKey(effectName))
                return Outcome.INVALID_STATUS;

            if (!affectedKerbals.ContainsKey(kerbalName) || !affectedKerbals[kerbalName].activeEffects.Contains(effectName))
                return Outcome.REDUNDANT;

            if (!affectedKerbals[kerbalName].RemoveEffect(effectName))
                return Outcome.FAILURE;

            if (affectedKerbals[kerbalName].activeEffects.Count == 0)
                affectedKerbals.Remove(kerbalName);

            return Outcome.SUCCESS;
        }
        #endregion

        #region Utils
        // Test if the given name corresponds to a non-dead kerbal in the roster
        internal bool KerbalExists(string kerbalName)
        {
            if (!HighLogic.LoadedSceneIsGame || String.IsNullOrEmpty(kerbalName))
                return false;

            return roster.Exists(kerbalName) && (roster[kerbalName].rosterStatus != ProtoCrewMember.RosterStatus.Dead);
        }

        // Remove all status effects from a kerbal
        internal void ClearStatusEffects(string kerbalName)
        {
            if (!affectedKerbals.ContainsKey(kerbalName)) return;
            affectedKerbals[kerbalName].RevertPCMExperienceTrait();
            affectedKerbals.Remove(kerbalName);
        }

        // Logging
        internal static void Log(string s, params object[] m)
        {
            Debug.LogFormat($"[KerbalStatusEffects] {s}", m);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        internal static void LogDebug(string s, params object[] m)
        {
            Log($"DEBUG: {s}", m);
        }
        #endregion
    }
}
