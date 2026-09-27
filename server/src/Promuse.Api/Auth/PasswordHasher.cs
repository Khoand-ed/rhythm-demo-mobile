using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Promuse.Api.Auth;

/// <summary>
/// Argon2id, as the contract promises.
///
/// 参数存在哈希里 / The cost parameters are encoded into the stored string in PHC
/// format rather than read from configuration at verification time. That is what
/// makes them changeable: raising the cost next year re-hashes on next sign-in
/// and every existing password still verifies against the settings it was made
/// with. Reading current settings to verify an old hash would lock the numbers
/// in forever.
/// </summary>
public sealed class PasswordHasher
{
    // OWASP's documented Argon2id minimum: 19 MiB, two passes, one lane.
    //
    // 不是越大越好 / Deliberately not the 64 MiB variant. Every sign-in allocates
    // this much for the duration of the hash, so the parameter that protects a
    // stolen database is also the one an attacker uses to exhaust a small server
    // by sending logins. 19 MiB is the documented floor and leaves headroom; the
    // rate limiter in front of /v1/auth/login is the other half of that answer.
    private const int MemoryKib = 19456;
    private const int Iterations = 2;
    private const int Parallelism = 1;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Derive(password, salt, MemoryKib, Iterations, Parallelism);

        return $"$argon2id$v=19$m={MemoryKib},t={Iterations},p={Parallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// False for anything malformed rather than throwing. A stored hash that
    /// cannot be parsed is a failed verification, not a 500: whatever went wrong,
    /// this password does not open this account, and an exception here would turn
    /// one corrupt row into an outage.
    /// </summary>
    public bool Verify(string password, string stored)
    {
        if (!TryParse(stored, out var p)) return false;

        byte[] computed = Derive(password, p.Salt, p.MemoryKib, p.Iterations, p.Parallelism);

        // Constant time. A length-then-content comparison leaks how much of the
        // hash matched, which over enough attempts is a hash-recovery oracle.
        return CryptographicOperations.FixedTimeEquals(computed, p.Hash);
    }

    /// <summary>
    /// True when a stored hash was made with weaker settings than the current
    /// ones, so the caller can re-hash while it has the plaintext - the only
    /// moment it ever will.
    /// </summary>
    public bool NeedsRehash(string stored)
    {
        if (!TryParse(stored, out var p)) return true;

        return p.MemoryKib < MemoryKib || p.Iterations < Iterations;
    }

    private static byte[] Derive(string password, byte[] salt, int memoryKib, int iterations, int parallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(HashBytes);
    }

    private readonly record struct Parsed(
        byte[] Salt, byte[] Hash, int MemoryKib, int Iterations, int Parallelism);

    private static bool TryParse(string stored, out Parsed parsed)
    {
        parsed = default;

        // $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>
        string[] parts = stored.Split('$', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 5 || parts[0] != "argon2id") return false;

        int memory = 0, iterations = 0, parallelism = 0;

        foreach (string pair in parts[2].Split(','))
        {
            string[] kv = pair.Split('=');
            if (kv.Length != 2 || !int.TryParse(kv[1], out int value)) return false;

            switch (kv[0])
            {
                case "m": memory = value; break;
                case "t": iterations = value; break;
                case "p": parallelism = value; break;
            }
        }

        if (memory <= 0 || iterations <= 0 || parallelism <= 0) return false;

        try
        {
            parsed = new Parsed(
                Convert.FromBase64String(parts[3]),
                Convert.FromBase64String(parts[4]),
                memory, iterations, parallelism);
        }
        catch (FormatException)
        {
            return false;
        }

        return true;
    }
}
