using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI
{
    /// An invisible raycast target: receives touches/clicks but draws nothing,
    /// so large hit areas (look pad, swap area) cost zero overdraw.
    [RequireComponent(typeof(CanvasRenderer))]
    public class EmptyGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }
}
