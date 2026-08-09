using UnityEngine;
using Pho.Core.Interaction;

namespace Pho.Player
{
    /// <summary>
    /// Plain (non-MonoBehaviour) helper owned and ticked by PlayerHandSlot:
    /// while something is held, raycasts from the player's eye each frame
    /// and shows a translucent preview of where the held item would land if
    /// placed right now -- green when the ray hits an IPlacementSurface and
    /// TryGetPlacement succeeds there, red otherwise (no surface hit at
    /// all, or the surface rejects the pose) -- per architecture.md §5:
    /// "Placement = IPlacementSurface returns a snapped pose ... a ghost
    /// material shows valid/invalid."
    ///
    /// Deliberately simple per the M3 scope note ("don't build a full
    /// shader graph ghost effect"): one runtime-instanced
    /// MeshFilter/MeshRenderer reusing the held item's own mesh (falling
    /// back to a plain cube if the item has none), one runtime-instanced
    /// material (created via `.material`, never `.sharedMaterial`, so nothing
    /// shared gets mutated) whose main color is toggled between two tints.
    ///
    /// Not visually verified in-Editor -- this pass is written under the
    /// "no Unity" constraint (see the calling agent's brief). The
    /// URP-Unlit transparency property names (_Surface/_Blend/_SrcBlend/...)
    /// match Unity 6's URP Unlit shader as of authoring time; every URP-only
    /// property set is guarded by HasProperty so a shader mismatch degrades
    /// to an opaque-but-still-correctly-colored ghost instead of an error.
    /// Flag this for a visual check in the integration pass.
    /// </summary>
    public sealed class CarryGhostPreview
    {
        static readonly Color ValidColor = new Color(0.2f, 1f, 0.3f, 0.35f);
        static readonly Color InvalidColor = new Color(1f, 0.2f, 0.2f, 0.35f);

        readonly Transform _owner;
        readonly float _range;
        readonly LayerMask _mask;

        GameObject _ghostGo;
        MeshFilter _meshFilter;
        MeshRenderer _meshRenderer;
        Material _ghostMaterial;
        Mesh _fallbackCubeMesh;

        public CarryGhostPreview(Transform owner, float range, LayerMask mask)
        {
            _owner = owner;
            _range = Mathf.Max(0.1f, range);
            _mask = mask;
        }

        /// <summary>Call once per frame (PlayerHandSlot.LateUpdate). Hides itself when nothing is held or nothing is hit.</summary>
        public void Tick(IHoldable held, Transform eye)
        {
            if (held == null || eye == null)
            {
                SetVisible(false);
                return;
            }

            EnsureGhostObject(held);

            bool hitSomething = Physics.Raycast(
                eye.position, eye.forward, out var hit, _range,
                _mask, QueryTriggerInteraction.Ignore);

            if (!hitSomething)
            {
                SetVisible(false);
                return;
            }

            var surface = hit.collider.GetComponentInParent<IPlacementSurface>();
            bool valid = surface != null && surface.TryGetPlacement(hit.point, held, out var pose);

            SetVisible(true);

            if (valid)
            {
                _ghostGo.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }
            else
            {
                _ghostGo.transform.SetPositionAndRotation(hit.point, held.Transform.rotation);
            }

            _ghostMaterial.color = valid ? ValidColor : InvalidColor;
        }

        public void Hide() => SetVisible(false);

        void SetVisible(bool visible)
        {
            if (_ghostGo != null) _ghostGo.SetActive(visible);
        }

        void EnsureGhostObject(IHoldable held)
        {
            if (_ghostGo == null)
            {
                _ghostGo = new GameObject("CarryGhostPreview (auto)");
                _ghostGo.transform.SetParent(_owner, false);

                _meshFilter = _ghostGo.AddComponent<MeshFilter>();
                _meshRenderer = _ghostGo.AddComponent<MeshRenderer>();
                _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _meshRenderer.receiveShadows = false;

                _ghostMaterial = CreateGhostMaterial();
                _meshRenderer.material = _ghostMaterial; // ".material" (not sharedMaterial) instances it for us.
            }

            var sourceFilter = held.Transform.GetComponentInChildren<MeshFilter>();
            _meshFilter.sharedMesh = sourceFilter != null && sourceFilter.sharedMesh != null
                ? sourceFilter.sharedMesh
                : GetFallbackCubeMesh();

            _ghostGo.transform.localScale = held.Transform.lossyScale;
        }

        Mesh GetFallbackCubeMesh()
        {
            if (_fallbackCubeMesh == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _fallbackCubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.Destroy(temp);
            }
            return _fallbackCubeMesh;
        }

        static Material CreateGhostMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = "CarryGhostPreview (instanced)" };

            if (mat.HasProperty("_Surface"))
            {
                // URP Unlit transparency recipe -- see class doc comment re: not visually verified.
                mat.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent
                mat.SetFloat("_Blend", 0f);   // 0 = Alpha
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            return mat;
        }
    }
}
