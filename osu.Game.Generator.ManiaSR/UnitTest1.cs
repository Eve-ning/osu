using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Tests.Beatmaps;
using System.Reflection;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Mania.Tests;
using osu.Game.Rulesets.Mods;
using FileInfo = osu.Game.IO.FileInfo;

#nullable disable
namespace osu.Game.Tests.Beatmaps
{
    [TestFixture]
    public class SRLoader : ManiaDifficultyCalculatorTest
    {
        private const string resource_namespace = "Beatmaps";
        protected override string ResourceAssembly => "osu.Game.Generator.ManiaSR";

        // [Test]
        protected double GetSr(string name, params Mod[] mods)
        {
            var attributes = CreateDifficultyCalculator(getBeatmap(name)).Calculate(mods);

            return attributes.StarRating;
        }

        [Test]
        public void TestSr()
        {
            double sr = GetSr("4118229");
            TestContext.WriteLine("SR: {0}", sr);
            int a = 3;
            int b = 1 + 2;
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void TestListFileName()
        {
            int i = 0;
            int maxFiles = 15;

            // Get all Resource names
            foreach (System.IO.FileInfo f in Directory.GetParent(Directory.GetCurrentDirectory())
                                                      .Parent.Parent
                                                      .EnumerateFiles("Resources/Beatmaps/*.osu"))
            {
                string beatmapId = f.Name;
                double srHt = GetSr(beatmapId, new ManiaModHalfTime());
                double srNt = GetSr(beatmapId);
                double srDt = GetSr(beatmapId, new ManiaModDoubleTime());
                TestContext.WriteLine($"Beatmap ID: {beatmapId} \t SR (HT/NT/DT): {srHt:.02} {srNt:.02} {srDt:.02}");

                if (i++ == maxFiles) break;
            }
        }

        private IWorkingBeatmap getBeatmap(string name)
        {
            using (var resStream = openResource($"{resource_namespace}.{name}"))
            using (var stream = new LineBufferedReader(resStream))
            {
                var decoder = Decoder.GetDecoder<Beatmap>(stream);

                ((LegacyBeatmapDecoder)decoder).ApplyOffsets = false;

                return new TestWorkingBeatmap(decoder.Decode(stream))
                {
                    BeatmapInfo =
                    {
                        Ruleset = CreateRuleset().RulesetInfo
                    }
                };
            }
        }

        private Stream openResource(string name)
        {
            string localPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).AsNonNull();
            return Assembly.LoadFrom(Path.Combine(localPath, $"{ResourceAssembly}.dll")).GetManifestResourceStream($@"{ResourceAssembly}.Resources.{name}");
            // return File.OpenRead(name);
        }
    }
}
