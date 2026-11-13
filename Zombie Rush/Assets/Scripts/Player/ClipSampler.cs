using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ZombieRush.Player
{
    /// Poses an object from an animation clip at a given time (the gun-hold body clips and first-person
    /// arms). AnimationClip.SampleAnimation only works for these clips in the Editor; a built game needs a
    /// playable graph. One manual graph per object, one clip playable per clip, evaluated on demand.
    public static class ClipSampler
    {
        class Entry
        {
            public PlayableGraph graph;
            public AnimationPlayableOutput output;
            public readonly Dictionary<AnimationClip, AnimationClipPlayable> clips = new Dictionary<AnimationClip, AnimationClipPlayable>();
        }

        static readonly Dictionary<GameObject, Entry> entries = new Dictionary<GameObject, Entry>();

        public static void Sample(GameObject go, AnimationClip clip, float time)
        {
            if (go == null || clip == null) return;
#if UNITY_EDITOR
            clip.SampleAnimation(go, time);
#else
            if (!entries.TryGetValue(go, out var e))
            {
                Cleanup();
                var animator = go.GetComponent<Animator>();
                if (animator == null) animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;   // nothing plays by itself
                animator.enabled = true;
                e = new Entry { graph = PlayableGraph.Create("ClipSampler_" + go.name) };
                e.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                e.output = AnimationPlayableOutput.Create(e.graph, "pose", animator);
                entries[go] = e;
            }
            if (!e.clips.TryGetValue(clip, out var playable))
            {
                playable = AnimationClipPlayable.Create(e.graph, clip);
                playable.SetApplyFootIK(false);
                e.clips[clip] = playable;
            }
            e.output.SetSourcePlayable(playable);
            playable.SetTime(time);
            e.graph.Evaluate(0f);
#endif
        }

        /// Drops the graphs of objects that no longer exist.
        static void Cleanup()
        {
            List<GameObject> dead = null;
            foreach (var kv in entries)
                if (kv.Key == null) { if (kv.Value.graph.IsValid()) kv.Value.graph.Destroy(); (dead ??= new List<GameObject>()).Add(kv.Key); }
            if (dead != null) foreach (var d in dead) entries.Remove(d);
        }
    }
}
