using Shouldly;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;
using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure.Solana;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Infrastructure;

public class TransactionSignerTests
{
    private static Account NewAccount()
    {
        var wallet = new Wallet(new Mnemonic(WordList.English, WordCount.Twelve));
        return wallet.Account;
    }

    /// <summary>A wire transaction: [count=1][64 zero bytes][message].</summary>
    private static byte[] WireTransaction(byte[] message, byte signatureCount = 1)
    {
        var buffer = new byte[1 + 64 + message.Length];
        buffer[0] = signatureCount;
        Buffer.BlockCopy(message, 0, buffer, 65, message.Length);
        return buffer;
    }

    [Fact]
    public void Signs_the_message_bytes_and_the_signature_verifies()
    {
        // This is the whole fallback path: sign without parsing, so a v0 transaction with address
        // lookup tables works exactly as a legacy one does.
        var account = NewAccount();
        var message = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var transaction = WireTransaction(message);

        var signed = TransactionSigner.Sign(transaction, account);

        var signature = signed[1..65];
        account.PublicKey.Verify(message, signature).ShouldBeTrue();
    }

    [Fact]
    public void Leaves_the_message_untouched()
    {
        var account = NewAccount();
        var message = new byte[] { 42, 43, 44 };

        var signed = TransactionSigner.Sign(WireTransaction(message), account);

        signed[65..].ShouldBe(message);
    }

    [Fact]
    public void Does_not_mutate_the_caller_s_buffer()
    {
        var account = NewAccount();
        var original = WireTransaction([1, 2, 3]);
        var before = (byte[])original.Clone();

        TransactionSigner.Sign(original, account);

        original.ShouldBe(before);
    }

    [Fact]
    public void Refuses_a_transaction_that_expects_a_co_signer()
    {
        // We hold exactly one key. Signing a 2-of-N transaction would broadcast something that can
        // never land, after having already paid for a quote.
        var account = NewAccount();

        var ex = Should.Throw<NightfallException>(
            () => TransactionSigner.Sign(WireTransaction([1, 2, 3], signatureCount: 2), account));

        ex.Message.ShouldContain("2 signatures");
    }

    [Fact]
    public void Refuses_a_buffer_too_short_to_hold_a_signature()
    {
        Should.Throw<NightfallException>(() => TransactionSigner.Sign([1, 2, 3], NewAccount()));
    }

    [Fact]
    public void Refuses_a_transaction_with_no_message()
    {
        var empty = new byte[65];
        empty[0] = 1;

        Should.Throw<NightfallException>(() => TransactionSigner.Sign(empty, NewAccount()));
    }
}
