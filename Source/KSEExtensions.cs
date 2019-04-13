using System.Collections.Generic;

namespace KerbalStatusEffects
{
    // Kerbal Status Effects extension methods
    public static class KSEExtensions
    {
        // Lists the status effects affecting the kerbal
        // pcm.StatusEffects()
        public static List<string> StatusEffects(this ProtoCrewMember pcm)
        {
            return KSEAddon.Instance.KerbalStatus(pcm.name);
        }

        // Is the kerbal eligible to have the effect applied?
        // pcm.IsEligibleForStatusEffect(statusEffectName)
        public static bool IsEligibleForStatusEffect(this ProtoCrewMember pcm, string statusEffectName)
        {
            return KSEAddon.Instance.IsEligibleFor(pcm.name, statusEffectName);
        }

        // Is the kerbal affected by the effect?
        // pcm.IsAffectedByStatusEffect(statusEffectName)
        public static bool IsAffectedByStatusEffect(this ProtoCrewMember pcm, string statusEffectName)
        {
            return KSEAddon.Instance.IsAffectedBy(pcm.name, statusEffectName);
        }

        // Apply an effect to a kerbal
        // pcm.ApplyStatusEffect(statusEffectName)
        public static KSEAddon.Outcome ApplyStatusEffect(this ProtoCrewMember pcm, string statusEffectName)
        {
            return KSEAddon.Instance.Apply(pcm.name, statusEffectName);
        }

        // Remove an effect from a kerbal
        // pcm.RemoveStatusEffect(statusEffectName)
        public static KSEAddon.Outcome RemoveStatusEffect(this ProtoCrewMember pcm, string statusEffectName)
        {
            return KSEAddon.Instance.Remove(pcm.name, statusEffectName);
        }
    }
}
