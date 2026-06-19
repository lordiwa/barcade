using System.Collections.Generic;
using UnityEngine;

namespace Barcade.Framework
{
    /// <summary>
    /// A serialized ordered list of <see cref="MicrogameDefinition"/> assets that
    /// <see cref="MicrogameLoopController"/> can load as the pool for a session.
    ///
    /// Create assets via: Assets > Create > Barcade > MicrogamePool.
    ///
    /// The orchestrator generates <c>Assets/Barcade/Content/DefaultMicrogamePool.asset</c>
    /// from all 12+ definitions via
    /// <c>-executeMethod Barcade.EditorTools.MicrogameContentGenerator.GenerateAll</c>.
    ///
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    [CreateAssetMenu(
        fileName = "MicrogamePool",
        menuName  = "Barcade/MicrogamePool")]
    public class MicrogamePool : ScriptableObject
    {
        [Tooltip("Ordered list of microgame definitions available for selection.")]
        public List<MicrogameDefinition> definitions = new List<MicrogameDefinition>();
    }
}
