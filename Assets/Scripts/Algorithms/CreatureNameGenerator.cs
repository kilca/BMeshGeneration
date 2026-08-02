// ============================================================================
// Creature Name Generator
// ============================================================================
// A random prefix+suffix combiner for cosmetic creature names -- no external
// word list, just enough variety to not repeat constantly.
using UnityEngine;

public static class CreatureNameGenerator
{
    private static readonly string[] Prefixes =
    {
        "Zar", "Mor", "Xen", "Kri", "Vol", "Thal", "Grum", "Nix", "Bex", "Quor",
        "Fenn", "Drak", "Sil", "Ozz", "Vex", "Plor", "Yuk", "Wren", "Emb", "Task",
    };

    private static readonly string[] Suffixes =
    {
        "gor", "thex", "ule", "ith", "ak", "on", "eth", "ux", "iss", "orn",
        "yx", "aal", "imo", "azz", "eel", "urk", "ova", "esh", "und", "ip",
    };

    public static string GenerateRandomName()
    {
        return Prefixes[Random.Range(0, Prefixes.Length)] + Suffixes[Random.Range(0, Suffixes.Length)];
    }
}
