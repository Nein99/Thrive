﻿using System.Collections.Generic;
using Newtonsoft.Json;
using Systems;

/// <summary>
///   Creates oxygen based on photosynthesizers (and removes carbon). And does the vice versa for oxygen consumption
///   to balance things out. This is kind of a simplified version of <see cref="CompoundProductionEffect"/>
/// </summary>
[JSONDynamicTypeAllowed]
public class NitrogenFixationEffect : IWorldEffect
{
    [JsonProperty]
    private GameWorld targetWorld;

    public NitrogenFixationEffect(GameWorld targetWorld)
    {
        this.targetWorld = targetWorld;
    }

    public void OnRegisterToWorld()
    {
    }

    public void OnTimePassed(double elapsed, double totalTimePassed)
    {
        ApplyCompoundsAddition();
    }

    private void ApplyCompoundsAddition()
    {
        // These affect the final balance
        var outputModifier = 1.0f;
        var inputModifier = 1.0f;

        // This affects how fast the conditions change, but also the final balance somewhat
        var modifier = 0.00015f;

        List<TweakedProcess> microbeProcesses = [];

        var cloudSizes = new Dictionary<Compound, float>();

        var changesToApply = new Dictionary<Compound, float>();

        foreach (var patchKeyValue in targetWorld.Map.Patches)
        {
            var patch = patchKeyValue.Value;

            // Skip empty patches as they don't need handling
            if (patch.SpeciesInPatch.Count < 1)
                continue;

            float nitrogenBalance = 0;

            foreach (var species in patch.SpeciesInPatch)
            {
                // Only microbial photosynthesis and respiration are taken into account
                if (species.Key is not MicrobeSpecies microbeSpecies)
                    continue;

                var balance = ProcessSystem.ComputeEnergyBalance(microbeSpecies.Organelles, patch.Biome,
                    microbeSpecies.MembraneType, false, false, targetWorld.WorldSettings, CompoundAmountType.Average,
                    false);

                float balanceModifier = 1;

                // Scale processes to not consume excess oxygen than what is actually needed. Though, see below which
                // actual processes use this modifier.
                if (balance.TotalConsumption < balance.TotalProduction)
                    balanceModifier = balance.TotalConsumption / balance.TotalProduction;

                // Cleared for efficiency
                microbeProcesses.Clear();
                ProcessSystem.ComputeActiveProcessList(microbeSpecies.Organelles, ref microbeProcesses);

                foreach (var process in microbeProcesses)
                {
                    if (process.Process.InternalName == "anaerobic_nitrogen_fixation")
                        _ = 1;

                    // Only handle relevant processes
                    if (!IsProcessRelevant(process.Process))
                        continue;

                    var rate = ProcessSystem.CalculateProcessMaximumSpeed(process,
                        patch.Biome, CompoundAmountType.Biome, true);

                    // Skip checking processes that cannot run
                    if (rate.CurrentSpeed <= 0)
                        continue;

                    // For metabolic processes the speed is at most to reach ATP equilibrium in order to not
                    // unnecessarily consume environmental oxygen
                    var effectiveSpeed =
                        (process.Process.IsMetabolismProcess ? balanceModifier : 1) * rate.CurrentSpeed;

                    // TODO: maybe photosynthesis should also only try to reach glucose balance of +0?

                    // Inputs take away compounds scaled by the speed and population of the species
                    foreach (var input in process.Process.Inputs)
                    {
                        if (input.Key.ID is Compound.Nitrogen)
                        {
                            nitrogenBalance -= input.Value * inputModifier * effectiveSpeed * species.Value;
                        }
                    }

                    // Outputs generate the given compounds
                    foreach (var output in process.Process.Outputs)
                    {
                        if (output.Key.ID is Compound.Nitrogen)
                        {
                            nitrogenBalance += output.Value * outputModifier * effectiveSpeed * species.Value;
                        }
                    }
                }
            }

            // Scale the balances to make the changes less drastic
            nitrogenBalance *= modifier;

            changesToApply.Clear();

            if (nitrogenBalance != 0)
                changesToApply[Compound.Nitrogen] = nitrogenBalance;

            if (changesToApply.Count > 0)
                patch.Biome.ApplyLongTermCompoundChanges(patch.BiomeTemplate, changesToApply, cloudSizes);
        }
    }

    private bool IsProcessRelevant(BioProcess process)
    {
        // For now only oxygen and co2 processes are handled by this simplified production effect
        foreach (var input in process.Inputs)
        {
            if (input.Key.ID is Compound.Nitrogen)
                return true;
        }

        foreach (var output in process.Outputs)
        {
            if (output.Key.ID is Compound.Nitrogen)
                return true;
        }

        return false;
    }
}
