using osu.Game.Scoring;
using Realms;

var realm = Realm.GetInstance(new RealmConfiguration(@"D:\Games\osu-lazer\client - Copy.realm")
{
    SchemaVersion = 25 // relates to RealmAccess.schema_version
});

Console.WriteLine($"{realm.All<ScoreInfo>().Count()} scores in database");
