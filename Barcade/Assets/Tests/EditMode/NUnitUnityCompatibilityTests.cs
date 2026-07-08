using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-074 (follow-up to TASK-073) — closes the headless-NUnit vs
    /// Unity-NUnit divergence gap: the headless fast-tests.csproj references
    /// NuGet NUnit 3.14.0, but Unity's own Test Framework bundles a much older
    /// NUnit, so a construct only available in the newer package (e.g.
    /// <c>Is.AnyOf</c>, added NUnit 3.6) compiles and passes headless while
    /// breaking the ENTIRE Unity EditMode assembly with a CS0117 the moment
    /// Unity tries to compile the exact same source file.
    ///
    /// <para>
    /// <b>Exact bundled version (AC1).</b> This project's Unity Test Framework
    /// is <c>com.unity.test-framework 1.4.6</c> (pinned in
    /// <c>Barcade/Packages/manifest.json</c>), which depends on
    /// <c>com.unity.ext.nunit</c> (resolved to 2.0.5 per
    /// <c>Barcade/Packages/packages-lock.json</c>; Unity's own version number
    /// for its NUnit distribution, NOT the NUnit version itself). The actual
    /// bundled <c>nunit.framework.dll</c>
    /// (<c>Library/PackageCache/com.unity.ext.nunit@.../net40/unity-custom/</c>)
    /// reports <c>AssemblyVersion</c>/<c>FileVersion</c> <b>3.5.0.0</b> exactly
    /// — confirmed via <c>System.Reflection.AssemblyName.GetAssemblyName</c>
    /// and <c>FileVersionInfo.GetVersionInfo</c> on the actual DLL, not
    /// inferred from a changelog.
    /// </para>
    ///
    /// <para>
    /// <b>Why not Option A (pin the headless csproj to that exact version).</b>
    /// Tried and rejected: pinning fast-tests.csproj's <c>NUnit</c>
    /// PackageReference to NuGet's <c>3.5.0</c> release compiles clean (proving
    /// no OTHER post-3.5 construct is in use anywhere in this repo's linked
    /// EditMode sources beyond the already-removed <c>Is.AnyOf</c>), but FAILS
    /// AT RUNTIME under net8.0: NUnit 3.5.0 predates .NET Standard/.NET Core
    /// entirely (net45/net40-only assemblies), and the VSTest adapter throws
    /// <c>TypeLoadException</c> (missing
    /// <c>System.Runtime.Remoting.Messaging.ILogicalThreadAffinative</c>,
    /// <c>System.Web.UI.ICallbackEventHandler</c>) before a single test can
    /// even be discovered — the entire headless suite goes from 848 green to
    /// 0 collected. Not viable on this project's net8.0 runner.
    /// </para>
    ///
    /// <para>
    /// <b>Why not Option B (a CI Unity-compile step).</b> Both
    /// <c>.github/workflows/fast-tests.yml</c> and <c>slow-sweep.yml</c>
    /// already document, in their own header comments, that "there is no
    /// Unity license/runner available in this CI environment." Option B has no
    /// infrastructure to build on here — not merely a higher cost, an absent
    /// one.
    /// </para>
    ///
    /// <para>
    /// <b>Option C: this test.</b> A static denylist of known Unity-bundled-
    /// NUnit-incompatible constructs, textually scanned (not compiled) across
    /// every source file Unity's OWN Test Framework also compiles —
    /// <c>Assets/Tests/EditMode</c> AND <c>Assets/Tests/PlayMode</c> (PlayMode
    /// is included even though fast-tests.csproj never links those files
    /// in, since a PlayMode-only <c>Is.AnyOf</c> would still break under
    /// Unity's real bundled NUnit, and this is a plain text scan, not a
    /// compile — no UnityEngine reference needed). Starts with the one
    /// confirmed incident; extend <see cref="Denylist"/> the next time a real
    /// headless-passes/Unity-breaks divergence is found — a denylist is
    /// inherently incomplete (TASK-074 ticket notes), but cheap and immediately
    /// catches every REPEAT of a known-bad construct, which is exactly how
    /// this one was discovered (TASK-073).
    /// </para>
    ///
    /// <para>
    /// <c>Is.EquivalentTo</c> (used at
    /// <c>MicrogameDefinitionV2Tests.cs:262</c>) is NUnit 2.x-era and
    /// deliberately NOT on this list (TASK-073 review) — the denylist regexes
    /// below are constructed to be safe against wrongly matching similarly-
    /// named-but-unrelated members like it (see the exact word-boundary
    /// patterns).
    /// </para>
    /// </summary>
    [TestFixture]
    public class NUnitUnityCompatibilityTests
    {
        /// <summary>
        /// Known NUnit constructs unavailable in Unity's bundled NUnit 3.5.0.0.
        /// Each entry pairs a compiled regex with a human-readable name for the
        /// failure message. Add an entry here the next time a headless-passes/
        /// Unity-breaks divergence is discovered — see class doc.
        /// </summary>
        private static readonly (Regex Pattern, string Name)[] Denylist =
        {
            (new Regex(@"\bIs\.AnyOf\b", RegexOptions.Compiled), "Is.AnyOf (added NUnit 3.6 — TASK-073 incident)"),
        };

        [Test]
        public void EditModeAndPlayModeSources_NeverUseUnityIncompatibleNUnitConstructs()
        {
            var offenders = new List<string>();
            foreach (string dir in new[] { FindTestsSubdir("EditMode"), FindTestsSubdir("PlayMode") })
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string code = StripComments(File.ReadAllText(file));
                    foreach ((Regex pattern, string name) in Denylist)
                        if (pattern.IsMatch(code))
                            offenders.Add($"{Path.GetFileName(file)}: {name}");
                }

            Assert.That(offenders, Is.Empty,
                "TASK-074: these constructs compile and pass headless but are unavailable in Unity's bundled " +
                "NUnit 3.5.0.0 and will break the ENTIRE Unity EditMode/PlayMode assembly the moment Unity " +
                "recompiles the same source (CS0117-style) -- " + string.Join(", ", offenders));
        }

        /// <summary>Works from both the dotnet test bin dir and the Unity project CWD — same technique as Pcg32SeededRandomTests.FindCoreRuntimeDir.</summary>
        private static string FindTestsSubdir(string subfolder)
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory ?? Environment.CurrentDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Barcade", "Assets", "Tests", subfolder);
                if (Directory.Exists(candidate)) return candidate;
                string unityCandidate = Path.Combine(dir.FullName, "Assets", "Tests", subfolder);
                if (Directory.Exists(unityCandidate)) return unityCandidate;
                dir = dir.Parent;
            }
            throw new InvalidOperationException($"could not locate Assets/Tests/{subfolder} from " + Environment.CurrentDirectory);
        }

        /// <summary>Removes // line and /* block */ comments (and string literals, to dodge false positives) — identical to Pcg32SeededRandomTests.StripComments.</summary>
        private static string StripComments(string code)
        {
            code = Regex.Replace(code, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            code = Regex.Replace(code, @"//[^\r\n]*", " ");
            code = Regex.Replace(code, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
            return code;
        }
    }
}
