using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

namespace Barcade.Framework
{
    /// <summary>
    /// Entry point MonoBehaviour — lives in Boot.unity.
    /// Loads Manager.unity additively, then destroys itself.
    /// Keeps all rules/scoring in Barcade.Core plain C# classes (thin-MB pattern).
    /// </summary>
    public class GameBootstrapper : MonoBehaviour
    {
        [SerializeField] private string _managerScene = "Manager";

        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);

            var op = SceneManager.LoadSceneAsync(_managerScene, LoadSceneMode.Additive);
            yield return op;

            SceneManager.SetActiveScene(SceneManager.GetSceneByName(_managerScene));
            Destroy(gameObject);
        }
    }
}
