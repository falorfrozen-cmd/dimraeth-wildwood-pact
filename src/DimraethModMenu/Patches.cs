using System;
using HarmonyLib;
using Unity.Netcode;
using S = DimraethModMenu.Settings;

namespace DimraethModMenu;

/// <summary>Shared game state lookups used by patches and the menu.</summary>
internal static class Game
{
    static Player _localPlayer;
    static float _nextLookup;

    /// <summary>Mods only act in single player or when we host, never in someone else's lobby.</summary>
    public static bool Active
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return nm == null || !nm.IsListening || nm.IsServer;
        }
    }

    /// <summary>Resolved through Netcode only, so nothing in the game's own classes is touched on the main menu.</summary>
    public static Player LocalPlayer
    {
        get
        {
            if (_localPlayer != null && _localPlayer.WasCollected) _localPlayer = null;
            // Re-check every second even with a cached player: travelling to another region replaces the
            // player object, and the old wrapper still compares non-null while its native object is gone.
            if (UnityEngine.Time.unscaledTime >= _nextLookup)
            {
                _nextLookup = UnityEngine.Time.unscaledTime + 1f;
                var nm = NetworkManager.Singleton;
                Player found = null;
                if (nm != null && nm.IsListening)
                {
                    var playerObject = nm.LocalClient?.PlayerObject;
                    if (playerObject != null) found = playerObject.GetComponent<Player>();
                    // After travelling, the new player isn't registered as Netcode's player object.
                    if (found == null)
                        foreach (var p in UnityEngine.Object.FindObjectsByType<Player>(UnityEngine.FindObjectsSortMode.None))
                            if (p.IsOwner) { found = p; break; }
                }
                _localPlayer = found;
            }
            return _localPlayer;
        }
    }

    public static bool IsLocal(ObjectsCommon obj)
    {
        var lp = LocalPlayer;
        return obj != null && lp != null && obj.Pointer == lp.Pointer;
    }

    public static int Scale(int value, float mult) =>
        value <= 0 || mult == 1f ? value : (int)Math.Min(int.MaxValue, Math.Round(value * (double)mult));

    static readonly System.Collections.Generic.Dictionary<string, int> _traceCounts = new();

    /// <summary>Logs the first few hits of each patch so the BepInEx log shows which hooks actually fire.</summary>
    public static void Trace(string key, string message)
    {
        _traceCounts.TryGetValue(key, out int n);
        if (n >= 5) return;
        _traceCounts[key] = n + 1;
        Plugin.Logger.LogInfo($"[{key}] {message}");
    }
}

// ───────────── Economy ─────────────

// GrantXPToPlayer is the main entry point; AddXPToPlayer is a separate path that GrantXPToPlayer
// does not route through (verified in game), so both are scaled without double counting.
[HarmonyPatch(typeof(XP), nameof(XP.GrantXPToPlayer))]
static class GrantXpPatch
{
    static void Prefix(ref int xp)
    {
        if (!Game.Active) return; int before = xp; xp = Game.Scale(xp, S.XpMult.Value); Game.Trace("GrantXP", $"{before} -> {xp}");
    }
}

[HarmonyPatch(typeof(XP), nameof(XP.AddXPToPlayer))]
static class XpPatch
{
    static void Prefix(ref int xp)
    {
        if (!Game.Active) return; int before = xp; xp = Game.Scale(xp, S.XpMult.Value); Game.Trace("XP", $"{before} -> {xp}");
    }
}

[HarmonyPatch(typeof(XP), nameof(XP.CalculatePetXPGain))]
static class PetXpPatch
{
    static void Postfix(ref int __result)
    {
        if (!Game.Active) return; int before = __result; __result = Game.Scale(__result, S.PetXpMult.Value); Game.Trace("PetXP", $"{before} -> {__result}");
    }
}

[HarmonyPatch(typeof(ProficienciesBookmark), nameof(ProficienciesBookmark.AddProficiencyXP))]
static class ProficiencyXpPatch
{
    static void Prefix(ref int additionalXP)
    {
        if (!Game.Active) return; int before = additionalXP; additionalXP = Game.Scale(additionalXP, S.ProfXpMult.Value); Game.Trace("ProfXP", $"{before} -> {additionalXP}");
    }
}

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.GetGoldMultiplier))]
static class GoldPatch
{
    static void Postfix(ref float __result)
    {
        if (!Game.Active) return; __result *= S.GoldMult.Value; Game.Trace("Gold", $"multiplier {__result}");
    }
}

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.GetLootChanceMultiplier))]
static class LootPatch
{
    static void Postfix(ref float __result)
    {
        if (!Game.Active) return; __result *= S.LootMult.Value; Game.Trace("Loot", $"multiplier {__result}");
    }
}

[HarmonyPatch(typeof(Forge), nameof(Forge.ApplyEssenceGainBonus))]
static class EssencePatch
{
    static void Postfix(ref int __result)
    {
        if (!Game.Active) return; int before = __result; __result = Game.Scale(__result, S.EssenceMult.Value); Game.Trace("Essence", $"{before} -> {__result}");
    }
}

[HarmonyPatch(typeof(NPCShop), nameof(NPCShop.EnoughGold))]
static class ShopEnoughGoldPatch
{
    static void Postfix(ref bool __result)
    {
        if (Game.Active && S.FreeShop.Value) __result = true;
    }
}

[HarmonyPatch(typeof(NPCShop), nameof(NPCShop.DeductPlayerGoldAtPrice))]
static class ShopDeductAtPricePatch
{
    static void Prefix(ref int unitPrice)
    {
        if (Game.Active && S.FreeShop.Value) unitPrice = 0;
    }
}

[HarmonyPatch(typeof(NPCShop), nameof(NPCShop.DeductPlayerGold))]
static class ShopDeductPatch
{
    static bool Prefix(NPCShop __instance, ulong networkId, int amount, ref int __result)
    {
        if (!Game.Active || !S.FreeShop.Value) return true;
        __result = __instance.DeductPlayerGoldAtPrice(networkId, 0, amount);
        return false;
    }
}

[HarmonyPatch]
static class FreeUpgradesPatch
{
    static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(RuneManager), nameof(RuneManager.CalculateUpgradeCost));
        yield return AccessTools.Method(typeof(RuneManager), nameof(RuneManager.CalculateTotalUpgradeCost));
        yield return AccessTools.Method(typeof(PetManager), nameof(PetManager.CalculateLevelUpCost));
        yield return AccessTools.Method(typeof(PetPanelUI), nameof(PetPanelUI.CalculateLevelUpCost));
        yield return AccessTools.Method(typeof(PetPenUI), nameof(PetPenUI.CalculateLevelUpCost));
        yield return AccessTools.Method(typeof(Forge), nameof(Forge.CurrentUpgradeCost));
    }

    static void Postfix(ref int __result)
    {
        if (Game.Active && S.FreeUpgrades.Value) __result = 0;
    }
}

// ───────────── Combat ─────────────

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.GetMonsterDamageTakenMultiplier))]
static class DamageDealtPatch
{
    static void Postfix(ref float __result)
    {
        if (!Game.Active) return; __result *= S.DamageMult.Value; Game.Trace("DamageDealt", $"multiplier {__result}");
    }
}

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.GetMonsterDamageDealtMultiplier))]
static class IncomingDamagePatch
{
    static void Postfix(ref float __result)
    {
        if (!Game.Active) return; __result *= S.IncomingDamageMult.Value; Game.Trace("DamageTaken", $"multiplier {__result}");
    }
}

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.GetMonsterHealthMultiplier))]
static class MonsterHealthPatch
{
    static void Postfix(ref float __result)
    {
        if (!Game.Active) return; __result *= S.MonsterHpMult.Value; Game.Trace("MonsterHP", $"multiplier {__result}");
    }
}

[HarmonyPatch(typeof(Formulas), nameof(Formulas.CalculateCriticalRate))]
static class CritRatePatch
{
    static void Postfix(ObjectsCommon obj, ref float __result)
    {
        if (!Game.Active || !Game.IsLocal(obj)) return; Game.Trace("CritRate", $"base {__result}"); if (S.AlwaysCrit.Value) __result = Math.Max(__result, 100f);
    }
}

[HarmonyPatch(typeof(Formulas), nameof(Formulas.CalculateCriticalDamage))]
static class CritDamagePatch
{
    static void Postfix(ObjectsCommon obj, ref float __result)
    {
        if (!Game.Active || !Game.IsLocal(obj)) return; Game.Trace("CritDamage", $"base {__result}"); __result *= S.CritDamageMult.Value;
    }
}

[HarmonyPatch(typeof(Formulas), nameof(Formulas.CalculateSpellCooldown))]
static class CooldownPatch
{
    static void Postfix(ObjectsCommon obj, ref float __result)
    {
        if (!Game.Active || !Game.IsLocal(obj)) return; Game.Trace("Cooldown", $"base {__result}"); __result *= S.CooldownMult.Value;
    }
}

// ───────────── Survival / base ─────────────

[HarmonyPatch(typeof(Consumption), nameof(Consumption.PassiveDrainTick))]
static class HungerDrainPatch
{
    static bool Prefix() => !(Game.Active && S.NoHunger.Value);
}

[HarmonyPatch(typeof(Consumption), nameof(Consumption.DrainForRegen))]
static class HungerRegenDrainPatch
{
    static bool Prefix() => !(Game.Active && S.NoHunger.Value);
}

[HarmonyPatch(typeof(Harvest), nameof(Harvest.ApplyToolDurabilityLoss))]
static class ToolDurabilityPatch
{
    static bool Prefix() => !(Game.Active && S.NoToolDurability.Value);
}

[HarmonyPatch(typeof(PlayerBaseManager), nameof(PlayerBaseManager.GetFarmPlotLimit))]
static class FarmPlotLimitPatch
{
    static void Postfix(ref int __result)
    {
        if (Game.Active && S.FarmPlotLimit.Value > 0) __result = S.FarmPlotLimit.Value;
    }
}

[HarmonyPatch]
static class CarryCapacityPatch
{
    const float Unlimited = 99999f;

    static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Formulas), nameof(Formulas.RemainingCarryCapacity));
        yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.GetRemainingCarryCapacity));
        yield return AccessTools.Method(typeof(ItemShop), nameof(ItemShop.GetRemainingCarryCapacity));
    }

    static void Postfix(ref float __result)
    {
        if (Game.Active && S.UnlimitedCarry.Value) __result = Unlimited;
    }
}

[HarmonyPatch(typeof(Formulas), nameof(Formulas.ExceedsEncumbrance))]
static class EncumbrancePatch
{
    static void Postfix(ref bool __result)
    {
        if (Game.Active && S.UnlimitedCarry.Value) __result = false;
    }
}


[HarmonyPatch(typeof(WeatherManager), nameof(WeatherManager.IsWithinStartSunnyPeriod))]
static class StartSunnyPeriodPatch
{
    static void Postfix(ref bool __result)
    {
        if (Game.Active && ModMenu.ForceWeather) __result = false;
    }
}

// ───────────── Menu input ─────────────

/// <summary>Keeps clicks on the mod menu from also walking or attacking in the game.</summary>
[HarmonyPatch]
static class PointerOverMenuPatch
{
    static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(UnityEngine.EventSystems.EventSystem), nameof(UnityEngine.EventSystems.EventSystem.IsPointerOverGameObject), Type.EmptyTypes);
        yield return AccessTools.Method(typeof(UnityEngine.EventSystems.EventSystem), nameof(UnityEngine.EventSystems.EventSystem.IsPointerOverGameObject), new[] { typeof(int) });
    }

    static void Postfix(ref bool __result)
    {
        if (ModMenu.MouseOverMenu) __result = true;
    }
}

[HarmonyPatch(typeof(MouseMovement), nameof(MouseMovement.IsMouseClickForMouseMovement))]
static class MouseMovementOverMenuPatch
{
    static void Postfix(ref bool __result)
    {
        if (ModMenu.MouseOverMenu) __result = false;
    }
}
