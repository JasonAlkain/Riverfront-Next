using Game.Conquest.Build;
using NUnit.Framework;
using UnityEditor.SceneManagement;

namespace Game.Conquest.Tests
{
    /// <summary>Runs the map validator as part of the EditMode suite, so CI fails on a broken map.</summary>
    public class ConquestMapValidatorTests
    {
        [Test]
        public void EveryRegisteredMapPasses()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            ConquestMapValidator.Report report;
            try
            {
                report = ConquestMapValidator.Run();
            }
            finally
            {
                // The validator opens scenes; put back whatever was open before.
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            Assert.That(report.MapsChecked, Is.GreaterThan(0), "no maps were checked");
            Assert.That(report.Errors, Is.Empty, string.Join("\n", report.Errors));
        }
    }
}
