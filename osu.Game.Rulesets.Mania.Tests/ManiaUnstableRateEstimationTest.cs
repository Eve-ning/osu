// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using NUnit.Framework;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Scoring;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Mania.Tests
{
    public class ManiaUnstableRateEstimationTest
    {
        /// <summary>
        /// Samples JudgementCounts from a normal distribution with given unstable rate, mods, and overall difficulty.
        /// </summary>
        /// <param name="unstableRate">Unstable Rate to sample from</param>
        /// <param name="nNoteSamples"></param>
        /// <param name="nHoldSamples"></param>
        /// <param name="overallDifficulty">Overall Difficulty of the map</param>
        /// <param name="mods">Mods used during play</param>
        /// <param name="isHoldsLegacy">Whether to sample Holds from the legacy system</param>
        /// <param name="seed">Random Number Seed</param>
        /// <returns>An array of judgement counts. For Mania, there is 6, starting from 300G down to Miss</returns>
        public static SampleResult SampleJudgementCounts(
            double unstableRate,
            int nNoteSamples,
            int nHoldSamples,
            double overallDifficulty,
            Mod[] mods,
            bool isHoldsLegacy = false,
            int seed = 42
        )
        {
            // This converts a list of errors into a list of judgement counts.
            IEnumerable<int> errorsToJudgementCounts(double[] errors)
            {
                // Firstly, we prepare the hit windows.
                IEnumerable<double> hitWindows = ManiaPerformanceCalculator
                                                 .GetLazerHitWindows(mods, overallDifficulty)
                                                 .Prepend(0).Append(double.MaxValue); // We prepend 0 and append infinity to make sure we count the first and last judgement.

                // Next, we count the number of errors that fall within each hit window.
                // This is done by zipping the hit windows with the next hit window, and counting the number of errors that fall within that range.
                //
                // For example
                //    errors     = { -100, -50, 0, 50, 100 }
                //    hitWindows = { 0, 50, 100, 150, 200, infinity }
                // hitWindows.Zip(hitWindows.Skip(1), ... creates a list of tuples of adjacent hit windows [(0, 50), (50, 100), (100, 150), (150, 200), (200, infinity)]
                // errors.Count(e => Math.Abs(e) >= lower && Math.Abs(e) < upper) counts the number of errors that fall within the range of the hit window.
                return hitWindows.Zip(hitWindows.Skip(1), (lower, upper) => errors.Count(e => Math.Abs(e) >= lower && Math.Abs(e) < upper));
            }

            // This converts a list of errors into a list of judgement counts.
            IEnumerable<int> errorsToLazerHoldJudgementCounts(double[] errorsHead, double[] errorsTail)
            {
                IEnumerable<double> hitWindows = ManiaPerformanceCalculator
                                                 .GetLazerHitWindows(mods, overallDifficulty)
                                                 .Select(x => x * 1.5)
                                                 .Prepend(0).Append(double.MaxValue);

                IEnumerable<double> errors = errorsHead.Concat(errorsTail);
                return hitWindows.Zip(hitWindows.Skip(1), (lower, upper) => errors.Count(e => Math.Abs(e) >= lower && Math.Abs(e) < upper));
            }

            // This converts a list of errors into a list of judgement counts.
            IEnumerable<int> errorsToLegacyHoldJudgementCounts(double[] errorsHead, double[] errorsTail)
            {
                // The innerworkings are complex, thus we'll explain how this is evaluated.
                // However, it's not necessary to understand on a high-level.
                //
                // Legacy HitWindows follows the following table
                // MAX if Head Error <= MAX Hit Window * 1.2 AND Sum Error <= Max Hit Window * 2.4
                // 300                  300            * 1.1                  300            * 2.2
                // 200                  200            * 1.0                  200            * 2.0
                // 100                  100            * 1.0                  100            * 2.0
                // 50                   50             * 1.0                  50             * 2.0
                //
                // We implement this by
                // Evaluating a boolean array on the left and right conditions separately, where True satisfies the condition mentioned above
                // Taking the best judgement of each array by finding a True boolean with the highest judgement
                // Then taking the best judgement of both.

                // Firstly, we prepare the hit windows.
                IEnumerable<double> hitWindows = ManiaPerformanceCalculator
                                                 .GetLazerHitWindows(mods, overallDifficulty)
                                                 .Append(double.MaxValue) // Append Max Value for misses
                                                 .Zip(new[] { 1.2, 1.1, 1, 1, 1, 1 }, ((d0, d1) => d0 * d1))
                                                 .ToArray();

                // The Diff is the difference of each error, against all hit windows, for each error.
                // E.g. headDiff    contains errorsHead.Length no. of elements
                //      headDiff[0] contains hitWindows.Length no. of elements
                //      Where each element is the difference between the hitWindow and the error
                //      However, to simplify finding the best judgement, if the resulting difference is negative, it's set to double.MaxValue
                var headDiff = errorsHead.Select(
                    e => hitWindows.Select(hw => hw > Math.Abs(e) ? hw - Math.Abs(e) : double.MaxValue).ToList()
                ).ToArray();

                // We find the best judgement by finding the LastIndexOf the minimum difference.
                // E.g. hitWindows = 50, 100, 150
                //      abs(error) = 75, 250
                //      diff       = [dbl.max, 25, 75], [dbl.max, dbl.max, dbl.max]
                //      By taking the LAST index of the smallest element, we can find the judgement index
                int[] headBestJudgeIx = headDiff.Select(
                    e => e.LastIndexOf(e.Min())
                ).ToArray();

                double[] errorsSum = errorsHead.Zip(errorsTail, (h, t) => h + t).ToArray();
                var sumDiff = errorsSum.Select(
                    e => hitWindows.Select(hw => hw * 2 > Math.Abs(e) ? hw * 2 - Math.Abs(e) : double.MaxValue).ToList()
                ).ToArray();
                int[] sumBestJudgeIx = sumDiff.Select(
                    e => e.LastIndexOf(e.Min())
                ).ToArray();

                var judgeCountsMap = headBestJudgeIx
                                     .Zip(sumBestJudgeIx, Math.Max) // Find the worse judgement out of both
                                     .GroupBy(i => i, (k, group) => new { key = k, count = group.Count() }); // Group by each judgement and count

                int[] judgeCounts = new int[hitWindows.Count()];
                foreach (var m in judgeCountsMap) judgeCounts[m.key] = m.count;
                return judgeCounts;
            }

            double stdev = unstableRate / 10;
            var normdist = new Normal(0, stdev, new Random(seed));
            double[] noteErrors = normdist.Samples().Take(nNoteSamples).ToArray();
            double[] holdHeadErrors = normdist.Samples().Take(nHoldSamples).ToArray();
            double[] holdTailErrors = normdist.Samples().Take(nHoldSamples).ToArray();
            var judgementCountNotes = errorsToJudgementCounts(noteErrors);
            var judgementCountHolds = isHoldsLegacy
                ? errorsToLegacyHoldJudgementCounts(holdHeadErrors, holdTailErrors)
                : errorsToLazerHoldJudgementCounts(holdHeadErrors, holdTailErrors);
            int[] judgementCounts = judgementCountNotes.Zip(judgementCountHolds, (n, h) => n + h).ToArray();
            return new SampleResult()
            {
                JudgementCounts = judgementCounts,
                HoldHeadErrors = holdHeadErrors,
                HoldTailErrors = holdTailErrors,
                NoteErrors = noteErrors
            };
        }

        // General test to make sure UR estimation isn't changed by anything, inclusive of rate changing, within a margin of +-0.001 UR.
        // [TestCaseSource(nameof(SampleJudgementCounts))]
        [Test, Combinatorial]
        public void TestUnstableRate(
            [Values(500d, 50d)] double ur,
            [Values(100, 0)] int notes,
            [Values(100, 0)] int holds,
            [Values(10d, 0d)] double od,
            [Values(typeof(ManiaModDoubleTime), typeof(ManiaModHalfTime), null)] [CanBeNull]
            Type mod,
            [Values(true, false)] bool isHoldsLegacy
        )
        {
            if (notes == 0 && holds == 0) Assert.Ignore();

            var mods = new Mod[] { };

            if (mod != null) mods = mods.Append((Mod)Activator.CreateInstance(mod)).ToArray();
            if (isHoldsLegacy) mods = mods.Append(new ManiaModClassic()).ToArray();

            SampleResult sample = SampleJudgementCounts(ur, notes, holds, od, mods, isHoldsLegacy);
            double? estimatedUr = computeUnstableRate(
                new ManiaDifficultyAttributes
                {
                    Mods = mods,
                    HoldNoteCount = holds,
                    NoteCount = notes,
                    OverallDifficulty = od,
                },
                sample.JudgementCounts
            );

            Func<double[], string> formatErrors = er => string.Join(", ", er.Select(x => x.Round(1)));

            Assert.That(estimatedUr, Is.EqualTo(ur),
                $"Estimated UR {estimatedUr} != {ur}. \n"
                + $"Note Errors {formatErrors(sample.NoteErrors)} \n"
                + $"Head Errors {formatErrors(sample.HoldHeadErrors)} \n"
                + $"Tail Errors {formatErrors(sample.HoldTailErrors)}"
            );
        }

        // Ensure the UR estimation only returns null when it is supposed to.
        [TestCase(false, new[] { 1, 0, 0, 0, 0, 0 })]
        [TestCase(true, new[] { 0, 0, 0, 0, 0, 1 })]
        [TestCase(true, new[] { 0, 0, 0, 0, 0, 0 })]
        public void TestNullUnstableRate(bool expectedNullStatus, int[] judgementCounts)
        {
            DifficultyAttributes attributes = new ManiaDifficultyAttributes { NoteCount = 1, OverallDifficulty = 10 };

            double? estimatedUr = computeUnstableRate(attributes, judgementCounts);
            bool isNull = estimatedUr == null;

            // Platform-dependent math functions (Pow, Cbrt, Exp, etc) and advanced math functions (Erf, FindMinimum) may result in slight differences.
            Assert.That(isNull, Is.EqualTo(expectedNullStatus), "The estimated mania UR was/wasn't null.");
        }

        // Ensure the estimated deviation doesn't reach too high of a value in a single note situation, as a sanity check.
        [TestCase(new[] { 0, 0, 0, 0, 1, 0 })]
        public void TestSingleNoteBound(int[] judgementCounts)
        {
            DifficultyAttributes attributes = new ManiaDifficultyAttributes { NoteCount = 1, OverallDifficulty = 0 };

            double? estimatedUr = computeUnstableRate(attributes, judgementCounts);
            Assert.That(estimatedUr, Is.AtMost(10000.0), "The estimated mania UR returned too high for a single note.");
        }

        // Evaluates the Unstable Rate estimation of a beatmap with the given judgements.
        private double? computeUnstableRate(DifficultyAttributes attr, IReadOnlyList<int> judgementCounts, params Mod[] mods)
        {
            var judgements = new Dictionary<HitResult, int>
            {
                { HitResult.Perfect, judgementCounts[0] },
                { HitResult.Great, judgementCounts[1] },
                { HitResult.Good, judgementCounts[2] },
                { HitResult.Ok, judgementCounts[3] },
                { HitResult.Meh, judgementCounts[4] },
                { HitResult.Miss, judgementCounts[5] }
            };

            ManiaPerformanceAttributes perfAttributes = new ManiaPerformanceCalculator().Calculate(
                new ScoreInfo
                {
                    Mods = mods,
                    Statistics = judgements
                }, attr
            );

            return perfAttributes.EstimatedUr;
        }

        // TODO: @Natelytle Is this used for anything?
        protected void TestHitWindows(double overallDifficulty)
        {
            DifficultyAttributes attributes = new ManiaDifficultyAttributes { OverallDifficulty = overallDifficulty };

            var hitWindows = new ManiaHitWindows();
            hitWindows.SetDifficulty(overallDifficulty);

            double[] trueHitWindows =
            {
                hitWindows.WindowFor(HitResult.Perfect),
                hitWindows.WindowFor(HitResult.Great),
                hitWindows.WindowFor(HitResult.Good),
                hitWindows.WindowFor(HitResult.Ok),
                hitWindows.WindowFor(HitResult.Meh)
            };

            ManiaPerformanceAttributes perfAttributes = new ManiaPerformanceCalculator().Calculate(new ScoreInfo(), attributes);

            // Platform-dependent math functions (Pow, Cbrt, Exp, etc) may result in minute differences.
            Assert.That(perfAttributes.HitWindows, Is.EqualTo(trueHitWindows).Within(0.000001), "The true mania hit windows are different to the ones calculated in ManiaPerformanceCalculator.");
        }
    }

    public class SampleResult
    {
        public int[] JudgementCounts;
        public double[] NoteErrors;
        public double[] HoldHeadErrors;
        public double[] HoldTailErrors;
    }
}
