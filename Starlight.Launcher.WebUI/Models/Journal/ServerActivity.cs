namespace Starlight.Launcher.WebUI.Models.Journal;

public sealed class ServerActivity
{
    public const int Buckets = 7 * 24;

    public string Address { get; set; } = "";

    public double[] PlayerSums { get; set; } = new double[Buckets];

    public int[] Samples { get; set; } = new int[Buckets];

    public DateTime LastSampleUtc { get; set; }

    public static int BucketOf(DayOfWeek day, int hour) => ((int)day * 24) + hour;

    public void Add(DateTime local, int players)
    {
        if (PlayerSums.Length != Buckets || Samples.Length != Buckets)
        {
            PlayerSums = new double[Buckets];
            Samples = new int[Buckets];
        }

        var bucket = BucketOf(local.DayOfWeek, local.Hour);
        PlayerSums[bucket] += players;
        Samples[bucket]++;
    }

    public double? Average(DayOfWeek day, int hour)
    {
        var bucket = BucketOf(day, hour);
        return bucket < Samples.Length && Samples[bucket] > 0 ? PlayerSums[bucket] / Samples[bucket] : null;
    }

    public int TotalSamples => Samples.Sum();
}
