using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace DimraethModMenu;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "falor.dimraeth.modmenu";
    public const string Name = "Dimraeth Mod Menu";
    public const string Version = "1.0.0";

    internal static ManualLogSource Logger;
    internal static Harmony Harmony;

    public override void Load()
    {
        Logger = Log;
        Settings.Bind(Config);

        // Troubleshooting switches: DIMRAETH_PATCHES=none|Name1,Name2 limits patches, DIMRAETH_NOMENU=1 skips the menu.
        var onlyPatches = Environment.GetEnvironmentVariable("DIMRAETH_PATCHES");
        bool noMenu = Environment.GetEnvironmentVariable("DIMRAETH_NOMENU") == "1";

        Harmony = new Harmony(Guid);
        // Patch each class on its own so one renamed/missing game method doesn't take the rest down.
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (type.GetCustomAttribute<HarmonyPatch>() == null) continue;
            if (!string.IsNullOrEmpty(onlyPatches) && Array.IndexOf(onlyPatches.Split(','), type.Name) < 0)
            {
                Log.LogInfo($"Skipped (DIMRAETH_PATCHES): {type.Name}");
                continue;
            }
            try
            {
                Harmony.CreateClassProcessor(type).Patch();
                Log.LogInfo($"Patched: {type.Name}");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Patch failed: {type.Name} -> {e.GetBaseException().Message}");
            }
        }

        if (noMenu)
        {
            Log.LogInfo("Menu disabled (DIMRAETH_NOMENU=1)");
            return;
        }
        ClassInjector.RegisterTypeInIl2Cpp<ModMenu>();
        AddComponent<ModMenu>();
        Log.LogInfo($"{Name} {Version} loaded. Press F1 in game to open the menu.");
    }
}

internal static class Settings
{
    // Economy
    public static ConfigEntry<float> XpMult, PetXpMult, ProfXpMult, GoldMult, LootMult, EssenceMult;
    public static ConfigEntry<bool> FreeShop, FreeUpgrades;

    // Combat
    public static ConfigEntry<bool> GodMode, NoCooldowns, Untargetable, AlwaysCrit;
    public static ConfigEntry<float> DamageMult, IncomingDamageMult, MonsterHpMult, CritDamageMult, CooldownMult, SpeedMult;

    // Survival / base
    public static ConfigEntry<bool> NoHunger, NoToolDurability, UnlimitedCarry;
    public static ConfigEntry<int> FarmPlotLimit;
    public static ConfigEntry<float> GameSpeed;

    public static void Bind(ConfigFile c)
    {
        XpMult = c.Bind("Economy", "XpMultiplier", 1f, "Player XP multiplier");
        PetXpMult = c.Bind("Economy", "PetXpMultiplier", 1f, "Pet XP multiplier");
        ProfXpMult = c.Bind("Economy", "ProficiencyXpMultiplier", 1f, "Proficiency (crafting/gathering) XP multiplier");
        GoldMult = c.Bind("Economy", "GoldMultiplier", 1f, "Monster gold multiplier");
        LootMult = c.Bind("Economy", "LootChanceMultiplier", 1f, "Item/rune drop chance multiplier");
        EssenceMult = c.Bind("Economy", "EssenceMultiplier", 1f, "Essence gain multiplier");
        FreeShop = c.Bind("Economy", "FreeShop", false, "NPC shop purchases cost no gold");
        FreeUpgrades = c.Bind("Economy", "FreeUpgrades", false, "Rune, pet and forge upgrades cost nothing");

        GodMode = c.Bind("Combat", "GodMode", false, "Player takes no damage (game's own developer invulnerability)");
        NoCooldowns = c.Bind("Combat", "NoCooldowns", false, "Game's own developer no-cooldown switch");
        Untargetable = c.Bind("Combat", "Untargetable", false, "Monsters ignore the player");
        AlwaysCrit = c.Bind("Combat", "AlwaysCrit", false, "Every player hit is critical");
        DamageMult = c.Bind("Combat", "DamageMultiplier", 1f, "Damage monsters take");
        IncomingDamageMult = c.Bind("Combat", "IncomingDamageMultiplier", 1f, "Damage monsters deal");
        MonsterHpMult = c.Bind("Combat", "MonsterHealthMultiplier", 1f, "Monster max health");
        CritDamageMult = c.Bind("Combat", "CritDamageMultiplier", 1f, "Player critical damage multiplier");
        CooldownMult = c.Bind("Combat", "CooldownMultiplier", 1f, "Player spell cooldown length multiplier");
        SpeedMult = c.Bind("Combat", "SpeedMultiplier", 1f, "Player move speed (game's own QA speed-up effect)");

        NoHunger = c.Bind("Survival", "NoHungerThirst", false, "Hunger and thirst never drain");
        NoToolDurability = c.Bind("Survival", "NoToolDurability", false, "Tools don't lose durability while harvesting");
        FarmPlotLimit = c.Bind("Survival", "FarmPlotLimit", 0, "Base farm plot limit (0 = game default)");
        GameSpeed = c.Bind("Survival", "GameSpeed", 1f, "Time.timeScale");
        UnlimitedCarry = c.Bind("Survival", "UnlimitedCarry", false, "No carry weight limit / encumbrance");    }
}
