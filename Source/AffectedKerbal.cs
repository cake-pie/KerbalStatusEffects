using System;
using System.Collections.Generic;
using Experience;

namespace KerbalStatusEffects
{
    // Class representing a kerbal affected by status effects
    public class AffectedKerbal
    {
        #region ConfigNode Parsing
        internal const string CfgNodeAffectedKerbal = "AFFECTED_KERBAL";
        internal const string CfgValueKerbalName = "name";
        internal const string CfgValueStatusEffect = "status_effect";

        internal const string CfgNodeExpEffect = "EFFECT";
        internal const string CfgValueExpEffectName = "name";
        internal const string CfgValueExpEffectLevel = "level";
        internal const string CfgValueExpEffectModifiers = "modifiers";
        #endregion

        private KSEAddon kse;
        internal ProtoCrewMember pcm;
        internal List<string> activeEffects = new List<string>();
        internal StatusEffect.Impacts overallImpact = StatusEffect.Impacts.UNKNOWN;

        internal static AffectedKerbal Create(string kerbalName, string effectName, KSEAddon kse)
        {
            AffectedKerbal result = new AffectedKerbal();
            result.pcm = kse.roster[kerbalName];
            result.kse = kse;
            if (!result.AddEffect(effectName))
                return null;
            return result;
        }

        #region Save/Load
        internal static AffectedKerbal Create(ConfigNode node, KSEAddon kse)
        {
            // Unlike the other methods, this one performs validation because it loads from ConfigNode rather than being called from KSEAddon
            string kerbalName = null;
            if (!node.TryGetValue(CfgValueKerbalName, ref kerbalName) || String.IsNullOrEmpty(kerbalName))
            {
                KSEAddon.Log($"ERROR: {CfgNodeAffectedKerbal} without {CfgValueKerbalName}, discarding!");
                return null;
            }
            if (!kse.KerbalExists(kerbalName))
            {
                KSEAddon.Log($"ERROR: {CfgNodeAffectedKerbal} {kerbalName} refers to a kerbal not found in the roster, discarding!");
                return null;
            }
            if (!node.HasValue(CfgValueStatusEffect))
            {
                KSEAddon.Log($"ERROR: {CfgNodeAffectedKerbal} {kerbalName} contains no {CfgValueStatusEffect} entries, discarding!");
                return null;
            }

            AffectedKerbal result = new AffectedKerbal();
            result.pcm = kse.roster[kerbalName];
            result.kse = kse;

            string[] cfgStatusEffects = node.GetValues(CfgValueStatusEffect);
            for (int i = 0; i < cfgStatusEffects.Length; i++)
            {
                if (!kse.StatusEffects.Contains(cfgStatusEffects[i]))
                {
                    KSEAddon.Log($"WARNING: {CfgValueStatusEffect} entry {cfgStatusEffects[i]} in {CfgNodeAffectedKerbal} {kerbalName} refers to an inexistent status effect, discarding.");
                    continue;
                }
                result.activeEffects.Add(cfgStatusEffects[i]);
            }
            if (result.activeEffects.Count == 0)
            {
                KSEAddon.Log($"INFO: {CfgNodeAffectedKerbal} {kerbalName} contains no {CfgValueStatusEffect} entries after culling inexistent entries, discarding.");
                return null;
            }

            result.ApplyEffects();
            return result;
        }

        internal ConfigNode Save()
        {
            ConfigNode node = new ConfigNode(CfgNodeAffectedKerbal);
            node.AddValue(CfgValueKerbalName, pcm.name);
            for (int i = 0; i < activeEffects.Count; i++)
                node.AddValue(CfgValueStatusEffect, activeEffects[i]);
            return node;
        }
        #endregion

        internal bool AddEffect(string effectName)
        {
            activeEffects.Add(effectName);
            ApplyEffects();
            return true;
        }

        internal bool RemoveEffect(string effectName)
        {
            activeEffects.Remove(effectName);

            if (activeEffects.Count == 0)
                RevertPCMExperienceTrait();
            else
                ApplyEffects();

            return true;
        }

        // (Re)compute and apply effects to the ProtoCrewMember
        internal void ApplyEffects()
        {
            overallImpact = StatusEffect.Impacts.UNKNOWN;
            for (int i = 0; i < activeEffects.Count; i++)
                overallImpact = overallImpact | kse.statusEffects[activeEffects[i]].Impact;
            overallImpact = Aggregate(overallImpact);

            ConfigNode etcNode = kse.traitConfigNodes[pcm.trait].CreateCopy();
            KSEAddon.Log("AffectedKerbal.ApplyEffects() applying: {0}", String.Join(",", activeEffects.ToArray()));
            KSEAddon.LogDebug("AffectedKerbal.ApplyEffects() BEFORE {1}{0}", etcNode.ToString(), Environment.NewLine);

            // TODO the tricky part: additions and modifications must be able to handle potentially multiple status effects touching the same skill
            // this works with only skill deletions for now
            HashSet<string> aggregateRemoveSkills = new HashSet<string>();
            bool aggregateRemoveExcept = false;
            for (int i = 0; i < activeEffects.Count; i++)
            {
                StatusEffect se = kse.statusEffects[activeEffects[i]];
                if (aggregateRemoveExcept)
                {
                    if (se.RemoveExcept)
                        aggregateRemoveSkills.IntersectWith(se.RemoveSkills);
                    else
                        aggregateRemoveSkills.ExceptWith(se.RemoveSkills);

                    // no point in continuing if total removal has been reached
                    if (aggregateRemoveSkills.Count == 0) break;
                }
                else
                {
                    if (se.RemoveExcept)
                    {
                        HashSet<string> temp = new HashSet<string>(se.RemoveSkills);
                        temp.ExceptWith(aggregateRemoveSkills);

                        // no point in continuing if total removal has been reached
                        if (temp.Count == 0) break;

                        aggregateRemoveSkills = temp;
                        aggregateRemoveExcept = true;
                    }
                    else
                        aggregateRemoveSkills.UnionWith(se.RemoveSkills);
                }
            }

            ConfigNode[] effectNodes = etcNode.GetNodes(CfgNodeExpEffect);
            for (int i = 0; i < effectNodes.Length; i++)
            {
                string effectName = effectNodes[i].GetValue(CfgValueExpEffectName);
                if (aggregateRemoveSkills.Contains(effectName) ^ aggregateRemoveExcept)
                    etcNode.RemoveNode(effectNodes[i]);
            }

            KSEAddon.LogDebug("AffectedKerbal.ApplyEffects() AFTER {1}{0}", etcNode.ToString(), Environment.NewLine);

            SetPCMExperienceTrait(ExperienceTraitConfig.Create(etcNode));
        }

        // Revalidate and reapply the active effects because GameDatabase got reloaded
        internal bool RevalidateEffects()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
                if (!kse.StatusEffects.Contains(activeEffects[i]))
                    activeEffects.RemoveAt(i);

            if (activeEffects.Count == 0)
            {
                // no more active effects after revalidation
                RevertPCMExperienceTrait();
                return false;
            }

            ApplyEffects();
            return true;
        }

        // Revert pcm's experienceTrait to stock copy with no status effects
        internal void RevertPCMExperienceTrait()
        {
            overallImpact = StatusEffect.Impacts.UNKNOWN;
            ExperienceTraitConfig config = GameDatabase.Instance.ExperienceConfigs.GetExperienceTraitConfig(pcm.trait);
            SetPCMExperienceTrait(config);
        }

        private void SetPCMExperienceTrait(ExperienceTraitConfig config)
        {
            Part p = pcm.KerbalRef?.InPart;
            if (p != null) pcm.UnregisterExperienceTraits(p);

            Type type = KerbalRoster.GetExperienceTraitType(pcm.trait) ?? typeof(ExperienceTrait);
            pcm.experienceTrait = ExperienceTrait.Create(type, config, pcm);

            if (p != null) pcm.RegisterExperienceTraits(p);
        }

        // Aggregate the impact of multiple status effects on the same kerbal
        private StatusEffect.Impacts Aggregate(StatusEffect.Impacts i)
        {
            if ((i & StatusEffect.Impacts.INCAPACITATED) != 0) return StatusEffect.Impacts.INCAPACITATED;
            if (i.CompareTo(StatusEffect.Impacts.NEUTRAL) > 0) return i & (StatusEffect.Impacts.BUFF | StatusEffect.Impacts.DEBUFF);
            return (i & StatusEffect.Impacts.NEUTRAL);
        }
    }
}
