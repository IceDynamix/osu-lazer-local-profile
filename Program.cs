using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using osu.Game.Scoring;
using osu.Game.Tests.Beatmaps;
using osu.Game.Utils;
using Realms;

if (args.Length == 0) throw new Exception("Missing osu directory argument");
if (args.Length == 1) throw new Exception("Missing ruleset argument");

var osuDir = args[0];
var mode = args[1];

WorkingBeatmap GetWorkingBeatmap(string hash)
{
    using (var stream = File.OpenRead($"{osuDir}/files/{hash[0]}/{hash[0..2]}/{hash}"))
    using (var reader = new LineBufferedReader(stream))
        return new TestWorkingBeatmap(Decoder.GetDecoder<Beatmap>(reader).Decode(reader));
}

Ruleset ruleset = mode switch
{
    "osu" => new OsuRuleset(),
    "taiko" => new TaikoRuleset(),
    "catch" => new CatchRuleset(),
    "mania" => new ManiaRuleset(),
    _ => throw new Exception("Invalid ruleset (must be one of osu/taiko/catch/mania)"),
};

var realm = Realm.GetInstance(new RealmConfiguration(osuDir + @"/client.realm")
{
    SchemaVersion = 25 // relates to RealmAccess.schema_version
});

Console.WriteLine("Created database instance");

var personalBests = new Dictionary<string, ScoreInfo>();
foreach (var score in realm.All<ScoreInfo>().Filter("Rank > -1"))
{
    if (score.Ruleset.ShortName != mode) continue;
    if (score.BeatmapInfo.Status != BeatmapOnlineStatus.Ranked) continue;
    if (score.Mods.Any(m => m is ModClassic)) continue;
    if (personalBests.ContainsKey(score.Hash))
        if (score.TotalScore < personalBests[score.Hash].TotalScore)
            continue;

    personalBests.Add(score.Hash, score);
}

Console.WriteLine("Computed personal bests");

var scores = new List<(ScoreInfo, double)>();
{
    var i = 0;
    var count = personalBests.Values.Count();
    foreach (var score in personalBests.Values)
    {
        var beatmap = GetWorkingBeatmap(score.BeatmapInfo.Hash);

        var diffCalc = ruleset.CreateDifficultyCalculator(beatmap);
        var diffAttr = diffCalc.Calculate(score.Mods);
        var perfCalc = ruleset.CreatePerformanceCalculator();
        var pp = perfCalc?.Calculate(score, diffAttr);

        if (pp is not null)
            scores.Add((score, pp.Total));

        Console.Write($"\r{++i}/{count} scores processed");
    }

    scores = scores.OrderByDescending(s => s.Item2).ToList();
}

Console.WriteLine();

var weightedPp = 0d;
var weightedAcc = 0d;

for (int i = 0; i < scores.Count(); i++)
{
    var (score, pp) = scores[i];

    var weight = Math.Pow(0.95, i);
    weightedPp += pp * weight / 20;
    weightedAcc += score.Accuracy * weight / 20;

    if (i < 25)
        Console.WriteLine($"{i, 5} | {pp:f}pp | {score.Accuracy.FormatAccuracy()} | {score.BeatmapInfo.StarRating:f2}* | {score.BeatmapInfo}");
}

Console.WriteLine($"{scores.Count()} filtered scores, {weightedPp:f2} avg pp, {weightedPp * 20:f2} total pp, {weightedAcc * 100:f2}% avg acc");

Console.Write("Press any key to close");
Console.Read();
