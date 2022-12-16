using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using osu.Game.Scoring;
using osu.Game.Tests.Beatmaps;
using Realms;

var cwd = @"D:\Games\osu-lazer";
var mode = "taiko";

WorkingBeatmap GetWorkingBeatmap(string hash)
{
    using (var stream = File.OpenRead($"{cwd}/files/{hash[0]}/{hash[0..2]}/{hash}"))
    using (var reader = new LineBufferedReader(stream))
        return new TestWorkingBeatmap(Decoder.GetDecoder<Beatmap>(reader).Decode(reader));
}

Ruleset? ruleset = mode switch
{
    "osu" => new OsuRuleset(),
    "taiko" => new TaikoRuleset(),
    "catch" => new CatchRuleset(),
    "mania" => new ManiaRuleset(),
    _ => null,
};

if (ruleset is null) throw new Exception("Invalid ruleset");

var realm = Realm.GetInstance(new RealmConfiguration(cwd + @"/client - Copy.realm")
{
    SchemaVersion = 25 // relates to RealmAccess.schema_version
});

var personalBests = new Dictionary<string, ScoreInfo>();
foreach (var score in realm.All<ScoreInfo>().Filter("Rank > -1"))
{
    if (score.Ruleset.ShortName != mode) continue;
    if (score.BeatmapInfo.Status != BeatmapOnlineStatus.Ranked) continue;
    if (personalBests.ContainsKey(score.Hash))
        if (score.TotalScore < personalBests[score.Hash].TotalScore) continue;

    personalBests.Add(score.Hash, score);
}

var scores = new List<(ScoreInfo, double)>();
foreach (var score in personalBests.Values)
{
    var beatmap = GetWorkingBeatmap(score.BeatmapInfo.Hash);

    var diffCalc = ruleset.CreateDifficultyCalculator(beatmap);
    var diffAttr = diffCalc.Calculate(score.Mods);
    var perfCalc = ruleset.CreatePerformanceCalculator();
    var pp = perfCalc?.Calculate(score, diffAttr);

    if (pp is not null)
        scores.Add((score, pp.Total));
}

foreach (var (score, pp) in scores.OrderByDescending(s => s.Item2).Take(10))
{
    Console.WriteLine($"{pp:f}pp | {score.BeatmapInfo}");
}

Console.Read();
