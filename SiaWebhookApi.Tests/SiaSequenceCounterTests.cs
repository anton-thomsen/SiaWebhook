using Microsoft.Extensions.Logging.Abstractions;
using SiaWebhookApi.Services;

namespace SiaWebhookApi.Tests;

public class SiaSequenceCounterTests
{
    [Fact]
    public void Next_WithPersistedFile_ContinuesAfterRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sia-sequence-{Guid.NewGuid():N}.txt");
        try
        {
            var first = new SiaSequenceCounter(
                new SiaSequenceOptions { FilePath = path },
                NullLogger<SiaSequenceCounter>.Instance);

            Assert.Equal("0001", first.Next());
            Assert.Equal("0002", first.Next());

            var afterRestart = new SiaSequenceCounter(
                new SiaSequenceOptions { FilePath = path },
                NullLogger<SiaSequenceCounter>.Instance);

            Assert.Equal("0003", afterRestart.Next());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + ".tmp"))
                File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void Next_After9999_WrapsTo0001()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sia-sequence-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "9999");
            var counter = new SiaSequenceCounter(
                new SiaSequenceOptions { FilePath = path },
                NullLogger<SiaSequenceCounter>.Instance);

            Assert.Equal("0001", counter.Next());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + ".tmp"))
                File.Delete(path + ".tmp");
        }
    }
}
