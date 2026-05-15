namespace Coffer.Core.Security;

public sealed record Argon2Parameters(
    int MemorySizeKb,
    int Iterations,
    int Parallelism,
    int OutputBytes,
    int SaltBytes)
{
    public static readonly Argon2Parameters Default = new(
        MemorySizeKb: 65536,
        Iterations: 3,
        Parallelism: 4,
        OutputBytes: 32,
        SaltBytes: 16);
}
