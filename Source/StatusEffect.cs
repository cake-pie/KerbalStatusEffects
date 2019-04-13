using System;
using System.Collections.Generic;
using Experience;

namespace KerbalStatusEffects
{
    // Class for representing a status effect definition
    public class StatusEffect
    {
        // General assessment of a status effect's impact on a kerbal's ability to "do their job"
        // Status effects should be defined with *one* of these impacts (other than unknown)
        // Treated as bit flags for resolving aggregate impact of multiple status effects on the same kerbal
        [Flags]
        public enum Impacts
        {
            UNKNOWN = 0x0,  // not known, e.g. impact not specified by the mod that defined this status effect
            NEUTRAL = 0x1,  // doesn't significantly affect the kerbal's ability to function
            BUFF    = 0x2,  // confers some benefit to the kerbal's skills
            DEBUFF  = 0x4,  // the kerbal's skills have been partly lost or penalized
            INCAPACITATED = 0x8  // the kerbal has been pretty much rendered unfit for duty
        }

        #region ConfigNode Parsing
        internal const string CfgNodeKSE = "KERBAL_STATUS_EFFECT";
        internal const string CfgValueName = "name";
        internal const string CfgValueTitle = "title";
        internal const string CfgValueDescription = "description";
        internal const string CfgValueMod = "mod";
        internal const string CfgValueModUnknown = "unknown";
        internal const string CfgValueImpact = "impact";
        // TODO internal const string CfgValueSuppress = "suppress_notifications";
        internal const string CfgValueEligible = "eligible_trait";
        internal const string CfgValueIneligible = "ineligible_trait";

        internal const string CfgNodeRemove = "SKILL_REMOVE";
        internal const string CfgNodeRemoveAllExcept = "SKILL_REMOVE_ALL_EXCEPT";
        internal const string CfgValueOverrideDefaults = "override_defaults";

        // TODO the tricky part: additions and modifications must be able to handle potentially multiple status effects touching the same skill
        #endregion

        #region Basic Properties
        // Internal identifier for this status effect
        // Should be prefixed with a mod identifier to prevent name collisions
        private string _name;
        public string Name
        {
            get { return _name; }
        }

        // Display string for the name of this status effect
        // Keep in mind this may be inserted in a sentence or wrapped in parentheses and appended to kerbal name.
        // e.g. "Bill Kerman is starving." / "Bill Kerman (starving)" works well
        //   whereas "Bill Kerman is starvation" / "Bill Kerman (starvation)" would sound unnatural
        private string _title;
        public string Title
        {
            get { return _title; }
        }

        // Brief sentence describing how a kerbal acquired this status effect and what it does to them
        // e.g. "This kerbal has not had any food in a long time and is thus unable to work."
        private string _description;
        public string Description
        {
            get { return _description; }
        }

        // Name of the mod that defined this status effect, human-readable
        private string _mod;
        public string Mod
        {
            get { return _mod; }
        }

        // Impact of this status effect on the kerbal's ability to "do their job"
        private Impacts _impact;
        public Impacts Impact
        {
            get { return _impact; }
        }

        // Kerbal classes (ExperienceTraits) that this status effect can be applied to
        private HashSet<string> _eligibleTraits;
        public HashSet<string> EligibleTraits
        {
            get { return _eligibleTraits; }
        }
        #endregion

        #region Operations on ExperienceEffects
        // TODO the tricky part: additions and modifications must be able to handle potentially multiple status effects touching the same skill

        // Kerbal skills (ExperienceEffects) that this status effect will remove
        // if RemoveExcept is false, only skills explictly listed in RemoveSkills will be removed
        // if RemoveExcept is true, all other skills not explictly listed in RemoveSkills will be removed
        private bool _removeExcept;
        public bool RemoveExcept
        {
            get { return _removeExcept; }
        }
        private HashSet<string> _removeSkills;
        public HashSet<string> RemoveSkills
        {
            get { return _removeSkills; }
        }
        #endregion

        internal static StatusEffect Create(ConfigNode node, KSEAddon kse)
        {
            StatusEffect result = new StatusEffect();
            if (!node.TryGetValue(CfgValueName, ref result._name) || String.IsNullOrEmpty(result._name))
            {
                KSEAddon.Log($"ERROR: {CfgNodeKSE} without {CfgValueName}, discarding!");
                return null;
            }
            if (!node.TryGetValue(CfgValueTitle, ref result._title) || String.IsNullOrEmpty(result._title))
            {
                KSEAddon.Log($"ERROR: {CfgNodeKSE} {result._name} lacks {CfgValueTitle}, discarding!");
                return null;
            }
            if (!node.TryGetValue(CfgValueDescription, ref result._description) || String.IsNullOrEmpty(result._description))
            {
                KSEAddon.Log($"ERROR: {CfgNodeKSE} {result._name} lacks {CfgValueDescription}, discarding!");
                return null;
            }

            if (!node.TryGetValue(CfgValueMod, ref result._mod))
            {
                KSEAddon.Log($"WARNING: {CfgNodeKSE} {result._name} lacks {CfgValueMod}.");
                result._mod = CfgValueModUnknown;
            }
            if (!node.TryGetEnum<Impacts>(CfgValueImpact, ref result._impact, Impacts.UNKNOWN))
            {
                KSEAddon.Log($"WARNING: {CfgNodeKSE} {result._name} lacks {CfgValueImpact}.");
            }

            result._eligibleTraits = ParseEligibleTraits(node, result._name, kse);
            if (result._eligibleTraits.Count == 0)
            {
                KSEAddon.Log($"INFO: {CfgNodeKSE} {result._name} contains no eligible traits after processing, discarding!");
                return null;
            }

            // TODO the tricky part: additions and modifications must be able to handle potentially multiple status effects touching the same skill

            result._removeSkills = ParseRemoveSkills(node, ref result._removeExcept, result._name, kse);

            return result;
        }

        private static HashSet<string> ParseEligibleTraits(ConfigNode node, string context, KSEAddon kse)
        {
            if (node.HasValue(CfgValueEligible) && node.HasValue(CfgValueIneligible))
                KSEAddon.Log($"WARNING: {CfgNodeKSE} {context} has both {CfgValueEligible} and {CfgValueIneligible} entries. This is likely to be in error and may lead to unexpected behavior.");

            HashSet<string> eligible = new HashSet<string>();

            ICollection<string> knownTraits = kse.traitConfigNodes.Keys;
            if (node.HasValue(CfgValueEligible))
            {
                List<string> cfgValues = node.GetValuesList(CfgValueEligible);
                // discard those that are not among the installed ExperienceTraits
                cfgValues.RemoveAll(x => !knownTraits.Contains(x));
                eligible.UnionWith(cfgValues);
            }
            else
                eligible.UnionWith(knownTraits);

            eligible.ExceptWith(node.GetValues(CfgValueIneligible));

            return eligible;
        }

        private static HashSet<string> ParseRemoveSkills(ConfigNode node, ref bool removeExcept, string context, KSEAddon kse)
        {
            if (node.HasNode(CfgNodeRemove) && node.HasNode(CfgNodeRemoveAllExcept))
                KSEAddon.Log($"WARNING: {CfgNodeKSE} {context} has both {CfgNodeRemove} and {CfgNodeRemoveAllExcept} nodes. This is likely to be in error and may lead to unexpected behavior.");

            HashSet<string> skills = new HashSet<string>();
            ConfigNode[] removeNodes;

            if (node.HasNode(CfgNodeRemoveAllExcept))
            {
                bool overrideDefaults = false;
                removeExcept = true;
                removeNodes = node.GetNodes(CfgNodeRemoveAllExcept);
                if (removeNodes.Length > 1)
                    KSEAddon.Log($"WARNING: {CfgNodeKSE} {context} has more than one {CfgNodeRemoveAllExcept} node. This is likely to be in error and may lead to unexpected behavior.");
                for (int i = 0; i < removeNodes.Length; i++)
                {
                    bool od = false;
                    overrideDefaults = overrideDefaults || (removeNodes[i].TryGetValue(CfgValueOverrideDefaults, ref od) && od);
                    skills.UnionWith(removeNodes[i].GetValues(CfgValueName));
                }
                if (!overrideDefaults)
                    skills.UnionWith(kse.removeAllExceptions);
            }

            if (node.HasNode(CfgNodeRemove))
            {
                removeNodes = node.GetNodes(CfgNodeRemove);
                if (removeNodes.Length > 1)
                    KSEAddon.Log($"INFO: {CfgNodeKSE} {context} has more than one {CfgNodeRemove} node. This is unnecessary, multiple skills can be bundled into a single {CfgNodeRemove} node.");
                for (int i = 0; i < removeNodes.Length; i++)
                {
                    if (removeExcept)
                        skills.ExceptWith(removeNodes[i].GetValues(CfgValueName));
                    else
                        skills.UnionWith(removeNodes[i].GetValues(CfgValueName));
                }
            }

            return skills;
        }

    }
}
