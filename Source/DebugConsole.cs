using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using KSP.UI.Screens.DebugToolbar;

namespace KerbalStatusEffects.Console
{
    // Kerbal Status Effects debug console tool
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class DebugConsole : MonoBehaviour
    {
        private const string Command = "kse";
        private const string HelpString = "Kerbal Status Effects console tool.";

        #region Command Strings
        private const string CmdHelp     = "help";
        private const string CmdList     = "list";
        private const string CmdStatus   = "status";
        private const string CmdEligible = "eligible";
        private const string CmdAffect   = "affected";
        private const string CmdApply    = "apply";
        private const string CmdRemove   = "remove";
        private const string CmdDump     = "dump";
        private const string CmdWildcard = "*";
        #endregion

        #region Message Strings
        private static readonly string MsgUsage = $"Type \"/{Command} {CmdHelp}\" for usage.";
        private static readonly string MsgUnparseable = $"Unable to parse command. {MsgUsage}";
        private static readonly string MsgInvalidCmd = $"{{0}} is not a valid command. {MsgUsage}";
        private static readonly string MsgNoWildcard = $"Wildcards are not supported by this command. {MsgUsage}";
        private const string MsgInvalidStatus = "{0} is not a valid status effect.";
        private const string MsgInvalidKerbal = "Could not find kerbal {0}.";
        private const string MsgBullet = " - ";

        private const string SampleStatusEffect = "sample_incapacitate";
        private static readonly string MsgHelpText = $@"{HelpString}
Usage:

/{Command} {CmdHelp}
Displays this help message.

/{Command} {CmdList}
Displays a list of all available status effects.

/{Command} {CmdStatus} <kerbal>
Lists the status effects that the kerbal is affected by.
e.g.:
    /{Command} {CmdStatus} ""Jebediah Kerman""

/{Command} {CmdEligible} <kerbal> <effect>
Is the kerbal eligible for the given status effect?
e.g.:
    /{Command} {CmdEligible} ""Jebediah Kerman"" {SampleStatusEffect}

/{Command} {CmdAffect} <kerbal> <effect>
Is the kerbal affected by the given status effect?
e.g.:
    /{Command} {CmdAffect} ""Jebediah Kerman"" {SampleStatusEffect}

/{Command} {CmdApply} <kerbal> <effect>
Applies a status effect to a Kerbal.
e.g.:
    /{Command} {CmdApply} ""Jebediah Kerman"" {SampleStatusEffect}

/{Command} {CmdRemove} <kerbal> <effect>
Removes a status effect from a Kerbal.
e.g.:
    /{Command} {CmdRemove} ""Jebediah Kerman"" {SampleStatusEffect}
Wildcards are supported for either argument (but not both):
e.g.:
    /{Command} {CmdRemove} ""Jebediah Kerman"" {CmdWildcard}
    /{Command} {CmdRemove} {CmdWildcard} {SampleStatusEffect}

/{Command} {CmdDump}
Dumps a list of Kerbals and the status effects that they are affected by.";

        private static readonly string MsgListCount = $"{{0}} status effects available{Environment.NewLine}";
        private static readonly string MsgStatusCount = $"Kerbal {{0}} is affected by {{1}} status effects{Environment.NewLine}";
        private const string MsgTestY = "";
        private const string MsgTestN = "not ";
        private const string MsgEligibleFor = "Kerbal {0} is {2}eligible for the {1} status effect";
        private const string MsgAffectedBy = "Kerbal {0} is {2}affected by the {1} status effect";
        private const string MsgApplySuccess = "Successfully applied the {1} status effect to kerbal {0}.";
        private const string MsgApplyRedundant = "Kerbal {0} is already affected by the {1} status effect.";
        private const string MsgApplyFailure = "Unable to apply the {1} status effect to kerbal {0}.";
        private const string MsgApplyIneligible = "Kerbal {0} is not eligible for the {1} status effect.";
        private const string MsgRemoveMultiEffNone = "Kerbal {0} is not affected by any effects.";
        private const string MsgRemoveMultiTgtNone = "There are no kerbals affected by {0} effect.";
        private const string MsgRemoveMultiEff = "Clearing all {1} effects from kerbal {0}...";
        private const string MsgRemoveMultiTgt = "Removing {0} effect from {1} kerbals...";
        private const string MsgRemoveMultiDone = "Done!";
        private const string MsgRemoveMultiWildcard = "Multiple wildcards are not supported. {MsgUsage}";
        private const string MsgRemoveSuccess = "Successfully removed the {1} status effect from kerbal {0}.";
        private const string MsgRemoveRedundant = "Kerbal {0} already does not have the {1} status effect.";
        private const string MsgRemoveFailure = "Unable to remove the {1} status effect to kerbal {0}.";
        private const string MsgDumpCount = "{0} Kerbals are affected by status effects";
        #endregion

        private static readonly Regex Parser = new Regex(@"^\s*(?<command>\S+)(?:\s+(?:""(?<target>.+)""|(?<target>\*)))?(?:\s+(?<effect>\w+|\*))?\s*$");

        KSEAddon kse;

        #region Lifecycle
        private void Awake()
        {
            kse = KSEAddon.Instance;
            DebugScreenConsole.AddConsoleCommand(Command, OnCommand, HelpString);
        }

        private void OnDestroy()
        {
            DebugScreenConsole.RemoveConsoleCommand(Command);
        }
        #endregion

        private void OnCommand(string argStr)
        {
            if (String.IsNullOrEmpty(argStr))
                argStr = CmdHelp;

            Match m = Parser.Match(argStr);
            if (!m.Success)
            {
                KSEAddon.Log(MsgUnparseable);
                return;
            }

            string command = m.Groups["command"].Value;
            switch (command)
            {
                case CmdHelp:
                    Help();
                    break;
                case CmdList:
                    List();
                    break;
                case CmdStatus:
                    Status(m.Groups["target"].Value);
                    break;
                case CmdEligible:
                    IsEligibleFor(m.Groups["target"].Value, m.Groups["effect"].Value);
                    break;
                case CmdAffect:
                    IsAffectedBy(m.Groups["target"].Value, m.Groups["effect"].Value);
                    break;
                case CmdApply:
                    Apply(m.Groups["target"].Value, m.Groups["effect"].Value);
                    break;
                case CmdRemove:
                    Remove(m.Groups["target"].Value, m.Groups["effect"].Value);
                    break;
                case CmdDump:
                    Dump();
                    break;
                default:
                    KSEAddon.Log(MsgInvalidCmd, command);
                    break;
            }
        }

        private void Help()
        {
            KSEAddon.Log(MsgHelpText);
        }

        private void List()
        {
            ICollection<string> effects = kse.StatusEffects;
            StringBuilder sb = new StringBuilder(MsgListCount);
            foreach (string s in effects)
                sb.Append(MsgBullet).Append(s).Append(Environment.NewLine);
            KSEAddon.Log(sb.ToString(), effects.Count);
        }

        private void Status(string target)
        {
            if (String.Equals(target, CmdWildcard))
            {
                KSEAddon.Log(MsgNoWildcard);
                return;
            }

            List<string> effects = kse.KerbalStatus(target);
            if (effects == null)
            {
                // we are definitely in a Game Scene, so it must be that the target kerbal does't exist
                KSEAddon.Log(MsgInvalidKerbal, target);
                return;
            }

            StatusPrint(target, effects);
        }

        private void StatusPrint(string name, List<string> effects)
        {
            StringBuilder sb = new StringBuilder(MsgStatusCount);
            foreach (string s in effects)
                sb.Append(MsgBullet).Append(s).Append(Environment.NewLine);
            KSEAddon.Log(sb.ToString(), name, effects.Count);
        }

        private void IsEligibleFor(string target, string effect)
        {
            KSEAddon.Log(MsgEligibleFor, target, effect,
                kse.IsEligibleFor(target, effect) ? MsgTestY : MsgTestN);
        }

        private void IsAffectedBy(string target, string effect)
        {
            KSEAddon.Log(MsgAffectedBy, target, effect,
                kse.IsAffectedBy(target, effect) ? MsgTestY : MsgTestN);
        }

        private void Apply(string target, string effect)
        {
            switch (kse.Apply(target, effect))
            {
                case KSEAddon.Outcome.SUCCESS:
                    KSEAddon.Log(MsgApplySuccess, target, effect);
                    break;
                case KSEAddon.Outcome.REDUNDANT:
                    KSEAddon.Log(MsgApplyRedundant, target, effect);
                    break;
                case KSEAddon.Outcome.FAILURE:
                    KSEAddon.Log(MsgApplyFailure, target, effect);
                    break;
                case KSEAddon.Outcome.INVALID_KERBAL:
                    KSEAddon.Log(MsgInvalidKerbal, target);
                    break;
                case KSEAddon.Outcome.INVALID_STATUS:
                    KSEAddon.Log(MsgInvalidStatus, effect);
                    break;
                case KSEAddon.Outcome.INELIGIBLE_KERBAL:
                    KSEAddon.Log(MsgApplyIneligible, target, effect);
                    break;
            }
        }

        private void Remove(string target, string effect)
        {
            if (target != CmdWildcard && effect != CmdWildcard)
                RemoveOne(target, effect);
            else if(target != CmdWildcard && effect == CmdWildcard)
            {
                // remove all effects from one kerbal
                List<string> effects = kse.KerbalStatus(target);
                if (effects == null)
                {
                    // we are definitely in a Game Scene, so it must be that the target kerbal does't exist
                    KSEAddon.Log(MsgInvalidKerbal, target);
                    return;
                }
                if (effects.Count == 0)
                {
                    KSEAddon.Log(MsgRemoveMultiEffNone, target);
                    return;
                }
                KSEAddon.Log(MsgRemoveMultiEff, target, effects.Count);
                kse.ClearStatusEffects(target);
                KSEAddon.Log(MsgRemoveMultiDone);
            }
            else if(target == CmdWildcard && effect != CmdWildcard)
            {
                // remove one effect from all affected kerbals
                if (!kse.StatusEffects.Contains(effect))
                {
                    KSEAddon.Log(MsgInvalidStatus, effect);
                    return;
                }
                string[] kerbals = kse.affectedKerbals
                    .Where(ak => ak.Value.activeEffects.Contains(effect))
                    .Select(ak => ak.Key)
                    .ToArray();
                if (kerbals.Count() == 0)
                {
                    KSEAddon.Log(MsgRemoveMultiTgtNone, effect);
                    return;
                }
                KSEAddon.Log(MsgRemoveMultiTgt, effect, kerbals.Count());
                for (int i = 0; i < kerbals.Count(); i++)
                    RemoveOne(kerbals[i], effect);
                KSEAddon.Log(MsgRemoveMultiDone);
            }
            else
                KSEAddon.Log(MsgRemoveMultiWildcard);
        }

        private void RemoveOne(string target, string effect)
        {
            switch (kse.Remove(target, effect))
            {
                case KSEAddon.Outcome.SUCCESS:
                    KSEAddon.Log(MsgRemoveSuccess, target, effect);
                    break;
                case KSEAddon.Outcome.REDUNDANT:
                    KSEAddon.Log(MsgRemoveRedundant, target, effect);
                    break;
                case KSEAddon.Outcome.FAILURE:
                    KSEAddon.Log(MsgRemoveFailure, target, effect);
                    break;
                case KSEAddon.Outcome.INVALID_KERBAL:
                    KSEAddon.Log(MsgInvalidKerbal, target);
                    break;
                case KSEAddon.Outcome.INVALID_STATUS:
                    KSEAddon.Log(MsgInvalidStatus, effect);
                    break;
            }
        }

        private void Dump()
        {
            KSEAddon.Log(MsgDumpCount, kse.affectedKerbals.Count);
            foreach (var ak in kse.affectedKerbals)
                StatusPrint(ak.Key, ak.Value.activeEffects);
        }
    }
}
