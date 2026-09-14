using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Unite.Tests
{
    /// <summary>
    /// Guards the shipped scene assets independently of Play Mode. These checks
    /// do not replace rendering and trial-end smoke tests in a licensed editor.
    /// </summary>
    public class EveryMoveSceneTests
    {
        private const string Root = "Assets/UNITE/Demo/every-move-you-make/";

        [TestCase("baseline", "NoAssistance")]
        [TestCase("network", "CommandTimelineAssistance")]
        [TestCase("path", "IdealTrajectoryAssistance")]
        [TestCase("envelope", "WorstCaseEnvelopeAssistance")]
        public void ConditionsHaveSameSharedSerializedValues(string condition, string type)
        {
            string baseline = File.ReadAllText(Root + "Scenes/condition-baseline.unity");
            string scene = File.ReadAllText(Root + "Scenes/condition-" + condition + ".unity");
            CollectionAssert.AreEqual(Normalize(baseline, "NoAssistance"), Normalize(scene, type),
                "Only the selected assistance type may change between conditions.");
        }

        [TestCase("baseline")]
        [TestCase("network")]
        [TestCase("path")]
        [TestCase("envelope")]
        public void VehicleBindingsReferToCorrectSerializedComponentTypes(string condition)
        {
            string scene = File.ReadAllText(Root + "Scenes/condition-" + condition + ".unity");
            Assert.That(Regex.Matches(scene, @"(?m)^  m_Name: TeleroboticsAgent\r?$").Count, Is.EqualTo(1));
            string vehicle = Regex.Match(scene,
                @"m_EditorClassIdentifier: [^\r\n]*\.TurtleBot3WafflePiEnhanced\r?\n(?:(?!--- !u!)[\s\S])*").Value;
            Assert.That(vehicle, Is.Not.Empty);
            foreach (string field in new[] { "robot", "robotMesh", "robotCamera", "cameraSensor", "terrain" })
            {
                string id = Regex.Match(vehicle, @"(?m)^  " + field + @": \{fileID: (\d+)\}").Groups[1].Value;
                Assert.That(id, Is.Not.Empty.And.Not.EqualTo("0"), field);
                string unityType = field == "cameraSensor" ? "20" : field == "terrain" ? "64" : "4";
                Assert.That(scene, Does.Match(@"(?m)^--- !u!" + unityType + " &" + id + @"(?: stripped)?\r?$"),
                    field + " must reference the correct component type, not its GameObject or Camera.");
            }
        }

        private static string[] Normalize(string scene, string assistanceType)
        {
            string meta = File.ReadAllText(Root + "Runtime/" + assistanceType + ".cs.meta");
            string guid = Regex.Match(meta, @"guid: ([0-9a-f]{32})").Groups[1].Value;
            Assert.That(guid, Is.Not.Empty);
            scene = scene.Replace(guid, "ASSISTANCE_GUID").Replace(assistanceType, "ASSISTANCE_TYPE");
            // Unity may reorder component declarations and serialized fields on save.
            // Compare the full multiset of values; binding types are checked separately.
            return scene.Split('\n').Select(line => line.TrimEnd('\r')).OrderBy(line => line, StringComparer.Ordinal).ToArray();
        }
    }
}
