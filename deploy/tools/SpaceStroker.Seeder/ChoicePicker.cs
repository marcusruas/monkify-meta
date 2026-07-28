namespace SpaceStroker.Seeder;

/// <summary>
/// Picks a bet choice the backend will accept. Pure — the caller supplies the Random.
///
/// The character pools mirror GameCharacterType's [Description] values EXACTLY and are
/// lowercase-only. Since specs/bet-choice-casing.md the backend REJECTS anything outside the pool
/// as InvalidCharacters rather than lowercasing it, so this must never uppercase or title-case a
/// choice.
/// </summary>
public static class ChoicePicker
{
    private const string Digits = "0123456789";
    private const string Letters = "abcdefghijklmnopqrstuvwxyz";

    public static string Pick(GameTypeDto type, Random random)
    {
        // A preset game only ever accepts one of its listed choices, so draw from the list the API
        // returned — never a hardcoded beast name. An EMPTY list means free-text (that is what
        // GameParametersDto sends for a non-preset type), so only a populated list takes this path.
        if (type.PresetChoices is { Count: > 0 })
        {
            var usable = type.PresetChoices.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

            if (usable.Count == 0)
                throw new SeederException(
                    $"\"{type.Name}\" is a preset game but every preset choice is blank — cannot pick a valid choice.");

            return usable[random.Next(usable.Count)];
        }

        if (type.ChoiceRequiredLength is not > 0)
            throw new SeederException(
                $"\"{type.Name}\" has no choiceRequiredLength — cannot build a free-text choice.");

        var pool = PoolFor(type);
        var length = type.ChoiceRequiredLength.Value;

        if (!type.AcceptDuplicatedCharacters && length > pool.Length)
            throw new SeederException(
                $"\"{type.Name}\" needs {length} distinct characters but its pool only has {pool.Length}.");

        if (type.AcceptDuplicatedCharacters)
        {
            return string.Create(length, (pool, random), static (span, state) =>
            {
                for (var i = 0; i < span.Length; i++)
                    span[i] = state.pool[state.random.Next(state.pool.Length)];
            });
        }

        // Partial Fisher-Yates over the pool — guarantees distinctness without a rejection loop
        // that could spin for a long time on a small pool.
        var chars = pool.ToCharArray();
        for (var i = 0; i < length; i++)
        {
            var j = random.Next(i, chars.Length);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars, 0, length);
    }

    /// <summary>
    /// An unknown value is an ERROR, not a fallback: guessing the pool produces a choice the
    /// backend rejects AFTER the stake has already been paid.
    /// </summary>
    private static string PoolFor(GameTypeDto type) => type.GameCharacterType switch
    {
        1 => Digits,
        2 => Letters,
        3 => Letters + Digits,
        _ => throw new SeederException(
            $"\"{type.Name}\" has an unknown gameCharacterType ({type.GameCharacterType}) — refusing to guess the character pool."),
    };
}
