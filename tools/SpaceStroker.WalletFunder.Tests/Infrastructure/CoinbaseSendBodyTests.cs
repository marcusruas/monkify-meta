using System.Text.Json;
using Shouldly;
using Xunit;
using SpaceStroker.WalletFunder.Infrastructure;
using SpaceStroker.WalletFunder.Infrastructure.Coinbase;

namespace SpaceStroker.WalletFunder.Tests.Infrastructure;

/// <summary>
/// The exact JSON Coinbase receives. Coinbase rejects a send with HTTP 400
/// "Incomplete travel rules information provided" when travel_rule_data is missing or malformed,
/// so the body shape is asserted key by key — this is the one place a typo costs a failed batch.
/// </summary>
public class CoinbaseSendBodyTests
{
    private static readonly Guid Idem = Guid.Parse("c8f5001f-1111-2222-3333-444455556666");

    private static JsonElement Serialize(Dictionary<string, object> body)
        => JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.Clone();

    private static TravelRuleSection SelfHosted() => new()
    {
        BeneficiaryWalletType = "WALLET_TYPE_SELF_HOSTED",
        IsSelf = "IS_SELF_TRUE",
        BeneficiaryName = "Satoshi Nakamoto",
        BeneficiaryCountry = "BR",
    };

    [Fact]
    public void Carries_the_core_send_fields_with_an_invariant_amount()
    {
        var json = Serialize(CoinbaseSendBody.Build("3GBLYN", 0.2m, Idem, travelRule: null));

        json.GetProperty("type").GetString().ShouldBe("send");
        json.GetProperty("to").GetString().ShouldBe("3GBLYN");
        json.GetProperty("amount").GetString().ShouldBe("0.2");     // never "0,2"
        json.GetProperty("currency").GetString().ShouldBe("SOL");
        json.GetProperty("network").GetString().ShouldBe("solana");
        json.GetProperty("idem").GetString().ShouldBe(Idem.ToString("D"));
    }

    [Fact]
    public void Omits_travel_rule_data_entirely_when_it_is_not_configured()
    {
        var json = Serialize(CoinbaseSendBody.Build("addr", 1m, Idem, travelRule: null));

        json.TryGetProperty("travel_rule_data", out _).ShouldBeFalse();
    }

    [Fact]
    public void Omits_travel_rule_data_when_the_wallet_type_is_blank()
    {
        var json = Serialize(CoinbaseSendBody.Build("addr", 1m, Idem, new TravelRuleSection()));

        json.TryGetProperty("travel_rule_data", out _).ShouldBeFalse();
    }

    [Fact]
    public void Puts_travel_rule_data_at_the_top_level_next_to_type_and_to()
    {
        var json = Serialize(CoinbaseSendBody.Build("addr", 0.2m, Idem, SelfHosted()));

        var travel = json.GetProperty("travel_rule_data");
        travel.GetProperty("beneficiary_wallet_type").GetString().ShouldBe("WALLET_TYPE_SELF_HOSTED");
        travel.GetProperty("is_self").GetString().ShouldBe("IS_SELF_TRUE");
        travel.GetProperty("beneficiary_name").GetString().ShouldBe("Satoshi Nakamoto");
    }

    [Fact]
    public void Nests_the_country_inside_beneficiary_address()
    {
        // Coinbase names the missing param BENEFICIARY_COUNTRY, but the field is nested.
        var json = Serialize(CoinbaseSendBody.Build("addr", 0.2m, Idem, SelfHosted()));

        json.GetProperty("travel_rule_data").GetProperty("beneficiary_address")
            .GetProperty("country").GetString().ShouldBe("BR");
    }

    [Fact]
    public void Omits_blank_optional_travel_rule_fields_rather_than_sending_empty_strings()
    {
        var json = Serialize(CoinbaseSendBody.Build("addr", 0.2m, Idem, SelfHosted()));
        var travel = json.GetProperty("travel_rule_data");

        travel.TryGetProperty("beneficiary_financial_institution", out _).ShouldBeFalse();
        travel.TryGetProperty("transfer_purpose", out _).ShouldBeFalse();

        var address = travel.GetProperty("beneficiary_address");
        address.TryGetProperty("address1", out _).ShouldBeFalse();
        address.TryGetProperty("city", out _).ShouldBeFalse();
    }

    [Fact]
    public void Carries_the_full_address_and_institution_when_the_jurisdiction_needs_them()
    {
        var travelRule = SelfHosted();
        travelRule.BeneficiaryWalletType = "WALLET_TYPE_EXCHANGE";
        travelRule.BeneficiaryFinancialInstitution = "009BBC11-03FB-4224-8CC7-CCE7CCDE4706";
        travelRule.TransferPurpose = "Treasury funding";
        travelRule.BeneficiaryAddress1 = "1 Dr Carlton B Goodlett Pl";
        travelRule.BeneficiaryCity = "San Francisco";
        travelRule.BeneficiaryState = "CA";
        travelRule.BeneficiaryPostalCode = "94102";

        var travel = Serialize(CoinbaseSendBody.Build("addr", 0.2m, Idem, travelRule))
            .GetProperty("travel_rule_data");

        travel.GetProperty("beneficiary_wallet_type").GetString().ShouldBe("WALLET_TYPE_EXCHANGE");
        travel.GetProperty("beneficiary_financial_institution").GetString()
            .ShouldBe("009BBC11-03FB-4224-8CC7-CCE7CCDE4706");
        travel.GetProperty("transfer_purpose").GetString().ShouldBe("Treasury funding");

        var address = travel.GetProperty("beneficiary_address");
        address.GetProperty("address1").GetString().ShouldBe("1 Dr Carlton B Goodlett Pl");
        address.GetProperty("city").GetString().ShouldBe("San Francisco");
        address.GetProperty("state").GetString().ShouldBe("CA");
        address.GetProperty("postal_code").GetString().ShouldBe("94102");
    }

    [Fact]
    public void Omits_beneficiary_address_completely_when_no_address_field_is_set()
    {
        // US/Canada self-hosted sends need only the wallet type — an empty address object is not sent.
        var travelRule = new TravelRuleSection { BeneficiaryWalletType = "WALLET_TYPE_SELF_HOSTED" };

        var travel = Serialize(CoinbaseSendBody.Build("addr", 0.2m, Idem, travelRule))
            .GetProperty("travel_rule_data");

        travel.TryGetProperty("beneficiary_address", out _).ShouldBeFalse();
        travel.TryGetProperty("beneficiary_name", out _).ShouldBeFalse();
        travel.GetProperty("beneficiary_wallet_type").GetString().ShouldBe("WALLET_TYPE_SELF_HOSTED");
    }
}
