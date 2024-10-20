﻿using Newtonsoft.Json.Linq;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Taiko;
using osu.Game.Scoring;
using osu.Game.Tests.Beatmaps;
using osu.Game.Utils;
using Realms;

if (args.Length == 0) throw new Exception("Missing osu directory argument");
if (args.Length == 1) throw new Exception("Missing ruleset argument");

var osuDir = args[0];
if (!Directory.Exists(osuDir) || !Directory.Exists(osuDir + "/files")) throw new Exception("osu directory was invalid");

var osuDailyApiKeyPath = @"./osu_daily_api_key.txt";

Ruleset ruleset = args[1] switch
{
    "osu" => new OsuRuleset(),
    "taiko" => new TaikoRuleset(),
    "catch" => new CatchRuleset(),
    "mania" => new ManiaRuleset(),
    _ => throw new Exception("Invalid ruleset (must be one of osu/taiko/catch/mania)"),
};

var realm = Realm.GetInstance(new RealmConfiguration(osuDir + @"/client.realm")
{
    SchemaVersion = 42 // relates to RealmAccess.schema_version
});

Console.WriteLine("Created database instance");

var scores = realm.All<ScoreInfo>().Filter("Rank > -1")
    .AsEnumerable()
    .Where(s => s.Ruleset.ShortName == ruleset.ShortName)
    .Where(s => s.BeatmapInfo != null && s.BeatmapInfo.Status == BeatmapOnlineStatus.Ranked)
    .Where(s => s.Mods.All(m => m.Ranked))
    .Where(s => s.User.Username != "Guest")
    .Select(score =>
    {
        try
        {
            var hash = score.BeatmapInfo.Hash;
            using var stream = File.OpenRead($"{osuDir}/files/{hash[0]}/{hash[0..2]}/{hash}");
            using var reader = new LineBufferedReader(stream);
            var beatmap = new TestWorkingBeatmap(Decoder.GetDecoder<Beatmap>(reader).Decode(reader));

            var diffCalc = ruleset.CreateDifficultyCalculator(beatmap);
            var diffAttr = diffCalc.Calculate(score.Mods);
            var perfCalc = ruleset.CreatePerformanceCalculator();
            var pp = perfCalc?.Calculate(score, diffAttr);
            return (score, pp);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to calculate pp for {score.BeatmapInfo} {score.Accuracy}");
            return (null, null);
        }
    })
    .Where(s => s.score != null && s.pp != null)
    .GroupBy(s => s.score.BeatmapInfo,
        s => s,
        (key, group) => group.MaxBy(s => s.pp.Total))
    .OrderByDescending(s => s.pp.Total)
    .ToList();

// var scores = new List<(ScoreInfo, double)>();
// {
//     WorkingBeatmap GetWorkingBeatmap(string hash)
//     {
//         using var stream = File.OpenRead($"{osuDir}/files/{hash[0]}/{hash[0..2]}/{hash}");
//         using var reader = new LineBufferedReader(stream);
//         return new TestWorkingBeatmap(Decoder.GetDecoder<Beatmap>(reader).Decode(reader));
//     }
//
//     var i = 0;
//     var count = personalBests.Values.Count;
//     var pbs = new List<(ScoreInfo, double)>();
//
//     foreach (var score in personalBests.Values)
//     {
//         try
//         {
//             var beatmap = GetWorkingBeatmap(score.BeatmapInfo.Hash);
//
//             var diffCalc = ruleset.CreateDifficultyCalculator(beatmap);
//             var diffAttr = diffCalc.Calculate(score.Mods);
//             var perfCalc = ruleset.CreatePerformanceCalculator();
//             var pp = perfCalc?.Calculate(score, diffAttr);
//
//             if (pp is not null)
//                 pbs.Add((score, pp.Total));
//         }
//         catch (Exception)
//         {
//             Console.WriteLine($"Failed to calculate pp for {score.BeatmapInfo} {score.Accuracy}");
//         }
//         finally
//         {
//             Console.Write($"\r{++i}/{count} scores processed");
//         }
//     }
//
//     Console.WriteLine();
//
//     scores = scores.OrderByDescending(s => s.Item2).ToList();
// }


{
    void PrintScore(ScoreInfo scoreInfo, double pp, int i1)
    {
        string TimeAgoString(DateTimeOffset dateTime)
        {
            var span = DateTimeOffset.Now.Subtract(dateTime);
            if (span.TotalDays < 0) return "time traveler";

            if (span.Hours < 1 && span.Days < 1) return "just now";
            if (span.Days < 1) return $"{span.Hours}h ago";
            if (span.Days < 7) return $"{span.Days}d ago";
            if (span.Days < 30) return $"{span.Days / 7}w ago";
            if (span.Days < 365) return $"{span.Days / 30}mo ago";

            return $"{span.Days / 365}y ago";
        }

        ConsoleColor RowColor(ScoreInfo score)
        {
            var span = DateTimeOffset.Now.Subtract(score.Date);
            if (span.Hours < 1 && span.Days < 1) return ConsoleColor.Magenta;
            if (span.Days < 1) return ConsoleColor.Red;
            if (span.Days < 7) return ConsoleColor.Yellow;
            if (span.Days < 30) return ConsoleColor.Green;
            return ConsoleColor.White;
        }

        var ppString = $"{pp:f0}pp";
        var accString = scoreInfo.Accuracy.FormatAccuracy();
        var timeString = TimeAgoString(scoreInfo.Date);
        var modString = scoreInfo.Mods.Length > 0 ? "+" + String.Join(',', scoreInfo.Mods.Select(m => m.Acronym)) : "";

        var misses = scoreInfo.Statistics[HitResult.Miss];

        var judgementsString = misses > 0 ? $"{scoreInfo.Statistics[HitResult.Miss]} miss" : "FC";

        Console.ForegroundColor = RowColor(scoreInfo);
        Console.WriteLine(
            $"{i1 + 1,5} | {ppString,6}, {accString,6} | {timeString,10} | {scoreInfo.BeatmapInfo.StarRating:f1}* | {judgementsString,10} | {scoreInfo.BeatmapInfo} {modString}");
        Console.ResetColor();
    }

    var weightedPp = 0d;
    var weightedAcc = 0d;

    for (int i = 0; i < scores.Count(); i++)
    {
        var score = scores[i];
        var weight = Math.Pow(0.95, i);
        weightedPp += score.pp.Total * weight / 20;
        weightedAcc += score.score.Accuracy * weight / 20;

        if (i < 25)
        {
            PrintScore(score.score, score.pp.Total, i);
        }
    }

    var weightedPpWithBonus = weightedPp + 416.6667d / 20;

    Console.WriteLine(
        $"{scores.Count()} filtered scores, {weightedPp:f2} avg pp, {weightedPp * 20:f2} total pp, {weightedPpWithBonus * 20:f2} total pp (bonus), {weightedAcc * 100:f2}% avg acc");

    async Task<double?> GetRankFromPp(double pp)
    {
        var rulesetId = new List<string> { "osu", "taiko", "catch", "mania" }.IndexOf(args[1]);
        if (!File.Exists(osuDailyApiKeyPath)) return null;
        var key = File.ReadAllText(osuDailyApiKeyPath).Trim();
        if (key.Length == 0) return null;
        var jsonString = await new HttpClient()
            .GetStringAsync($"https://osudaily.net/api/pp.php?k={key}" +
                            $"&t=pp" +
                            $"&v={weightedPp * 20}" +
                            $"&m={rulesetId}"
            );
        var rankString = JObject.Parse(jsonString)["rank"];
        if (rankString is null) return null;
        double rank;
        var ok = Double.TryParse(rankString.ToString(), out rank);
        return ok ? rank : null;
    }

    var rank = await GetRankFromPp(weightedPp * 20);
    if (rank is not null)
        Console.WriteLine($"Estimated rank: #{rank}");
}

Console.Write("Press any key to close");
Console.Read();