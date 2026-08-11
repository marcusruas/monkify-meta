using SpaceStroker.WalletFunder.Domain;

namespace SpaceStroker.WalletFunder.Infrastructure.Coinbase;

/// <summary>
/// Builds the JSON body for POST /v2/accounts/{id}/transactions.
///
/// Kept separate from <see cref="CoinbaseClient"/> because it is pure: the exact shape Coinbase
/// receives is the one thing in this tool that cannot be verified without spending real money,
/// so it is asserted key by key in unit tests instead.
/// </summary>
public static class CoinbaseSendBody
{
    public static Dictionary<string, object> Build(
        string toAddress, decimal sol, Guid idem, TravelRuleSection? travelRule)
    {
        var body = new Dictionary<string, object>
        {
            ["type"] = "send",
            ["to"] = toAddress,
            ["amount"] = WalletPool.FormatSol(sol),      // invariant — never "2,54"
            ["currency"] = "SOL",
            ["network"] = "solana",
            ["idem"] = idem.ToString("D"),
        };

        // Absent travel-rule config ⇒ no travel_rule_data key at all. Coinbase rejects the send with
        // "Incomplete travel rules information provided" when the account's jurisdiction requires it.
        if (travelRule is { IsConfigured: true })
            body["travel_rule_data"] = BuildTravelRuleData(travelRule);

        return body;
    }

    private static Dictionary<string, object> BuildTravelRuleData(TravelRuleSection travelRule)
    {
        var data = new Dictionary<string, object>
        {
            ["beneficiary_wallet_type"] = travelRule.BeneficiaryWalletType.Trim(),
        };

        AddIfSet(data, "is_self", travelRule.IsSelf);
        AddIfSet(data, "beneficiary_name", travelRule.BeneficiaryName);
        AddIfSet(data, "beneficiary_financial_institution", travelRule.BeneficiaryFinancialInstitution);
        AddIfSet(data, "transfer_purpose", travelRule.TransferPurpose);

        var address = new Dictionary<string, object>();
        AddIfSet(address, "address1", travelRule.BeneficiaryAddress1);
        AddIfSet(address, "city", travelRule.BeneficiaryCity);
        AddIfSet(address, "state", travelRule.BeneficiaryState);
        AddIfSet(address, "postal_code", travelRule.BeneficiaryPostalCode);
        AddIfSet(address, "country", travelRule.BeneficiaryCountry);

        // An empty address object is not sent — US/Canada self-hosted sends need only the wallet type.
        if (address.Count > 0) data["beneficiary_address"] = address;

        return data;
    }

    private static void AddIfSet(Dictionary<string, object> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) target[key] = value.Trim();
    }
}
