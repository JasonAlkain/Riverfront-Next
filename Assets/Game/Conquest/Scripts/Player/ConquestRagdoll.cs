using System.Collections.Generic;
using UnityEngine;

namespace Game.Conquest.Player
{
    /// <summary>
    /// Cosmetic death ragdoll. Each client copies the dead combatant's third-person body, turns the
    /// main bones into jointed rigidbodies, knocks it away from the killer and, after a few
    /// seconds, dissolves it: the body freezes, sinks into the ground and shrinks away. Nothing here is networked or affects gameplay: the copy is
    /// on the Ignore Raycast layer (shots and grenade checks pass through it) and never collides
    /// with characters, so every client may simulate its own fall.
    /// </summary>
    public class ConquestRagdoll : MonoBehaviour
    {
        // Bone, the bone it points at (for the capsule's length), radius, share of the mass, parent body.
        private struct BodyDef
        {
            public string bone, tip, parent;
            public float radius, mass;
            public BodyDef(string bone, string tip, float radius, float mass, string parent)
            {
                this.bone = bone; this.tip = tip; this.radius = radius; this.mass = mass; this.parent = parent;
            }
        }

        // Matches the template character's skeleton. A bone that isn't found is skipped.
        private static readonly BodyDef[] s_Bodies =
        {
            new BodyDef("Hips", "Spine", 0.15f, 0.20f, null),
            new BodyDef("Chest", "Neck", 0.16f, 0.25f, "Hips"),
            new BodyDef("Head", null, 0.12f, 0.08f, "Chest"),
            new BodyDef("Left_UpperArm", "Left_LowerArm", 0.06f, 0.04f, "Chest"),
            new BodyDef("Left_LowerArm", "Left_Hand", 0.05f, 0.03f, "Left_UpperArm"),
            new BodyDef("Right_UpperArm", "Right_LowerArm", 0.06f, 0.04f, "Chest"),
            new BodyDef("Right_LowerArm", "Right_Hand", 0.05f, 0.03f, "Right_UpperArm"),
            new BodyDef("Left_UpperLeg", "Left_LowerLeg", 0.08f, 0.09f, "Hips"),
            new BodyDef("Left_LowerLeg", "Left_Foot", 0.06f, 0.07f, "Left_UpperLeg"),
            new BodyDef("Right_UpperLeg", "Right_LowerLeg", 0.08f, 0.09f, "Hips"),
            new BodyDef("Right_LowerLeg", "Right_Foot", 0.06f, 0.07f, "Right_UpperLeg"),
        };

        // Placeholder: most ragdolls at once; the oldest goes early when a new one needs room.
        private const int MaxRagdolls = 16;
        private const float TotalMass = 70f;
        private const int IgnoreRaycastLayer = 2;

        private static readonly List<ConquestRagdoll> s_Active = new List<ConquestRagdoll>();
        private static readonly Dictionary<string, Transform> s_Bones = new Dictionary<string, Transform>();

        private float m_DissolveAt;
        private float m_DissolveSeconds;
        private bool m_Dissolving;
        private Vector3 m_StartScale;
        private Transform m_Hips;

        /// <summary>
        /// Copy <paramref name="body"/> (active or not) as a ragdoll, pushed by <paramref name="impulse"/>
        /// (m/s, applied to every part), that lies for <paramref name="lifetime"/> seconds and then
        /// dissolves over <paramref name="dissolveSeconds"/>. Returns false if the body has no skeleton.
        /// </summary>
        public static bool Spawn(GameObject body, Vector3 impulse, float lifetime, float dissolveSeconds)
        {
            if (body == null) return false;
            while (s_Active.Count >= MaxRagdolls) s_Active[0].Remove();

            Transform source = body.transform;
            GameObject copy = Instantiate(body, source.position, source.rotation);
            copy.name = body.name + " (Ragdoll)";
            copy.transform.localScale = source.lossyScale;
            Strip(copy);
            SetLayer(copy.transform, IgnoreRaycastLayer);

            s_Bones.Clear();
            Collect(copy.transform, s_Bones);
            if (!s_Bones.ContainsKey("Hips"))
            {
                Destroy(copy);
                return false;
            }

            var colliders = new List<Collider>();
            var rigidbodies = new Dictionary<string, Rigidbody>();
            foreach (BodyDef def in s_Bodies)
            {
                if (!s_Bones.TryGetValue(def.bone, out Transform bone)) continue;
                Rigidbody rb = bone.gameObject.AddComponent<Rigidbody>();
                rb.mass = TotalMass * def.mass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                colliders.Add(AddCollider(bone, def));
                rigidbodies[def.bone] = rb;

                if (def.parent != null && rigidbodies.TryGetValue(def.parent, out Rigidbody parentBody))
                {
                    var joint = bone.gameObject.AddComponent<CharacterJoint>();
                    joint.connectedBody = parentBody;
                    joint.enableProjection = true;
                    joint.lowTwistLimit = new SoftJointLimit { limit = -30f };
                    joint.highTwistLimit = new SoftJointLimit { limit = 30f };
                    joint.swing1Limit = new SoftJointLimit { limit = 50f };
                    joint.swing2Limit = new SoftJointLimit { limit = 30f };
                }
            }

            // Never trip over or block a live character.
            var all = ConquestPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var controller = all[i] != null ? all[i].GetComponent<CharacterController>() : null;
                if (controller == null) continue;
                for (int c = 0; c < colliders.Count; c++) Physics.IgnoreCollision(colliders[c], controller);
            }

            foreach (var rb in rigidbodies.Values) rb.linearVelocity = impulse;

            var ragdoll = copy.AddComponent<ConquestRagdoll>();
            ragdoll.m_DissolveAt = Time.time + lifetime;
            ragdoll.m_DissolveSeconds = Mathf.Max(0.01f, dissolveSeconds);
            ragdoll.m_Hips = s_Bones["Hips"];
            s_Active.Add(ragdoll);
            copy.SetActive(true);
            return true;
        }

        // A copy keeps only what draws: no animation, scripts, sounds or running effects.
        private static void Strip(GameObject copy)
        {
            foreach (var animator in copy.GetComponentsInChildren<Animator>(true)) DestroyImmediate(animator);
            foreach (var script in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(script);
            foreach (var audio in copy.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(audio);
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            foreach (var particles in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.gameObject.SetActive(false);
            }
            // Bones now move by physics, far from the mesh's animated bounds: keep it from culling.
            foreach (var skinned in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.updateWhenOffscreen = true;
            }
        }

        private static Collider AddCollider(Transform bone, BodyDef def)
        {
            if (def.tip == null || !s_Bones.TryGetValue(def.tip, out Transform tip))
            {
                var sphere = bone.gameObject.AddComponent<SphereCollider>();
                sphere.radius = def.radius;
                sphere.center = Vector3.up * def.radius * 0.5f;
                return sphere;
            }

            // A capsule from the bone to the next one, along whichever local axis points there.
            Vector3 local = bone.InverseTransformPoint(tip.position);
            Vector3 abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            int axis = abs.x > abs.y ? (abs.x > abs.z ? 0 : 2) : (abs.y > abs.z ? 1 : 2);
            var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = axis;
            capsule.radius = def.radius;
            capsule.height = local.magnitude + def.radius;
            capsule.center = local * 0.5f;
            return capsule;
        }

        private static void Collect(Transform t, Dictionary<string, Transform> bones)
        {
            if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            foreach (Transform child in t) Collect(child, bones);
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }

        // Placeholder: metres the body sinks while it dissolves.
        private const float DissolveSink = 0.4f;

        private void Update()
        {
            if (Time.time < m_DissolveAt) return;
            if (!m_Dissolving)
            {
                // Freeze where it lies, so the whole body can sink and shrink as one.
                m_Dissolving = true;
                foreach (var rb in GetComponentsInChildren<Rigidbody>())
                {
                    rb.interpolation = RigidbodyInterpolation.None;
                    rb.isKinematic = true;
                }
                // Sink and shrink around the hips, where the body lies, not around the copy's root.
                if (m_Hips != null)
                {
                    Vector3 pivot = m_Hips.position;
                    m_Hips.SetParent(null, true);
                    transform.position = pivot;
                    m_Hips.SetParent(transform, true);
                }
                m_StartScale = transform.localScale;
            }

            float t = (Time.time - m_DissolveAt) / m_DissolveSeconds;
            if (t >= 1f)
            {
                Remove();
                return;
            }
            float step = DissolveSink * Time.deltaTime / m_DissolveSeconds;
            transform.position += Vector3.down * step;
            transform.localScale = m_StartScale * (1f - t * t);
        }

        private void Remove()
        {
            s_Active.Remove(this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Scene unload or match end destroys it without Remove.
            s_Active.Remove(this);
        }
    }
}
