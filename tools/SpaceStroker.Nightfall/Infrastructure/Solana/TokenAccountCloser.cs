using Solnet.Programs;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Types;
using Solnet.Wallet;
using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Infrastructure.Solana;

/// <summary>
/// Reclaims the ~0.002 SOL of rent sitting in a token account that a 100% sell just emptied.
///
/// ⚠️ A SEPARATE transaction, deliberately. Appending closeAccount to the swap would be one fee and
/// one signature — but if the swap consumes anything less than the exact full balance, the close
/// fails and takes the ENTIRE SALE down with it. Here the worst case is an unreclaimed 0.002 SOL,
/// never a lost sale. This is a legacy single-instruction transaction, so Solnet's TransactionBuilder
/// handles it directly (unlike the Jupiter swap — see TransactionSigner).
/// </summary>
public sealed class TokenAccountCloser(IRpcClient rpc, SolanaReader reader)
{
    /// <summary>
    /// Returns the close signature, or null if it did not happen. NEVER throws into the caller: the
    /// sale is already confirmed by this point and must not be reported as failed.
    /// </summary>
    public async Task<string?> TryCloseAsync(PoolEntry wallet, CancellationToken ct)
    {
        try
        {
            // Only ever close an account that is genuinely empty. A partial sell, or a swap that left
            // dust, must keep its account.
            var remaining = await reader.GetTokenBalanceAsync(wallet.PublicKey, ct);

            if (remaining != 0UL)
                return null;

            var account = new Account(wallet.PrivateKey, wallet.PublicKey);
            var tokenAccount = reader.DeriveAta(wallet.PublicKey);

            var blockhash = await rpc.GetLatestBlockHashAsync(Commitment.Confirmed);

            if (!blockhash.WasSuccessful || string.IsNullOrWhiteSpace(blockhash.Result?.Value?.Blockhash))
                return null;

            var transaction = new TransactionBuilder()
                .SetRecentBlockHash(blockhash.Result.Value.Blockhash)
                .SetFeePayer(account)
                .AddInstruction(TokenProgram.CloseAccount(
                    new PublicKey(tokenAccount),
                    account.PublicKey,     // rent destination — back to the wallet itself
                    account.PublicKey,     // owner/authority
                    TokenProgram.ProgramIdKey))
                .Build(account);

            var result = await rpc.SendTransactionAsync(transaction, skipPreflight: false, Commitment.Confirmed);

            return result.WasSuccessful ? result.Result : null;
        }
        catch (Exception)
        {
            // Swallowed on purpose. The rent is worth far less than a sale mis-reported as failed.
            return null;
        }
    }
}
