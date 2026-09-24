using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PokeTokenBar.Windows.Core;

public interface IPokemonDetailProvider
{
    Task<PokemonDetails> GetPokemonDetailsAsync(int speciesId, CancellationToken cancellationToken = default);
}

public enum PokemonGender { Male, Female, Genderless }
public sealed record PokemonIVs(int Hp, int Attack, int Defense, int SpecialAttack, int SpecialDefense, int Speed)
{
    public int this[string stat] => stat switch
    {
        "hp" => Hp, "attack" => Attack, "defense" => Defense,
        "special-attack" => SpecialAttack, "special-defense" => SpecialDefense, "speed" => Speed, _ => 0,
    };
    public PokemonIVs Sanitize() => new(Math.Clamp(Hp, 0, 31), Math.Clamp(Attack, 0, 31),
        Math.Clamp(Defense, 0, 31), Math.Clamp(SpecialAttack, 0, 31), Math.Clamp(SpecialDefense, 0, 31), Math.Clamp(Speed, 0, 31));
}
public sealed record PokemonKnownMove(string Name, int LearnedAtLevel);
public sealed record PokemonAbilityOption(string Name, int Slot, bool IsHidden);
public sealed record PokemonMoveLearnMethod(string Method, int Level);
public sealed record PokemonMoveOption(string Name, IReadOnlyList<PokemonMoveLearnMethod> LearnMethods);
public sealed record PokemonComputedStat(string Name, int Base, int Iv, int Value);

public sealed record PokemonDetails
{
    public const string PreferredVersionGroup = "black-2-white-2";
    public int SpeciesId { get; init; }
    public string Name { get; init; } = "";
    public int Height { get; init; }
    public int Weight { get; init; }
    public int? BaseExperience { get; init; }
    public int GenderRate { get; init; } = -1;
    public IReadOnlyList<string> Types { get; init; } = [];
    public IReadOnlyDictionary<string, int> BaseStats { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<PokemonAbilityOption> Abilities { get; init; } = [];
    public IReadOnlyList<PokemonMoveOption> Moves { get; init; } = [];

    public IReadOnlyList<PokemonKnownMove> LevelUpMoves(int level) => Moves
        .SelectMany(move => move.LearnMethods.Where(row => row.Method == "level-up" && row.Level <= level)
            .Select(row => new PokemonKnownMove(move.Name, row.Level)))
        .GroupBy(move => move.Name, StringComparer.Ordinal)
        .Select(group => new PokemonKnownMove(group.Key, group.Min(move => move.LearnedAtLevel)))
        .OrderBy(move => move.LearnedAtLevel).ThenBy(move => move.Name, StringComparer.Ordinal).ToArray();
}

public sealed record PokemonProfile
{
    public const long MaxGrowthTokens = 1_000_000_000_000_000;
    [System.Text.Json.Serialization.JsonPropertyName("instanceID")]
    public string InstanceId { get; init; } = "";
    public ulong Seed { get; init; }
    public PokemonGender? Gender { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("ivs")]
    public PokemonIVs IVs { get; init; } = new(0, 0, 0, 0, 0, 0);
    public int? AbilitySlot { get; init; }
    public string? AbilityName { get; init; }
    public bool AbilityIsHidden { get; init; }
    public int Level { get; init; } = 5;
    public long GrowthTokens { get; init; }
    public IReadOnlyList<PokemonKnownMove> Moves { get; init; } = [];

    public static PokemonProfile Create() => Generate(
        BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(sizeof(ulong))), Guid.NewGuid().ToString("N"));

    public static PokemonProfile Generate(ulong seed, string instanceId)
    {
        var random = new ProfileRandom(seed);
        int Iv() => (int)(random.Next() % 32);
        return new() { Seed = seed, InstanceId = instanceId, IVs = new(Iv(), Iv(), Iv(), Iv(), Iv(), Iv()) };
    }

    public PokemonProfile AdvanceGrowth(long candidate, PokemonRarity rarity)
    {
        var growth = Math.Clamp(Math.Max(GrowthTokens, candidate), 0, MaxGrowthTokens);
        var level = 5 + (int)decimal.Floor(Math.Min(1m, (decimal)growth / PokemonBalance.GraduationTotal(rarity)) * 95);
        return this with { GrowthTokens = growth, Level = Math.Clamp(Math.Max(Level, level), 5, 100) };
    }

    public PokemonProfile RebaseSpecies(PokemonRarity oldRarity, PokemonRarity newRarity)
    {
        var growth = (long)decimal.Floor(Math.Clamp((decimal)GrowthTokens / PokemonBalance.GraduationTotal(oldRarity), 0, 1)
            * PokemonBalance.GraduationTotal(newRarity));
        return (this with { GrowthTokens = growth, Gender = null, AbilitySlot = null,
            AbilityName = null, AbilityIsHidden = false, Moves = [] }).AdvanceGrowth(growth, newRarity);
    }

    public PokemonProfile Enrich(PokemonDetails details)
    {
        // Always consume the gender roll, even on deferred ability retry: saved fields cannot shift the RNG stream.
        var random = new ProfileRandom(Seed ^ 0xA11B1E5D9EEDUL);
        var genderRoll = random.Next() % 8;
        var gender = Gender ?? (details.GenderRate < 0 ? PokemonGender.Genderless
            : genderRoll < (ulong)Math.Clamp(details.GenderRate, 0, 8) ? PokemonGender.Female : PokemonGender.Male);
        var normal = details.Abilities.Where(a => !a.IsHidden).OrderBy(a => a.Slot).ToArray();
        var hidden = details.Abilities.Where(a => a.IsHidden).OrderBy(a => a.Slot).ToArray();
        PokemonAbilityOption? chosen = null;
        if (AbilitySlot is null)
        {
            if (hidden.Length > 0 && random.Next() % 128 == 0) chosen = hidden[random.Next() % (ulong)hidden.Length];
            else if (normal.Length > 0) chosen = normal[random.Next() % (ulong)normal.Length];
        }
        chosen ??= details.Abilities.FirstOrDefault(a => a.Slot == AbilitySlot && a.IsHidden == AbilityIsHidden)
            ?? details.Abilities.FirstOrDefault(a => a.Slot == AbilitySlot) ?? normal.FirstOrDefault() ?? hidden.FirstOrDefault();
        var moves = details.LevelUpMoves(Level).TakeLast(4).ToArray();
        return this with { Gender = gender, AbilitySlot = chosen?.Slot ?? AbilitySlot,
            AbilityName = chosen?.Name, AbilityIsHidden = chosen?.IsHidden ?? AbilityIsHidden,
            Moves = Moves.SequenceEqual(moves) ? Moves : moves };
    }

    public PokemonProfile Sanitize() => this with
    {
        InstanceId = string.IsNullOrWhiteSpace(InstanceId) ? $"profile-{Seed:x16}" : InstanceId,
        IVs = IVs.Sanitize(), Level = Math.Clamp(Level, 5, 100), GrowthTokens = Math.Clamp(GrowthTokens, 0, MaxGrowthTokens),
        Gender = Gender is { } gender && Enum.IsDefined(gender) ? gender : null,
        AbilitySlot = AbilitySlot is > 0 and <= 3 ? AbilitySlot : null,
        AbilityName = AbilityName is null ? null : AbilityName[..Math.Min(80, AbilityName.Length)],
        Moves = Moves.Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Name)).Take(4)
            .Select(m => new PokemonKnownMove(m.Name[..Math.Min(80, m.Name.Length)], Math.Clamp(m.LearnedAtLevel, 0, 100))).ToArray(),
    };

    private struct ProfileRandom(ulong seed)
    {
        private ulong _state = seed;
        public ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                var value = (_state ^ (_state >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }
    }
}

public static class PokemonStatCalculator
{
    public static IReadOnlyList<string> Order { get; } = ["hp", "attack", "defense", "special-attack", "special-defense", "speed"];

    public static decimal NatureModifier(PokemonNature? nature, string stat)
    {
        // The existing enum follows the main-series 5x5 nature table (Atk, Def, Spe, SpA, SpD).
        string[] axes = ["attack", "defense", "speed", "special-attack", "special-defense"];
        if (nature is null || !Enum.IsDefined(nature.Value)) return 1m;
        var up = (int)nature.Value / 5;
        var down = (int)nature.Value % 5;
        return up == down ? 1m : stat == axes[up] ? 1.1m : stat == axes[down] ? 0.9m : 1m;
    }

    public static IReadOnlyList<PokemonComputedStat> Calculate(PokemonDetails details, PokemonProfile profile, PokemonNature? nature) =>
        Order.Where(details.BaseStats.ContainsKey).Select(stat =>
        {
            var basis = Math.Clamp(details.BaseStats[stat], 0, 1000);
            var iv = Math.Clamp(profile.IVs[stat], 0, 31);
            var level = Math.Clamp(profile.Level, 5, 100);
            var neutral = (2 * basis + iv) * level / 100;
            var value = stat == "hp" ? neutral + level + 10 : (int)decimal.Floor((neutral + 5) * NatureModifier(nature, stat));
            return new PokemonComputedStat(stat, basis, iv, value);
        }).ToArray();
}

public static class PokemonProfileMigration
{
    public static PokemonProfile FromKey(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return PokemonProfile.Generate(BinaryPrimitives.ReadUInt64LittleEndian(hash), Convert.ToHexString(hash.AsSpan(0, 16)));
    }

    public static long ReconstructedGrowth(MonState mon, double difficulty)
    {
        var forms = Math.Clamp(mon.TotalForms, 1, 12);
        var stage = Math.Clamp(mon.StageIndex, 0, forms - 1);
        var completed = Enumerable.Range(0, stage).Sum(i => PokemonBalance.PhaseThreshold(mon.Rarity, forms, i));
        var standard = PokemonBalance.PhaseThreshold(mon.Rarity, forms, stage);
        var actual = PokemonBalance.StageThreshold(mon with { TotalForms = forms, StageIndex = stage }, difficulty);
        return Math.Min(PokemonBalance.GraduationTotal(mon.Rarity), completed +
            (long)decimal.Floor(standard * Math.Clamp((decimal)mon.UsedAtStage / actual, 0, 1)));
    }

    public static CompanionState Migrate(CompanionState state, double difficulty)
    {
        var active = state.Active;
        if (active is { Profile: null })
        {
            var key = FormattableString.Invariant($"active:{active.BaseId}:{string.Join(',', active.PathIds)}:{state.LastDate}:{active.IsShiny}:{active.Nature}");
            active = active with { Profile = FromKey(key).AdvanceGrowth(ReconstructedGrowth(active, difficulty), active.Rarity) };
        }
        var dex = state.Dex.Select(entry =>
        {
            if (entry.Profile is not null) return entry;
            // Legacy released records only know reached forms, not the planned length or partial credits.
            // This deterministic reached-chain estimate is an upper bound when the original route was longer.
            var growth = entry.IsReleased ? ReconstructedGrowth(new MonState { Rarity = entry.Rarity,
                TotalForms = Math.Clamp(entry.ChainOrder.Count, 1, 12), StageIndex = Math.Max(0, entry.ChainOrder.Count - 1) }, 1)
                : PokemonBalance.GraduationTotal(entry.Rarity);
            return entry with { Profile = FromKey(FormattableString.Invariant($"dex:{entry.Id}:{entry.FinalId}"))
                .AdvanceGrowth(growth, entry.Rarity) };
        }).ToArray();
        return ReferenceEquals(active, state.Active) && dex.SequenceEqual(state.Dex) ? state : state with { Active = active, Dex = dex };
    }
}
