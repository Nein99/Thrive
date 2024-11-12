using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Xoshiro.PRNG64;

/// <summary>
///   Underwater vents only event producing hydrogen sulfide and carbon dioxide
/// </summary>
[JSONDynamicTypeAllowed]
public class UnderwaterVentEruptionEffect : IWorldEffect
{
    [JsonProperty]
    private readonly XoShiRo256starstar random;

    [JsonProperty]
    private GameWorld targetWorld;

    public UnderwaterVentEruptionEffect(GameWorld targetWorld, long randomSeed)
    {
        this.targetWorld = targetWorld;
        random = new XoShiRo256starstar(randomSeed);
    }

    [JsonConstructor]
    public UnderwaterVentEruptionEffect(GameWorld targetWorld, XoShiRo256starstar random)
    {
        this.targetWorld = targetWorld;
        this.random = random;
    }

    public void OnRegisterToWorld()
    {
    }

    public void OnTimePassed(double elapsed, double totalTimePassed)
    {
        var changes = new Dictionary<Compound, float>();
        var cloudSizes = new Dictionary<Compound, float>();

        foreach (var patch in targetWorld.Map.Patches.Values)
        {
            if (patch.BiomeType != BiomeType.Vents)
                continue;

            if (random.Next(100) > Constants.VENT_ERUPTION_CHANCE)
                continue;

            var hasHydrogenSulfide = patch.Biome.ChangeableCompounds.TryGetValue(Compound.Hydrogensulfide,
                out var currentHydrogenSulfide);
            var hasCarbonDioxide = patch.Biome.ChangeableCompounds.TryGetValue(Compound.Carbondioxide,
                out var currentCarbonDioxide);
            var hasMethane = patch.Biome.ChangeableCompounds.TryGetValue(Compound.Methane,
                out var currentMethane);
            var hasCarbonMonoxide = patch.Biome.ChangeableCompounds.TryGetValue(Compound.Carbonmonoxide,
                out var currentCarbonMonoxide);
            var hasHydrogen = patch.Biome.ChangeableCompounds.TryGetValue(Compound.Hydrogen,
                out var currentHydrogen);


            // TODO: shouldn't the eruption work even with the compounds not present initially?
            if (!hasHydrogenSulfide || !hasCarbonDioxide)
                continue;

            currentHydrogenSulfide.Density += Constants.VENT_ERUPTION_HYDROGEN_SULFIDE_INCREASE;
            currentCarbonDioxide.Ambient += Constants.VENT_ERUPTION_CARBON_DIOXIDE_INCREASE;
            currentMethane.Ambient += Constants.VENT_ERUPTION_METHANE_INCREASE;
            currentCarbonMonoxide.Ambient += Constants.VENT_ERUPTION_CARBON_MONOXIDE_INCREASE;
            currentHydrogen.Ambient += Constants.VENT_ERUPTION_HYDROGEN_INCREASE;

            // Percentage is density times amount, so clamp to the inversed amount (times 100)
            currentHydrogenSulfide.Density = Math.Clamp(currentHydrogenSulfide.Density, 0, 1
                / currentHydrogenSulfide.Amount * 100);
            currentCarbonDioxide.Ambient = Math.Clamp(currentCarbonDioxide.Ambient, 0, 1);
            currentMethane.Ambient = Math.Clamp(currentMethane.Ambient, 0, 1);
            currentCarbonMonoxide.Ambient = Math.Clamp(currentCarbonMonoxide.Ambient, 0, 1);
            currentHydrogen.Ambient = Math.Clamp(currentHydrogen.Ambient, 0, 1);

            // Intelligently apply the changes taking total gas percentages into account
            changes[Compound.Hydrogensulfide] = currentHydrogenSulfide.Density;
            changes[Compound.Carbondioxide] = currentCarbonDioxide.Ambient;
            changes[Compound.Methane] = currentMethane.Ambient;
            changes[Compound.Carbonmonoxide] = currentCarbonMonoxide.Ambient;
            changes[Compound.Hydrogen] = currentHydrogen.Ambient;
            cloudSizes[Compound.Hydrogensulfide] = currentHydrogenSulfide.Amount;

            patch.Biome.ApplyLongTermCompoundChanges(patch.BiomeTemplate, changes, cloudSizes);

            // Patch specific log
            patch.LogEvent(new LocalizedString("UNDERWATER_VENT_ERUPTION"),
                true, true, "PatchVents.svg");

            if (patch.Visibility == MapElementVisibility.Shown)
            {
                // Global log, but only if patch is known to the player
                targetWorld.LogEvent(new LocalizedString("UNDERWATER_VENT_ERUPTION_IN", patch.Name),
                    true, true, "PatchVents.svg");
            }

            patch.AddPatchEventRecord(WorldEffectVisuals.UnderwaterVentEruption, totalTimePassed);
        }
    }
}
