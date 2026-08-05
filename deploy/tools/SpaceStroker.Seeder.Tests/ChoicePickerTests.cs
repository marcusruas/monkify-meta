using Shouldly;
using Xunit;

namespace SpaceStroker.Seeder.Tests;

public class ChoicePickerTests
{
    private static GameTypeDto Type(
        int charType = 2,
        int? length = 6,
        bool duplicates = true,
        List<string>? presets = null)
        => new(
            GameTypeId: Guid.NewGuid(),
            Name: "Test",
            RequiredAmount: 1m,
            MinimumNumberOfPlayers: 2,
            ChoiceRequiredLength: length,
            AcceptDuplicatedCharacters: duplicates,
            GameCharacterType: charType,
            PresetChoices: presets,
            TokenMintAddress: "mint",
            TokenDecimals: 6,
            TokenReceiverPublicKey: "receiver");

    [Fact]
    public void A_preset_game_always_returns_a_listed_choice_verbatim()
    {
        var presets = new List<string> { "rabbit", "monkey", "jaguar", "falcon" };
        var type = Type(presets: presets);

        for (var seed = 0; seed < 50; seed++)
            presets.ShouldContain(ChoicePicker.Pick(type, new Random(seed)));
    }

    [Fact]
    public void A_preset_game_eventually_reaches_every_choice()
    {
        // A picker that always returned presets[0] would pass the "is a member" test above.
        var presets = new List<string> { "rabbit", "monkey", "jaguar", "falcon" };
        var type = Type(presets: presets);

        var seen = Enumerable.Range(0, 200).Select(s => ChoicePicker.Pick(type, new Random(s))).ToHashSet();

        seen.Count.ShouldBe(presets.Count);
    }

    [Fact]
    public void A_preset_game_whose_choices_are_all_blank_throws_rather_than_inventing_one()
    {
        var type = Type(presets: new List<string> { "  ", "" });

        Should.Throw<SeederException>(() => ChoicePicker.Pick(type, new Random(1)));
    }

    [Fact]
    public void An_empty_preset_list_means_free_text_not_a_preset_game()
    {
        // GameParametersDto sends an empty list (not null) for free-text types.
        var choice = ChoicePicker.Pick(Type(presets: new List<string>()), new Random(1));

        choice.Length.ShouldBe(6);
    }

    [Fact]
    public void Free_text_letters_produces_six_lowercase_letters()
    {
        var choice = ChoicePicker.Pick(Type(charType: 2, length: 6), new Random(7));

        choice.Length.ShouldBe(6);
        choice.ShouldBe(choice.ToLowerInvariant());
        choice.ShouldAllBe(c => c >= 'a' && c <= 'z');
    }

    [Fact]
    public void Free_text_numbers_produces_digits_only()
    {
        var choice = ChoicePicker.Pick(Type(charType: 1, length: 6), new Random(7));

        choice.Length.ShouldBe(6);
        choice.ShouldAllBe(c => c >= '0' && c <= '9');
    }

    [Fact]
    public void Free_text_alphanumeric_stays_inside_the_pool()
    {
        var choice = ChoicePicker.Pick(Type(charType: 3, length: 8), new Random(7));

        choice.Length.ShouldBe(8);
        choice.ShouldAllBe(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'));
    }

    [Fact]
    public void Duplicates_are_avoided_when_the_game_forbids_them()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var choice = ChoicePicker.Pick(Type(charType: 2, length: 6, duplicates: false), new Random(seed));

            choice.Distinct().Count().ShouldBe(choice.Length);
        }
    }

    [Fact]
    public void An_unknown_character_type_throws_instead_of_silently_falling_back()
    {
        // Guessing the pool yields a choice the backend rejects AFTER the stake was paid.
        Should.Throw<SeederException>(() => ChoicePicker.Pick(Type(charType: 99), new Random(1)));
    }

    [Fact]
    public void A_missing_choice_length_throws()
    {
        Should.Throw<SeederException>(() => ChoicePicker.Pick(Type(length: null), new Random(1)));
    }

    [Fact]
    public void The_same_seed_produces_the_same_choice()
    {
        ChoicePicker.Pick(Type(), new Random(42)).ShouldBe(ChoicePicker.Pick(Type(), new Random(42)));
    }

    [Fact]
    public void Requesting_more_distinct_characters_than_the_pool_holds_throws()
    {
        // charset 1 has 10 digits; a 12-character no-duplicates choice is impossible.
        Should.Throw<SeederException>(
            () => ChoicePicker.Pick(Type(charType: 1, length: 12, duplicates: false), new Random(1)));
    }
}
