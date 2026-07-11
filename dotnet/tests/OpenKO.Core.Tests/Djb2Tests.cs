using OpenKO.Core.Hashing;
using Xunit;

namespace OpenKO.Core.Tests;

public class Djb2Tests
{
    [Fact]
    public void Hash_MatchesCppGoldenVectors()
    {
        foreach (var testCase in GoldenVectors.Load("djb2.json").EnumerateArray())
        {
            string input = testCase.GetProperty("input").GetString()!;
            ulong expected = ulong.Parse(testCase.GetProperty("hash").GetString()!);

            Assert.Equal(expected, Djb2.Hash(input));
            Assert.Equal(expected, Djb2.Hash(System.Text.Encoding.ASCII.GetBytes(input)));
        }
    }
}
